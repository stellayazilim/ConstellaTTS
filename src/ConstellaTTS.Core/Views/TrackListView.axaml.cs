using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.Core.Logging;
using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.SDK;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Timeline;
using ConstellaTTS.SDK.UI.Animation;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.UI.Tools;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Track list + timeline view. Handles two drag gestures over a single
/// pointer capture, dispatched in OnPressed based on where and how the
/// press happened:
///
///   · REORDER — press on the left 200 px header of any row. Floating
///     cursor-attached preview + three-stage release animation. Always
///     available regardless of tool mode.
///
///   · CREATE — press on the canvas area (X ≥ 200 px) with the Create
///     tool active, or while holding Ctrl (= Section) / Ctrl+Shift
///     (= Stage) as a transient override from Select mode. Paints a
///     preview rectangle; on release creates a Section or Stage block
///     on the hit track and appends it to that track's collection.
///
///   · SELECT — press on the canvas with Select tool active and no
///     Ctrl override. Hit-tests against the track's blocks; on hit
///     selects (editor opens via selection observer), on miss clears.
///     No drag.
///
/// Block geometry lives in the time domain (StartSec / DurationSec).
/// Pixel coords are viewport projections — convert at the gesture
/// boundary and store only time on the VM. Collision is checked in
/// the time domain against existing blocks on the same track, so
/// clamp logic is zoom-independent.
///
/// Ctrl-preview: while the pointer hovers the canvas AND Ctrl is held,
/// this view writes PreviewTool=Create / PreviewCreateType={Section|Stage}
/// to IToolModeService. The context bar's buttons bind to EffectiveTool/
/// CreateType and light up accordingly without actually committing the
/// mode change. Clearing happens on pointer leave or when the modifier
/// goes away during a move.
/// </summary>
public partial class TrackListView : UserControl
{
    private const int    RowHeight         = 56;
    private const int    HeaderWidth       = 200;
    private const int    DragThresholdPx   = 5;
    private const double MinCreateDurSec   = 0.2; // reject drags shorter than 200 ms

    // Cursor-anchored zoom tuning.
    private const double ZoomFactorPerNotch = 1.15;
    private const double MinPxPerSec        = 4;
    private const double MaxPxPerSec        = 400;

    // Shift+Scroll horizontal time-scroll: seconds advanced per unit of
    // wheel delta. Tuned so one physical notch (~1.0) steps about 2 s.
    private const double ScrollStepSec = 2;

    // Reorder animation tuning — short, sequential-phase feel.
    private static readonly TimeSpan CloseSourceDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan OpenTargetDuration  = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeInDuration      = TimeSpan.FromMilliseconds(180);

    private enum DragKind { None, Reorder, Create }

    private readonly IToolModeService  _toolMode;
    private readonly ITimelineViewport _viewport;
    private readonly IHistoryManager   _history;
    private readonly ISelectionService _selection;
    private readonly ILogger           _log;

    // Shared drag state
    private DragKind _dragKind;
    private bool     _isDragging;
    private bool     _isAnimating;
    private Point    _dragStart;

    // Reorder-specific
    private ITrackViewModel? _dragging;
    private double           _clickOffsetY;

    // Create-specific (time domain)
    private ITrackViewModel? _createTrack;
    private int              _createTrackIdx;
    private double           _createAnchorSec;
    private CreateType       _createType;
    private double           _createClampMinSec;

    // Re-entry guard for OnCaptureLost vs OnReleased's Capture(null).
    private bool _inReleaseHandler;

    // Cached last pointer position in items-coord space, kept in sync by
    // OnMoved and invalidated on OnPointerExited. Consumed by the key
    // listener so a Ctrl press (no cursor movement) can re-evaluate the
    // tool preview using the cursor's current location — otherwise the
    // preview would only light up after the user wiggled the mouse.
    private Point? _lastPointerInItems;

    // TopLevel we're currently subscribed to for global key events. Stored
    // so we can detach the same instance we attached to; if the control is
    // ever re-parented across windows the pair stays consistent.
    private TopLevel? _keyListenerTopLevel;

    public TrackListView(
        IToolModeService  toolMode,
        ITimelineViewport viewport,
        IHistoryManager   history,
        ISelectionService selection,
        ILoggerFactory    loggerFactory)
    {
        _toolMode  = toolMode;
        _viewport  = viewport;
        _history   = history;
        _selection = selection;
        _log       = loggerFactory.CreateLogger(LogCategory.WindowProcess);
        InitializeComponent();
        Setup();
    }

    private TrackListViewModel? Vm => DataContext as TrackListViewModel;

    private void Setup()
    {
        PointerPressed      += OnPressed;
        PointerMoved        += OnMoved;
        PointerReleased     += OnReleased;
        PointerCaptureLost  += OnCaptureLost;
        PointerWheelChanged += OnWheel;
        PointerExited       += OnPointerExited;

        _selection.PropertyChanged += OnSelectionChanged;
        _viewport.PropertyChanged  += OnViewportChanged;

        BlockEditorCloseButton.Click     += (_, _) => CloseBlockEditor();
        BlockEditorLabelText.TextChanged += OnBlockEditorLabelChanged;

        DataContextChanged += (_, _) => BindMinimap();
        BindMinimap();
    }

    /// <summary>
    /// Hook global key events from the owning <see cref="TopLevel"/> so
    /// Ctrl presses re-evaluate the Ctrl-preview even when the user hasn't
    /// wiggled the mouse. UserControls don't reliably receive keyboard
    /// focus, so listening at the window level is the straightforward
    /// path — the alternative (keeping focus here) would fight with
    /// TextBox focus inside the block editor.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _keyListenerTopLevel = TopLevel.GetTopLevel(this);
        if (_keyListenerTopLevel is not null)
        {
            _keyListenerTopLevel.KeyDown += OnKeyStateChanged;
            _keyListenerTopLevel.KeyUp   += OnKeyStateChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_keyListenerTopLevel is not null)
        {
            _keyListenerTopLevel.KeyDown -= OnKeyStateChanged;
            _keyListenerTopLevel.KeyUp   -= OnKeyStateChanged;
            _keyListenerTopLevel = null;
        }
    }

    /// <summary>
    /// Reacts to Ctrl / Shift transitions by re-running the preview logic
    /// against the cached pointer position. No cached position (pointer
    /// outside the view) → nothing to preview, skip. Non-modifier keys are
    /// ignored; we don't want Alt / Tab / letter keys spuriously clearing
    /// the preview.
    /// </summary>
    private void OnKeyStateChanged(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl
                       or Key.LeftShift or Key.RightShift))
            return;

        if (_lastPointerInItems is null) return;
        UpdateToolPreview(_lastPointerInItems.Value, e.KeyModifiers);
    }

    private void BindMinimap()
    {
        Minimap?.SetTracks(Vm?.Tracks);
    }

    // ── Block editor overlay ─────────────────────────────────────────────

    private bool _suppressLabelEcho;

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshBlockEditor();
    }

    private void OnViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!BlockEditor.IsVisible) return;
        PositionBlockEditor();
    }

    private void RefreshBlockEditor()
    {
        var block = _selection.SelectedBlock;
        var track = _selection.SelectedTrack;

        if (block is null || track is null)
        {
            BlockEditor.IsVisible = false;
            return;
        }

        BlockEditor.IsVisible     = true;
        BlockEditor.BorderBrush   = new SolidColorBrush(Color.Parse(track.Color));

        BlockEditorDurationText.Text = $"⏱ {FormatDuration(block.DurationSec)}";
        BlockEditorStartText.Text    = $"▶ {FormatDuration(block.StartSec)}";
        BlockEditorEndText.Text      = $"⏹ {FormatDuration(block.EndSec)}";
        BlockEditorKindText.Text     = block is ISectionViewModel
            ? "SECTION · " + track.Name.ToUpperInvariant()
            : "STAGE · "   + track.Name.ToUpperInvariant();

        _suppressLabelEcho        = true;
        BlockEditorLabelText.Text = block.Label;
        _suppressLabelEcho        = false;

        PositionBlockEditor();
    }

    /// <summary>
    /// Anchor the editor to the selected block's time position. Three
    /// clamping rules, applied in order:
    ///
    ///   1. Block fully off-screen (end before viewport start, or start
    ///      past viewport end) → close the editor. The "collapse on
    ///      hit" behaviour Kerim asked for — scrolling the block out
    ///      of view dismisses the overlay rather than dragging it along
    ///      with some out-of-sight anchor.
    ///
    ///   2. Right spill — editor would extend past the tracks panel's
    ///      right edge → pull left so the whole editor stays visible.
    ///
    ///   3. Left spill — editor's left edge would land inside the left
    ///      header column → push right so the editor never covers the
    ///      track name panel. Without this clamp, scrolling right made
    ///      the editor creep over the header area.
    /// </summary>
    private void PositionBlockEditor()
    {
        var track = _selection.SelectedTrack;
        var block = _selection.SelectedBlock;
        if (track is null || block is null) return;

        var tracksList = Vm?.Tracks;
        if (tracksList is null) return;

        var trackIdx = tracksList.IndexOf(track);
        if (trackIdx < 0) return;

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

        // (1) Viewport-bounds check. Close if the block has scrolled off.
        var available  = items.Bounds.Width;
        var visibleSec = (available - HeaderWidth) / Math.Max(0.001, _viewport.PxPerSec);
        var viewStart  = _viewport.ScrollOffsetSec;
        var viewEnd    = viewStart + visibleSec;
        if (block.EndSec < viewStart || block.StartSec > viewEnd)
        {
            CloseBlockEditor();
            return;
        }

        var leftPx = HeaderWidth + _viewport.TimeToPx(block.StartSec);
        var topPx  = trackIdx * RowHeight + RowHeight;

        const double EditorWidth = 360;

        // (2) Right clamp.
        if (leftPx + EditorWidth > available)
            leftPx = available - EditorWidth - 8;

        // (3) Left clamp — never overlap the track-header column.
        if (leftPx < HeaderWidth + 8)
            leftPx = HeaderWidth + 8;

        BlockEditor.Margin = new Thickness(leftPx, topPx, 0, 0);
    }

    private void OnBlockEditorLabelChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressLabelEcho) return;
        var block = _selection.SelectedBlock;
        if (block is null) return;
        block.Label = BlockEditorLabelText.Text ?? string.Empty;
    }

    private void CloseBlockEditor()
    {
        _selection.SelectedBlock = null;
        _selection.SelectedTrack = null;
    }

    /// <summary>
    /// Hit-test a point (in this UserControl's coord space) against the
    /// currently-open block editor overlay. Used to veto canvas gestures
    /// that would otherwise fire from events bubbling out of the editor —
    /// a Ctrl+click on an editor text box shouldn't start a create
    /// gesture, and an empty-space click inside the editor shouldn't
    /// clear selection and close the editor out from under the user.
    /// </summary>
    private bool IsInsideBlockEditor(Point posInTrackListView)
    {
        if (!BlockEditor.IsVisible) return false;
        var b = BlockEditor.Bounds;
        return posInTrackListView.X >= b.Left && posInTrackListView.X <= b.Right
            && posInTrackListView.Y >= b.Top  && posInTrackListView.Y <= b.Bottom;
    }

    /// <summary>
    /// Returns (track, block) at the given items-coord point. Track is
    /// non-null whenever the Y coord lands inside a valid row; block is
    /// non-null only when the X coord also lands inside an existing
    /// block's [StartSec, EndSec] span.
    /// </summary>
    private (ITrackViewModel? track, IStageViewModel? block) HitBlock(Point posInItems)
    {
        var tracks = Vm?.Tracks;
        if (tracks is null || tracks.Count == 0) return (null, null);

        var trackIdx = (int)(posInItems.Y / RowHeight);
        if (trackIdx < 0 || trackIdx >= tracks.Count) return (null, null);

        var track = tracks[trackIdx];
        if (posInItems.X < HeaderWidth) return (track, null);

        var timeSec = _viewport.PxToTime(posInItems.X - HeaderWidth);
        foreach (var b in track.Sections)
            if (timeSec >= b.StartSec && timeSec <= b.EndSec)
                return (track, b);
        return (track, null);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_inReleaseHandler) return;
        if (_dragKind == DragKind.None) return;

        _log.LogDebug($"[CAPTURE-LOST] _dragKind={_dragKind} _isDragging={_isDragging}");

        EndReorderVisuals();
        ClearCreateVisuals();
        Reset();
    }

    /// <summary>
    /// Pointer left the control — any active Ctrl-preview override is
    /// now stale. Clearing here is required because PointerMoved stops
    /// firing once the pointer is outside, so the preview would stick
    /// on until the user re-entered. The cached pointer position is also
    /// invalidated so a Ctrl press outside the canvas doesn't light the
    /// preview on a stale coordinate.
    /// </summary>
    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _lastPointerInItems = null;
        ClearToolPreview();
    }

    // ── Wheel ────────────────────────────────────────────────────────────

    /// <summary>
    /// Routes mouse-wheel input based on modifier keys.
    ///   • Ctrl  → zoom the timeline, anchored at the pointer.
    ///   • Shift → unhandled; ScrollViewer does the vertical track scroll.
    ///   • None  → horizontal time-scroll (DAW convention).
    /// </summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

        var pos = e.GetPosition(items);
        if (pos.X < HeaderWidth) return;

        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl)
        {
            ApplyZoomAtCursor(pos, e.Delta.Y);
            e.Handled = true;
            return;
        }

        if (shift) return; // let ScrollViewer do vertical scroll

        var newOffset = _viewport.ScrollOffsetSec - (e.Delta.Y * ScrollStepSec);
        _viewport.ScrollOffsetSec = Math.Max(0, newOffset);
        e.Handled = true;
    }

    private void ApplyZoomAtCursor(Point posInItems, double wheelDeltaY)
    {
        var canvasX      = posInItems.X - HeaderWidth;
        var timeAtCursor = _viewport.PxToTime(canvasX);

        var factor      = wheelDeltaY > 0 ? ZoomFactorPerNotch : 1.0 / ZoomFactorPerNotch;
        var newPxPerSec = Math.Clamp(_viewport.PxPerSec * factor, MinPxPerSec, MaxPxPerSec);

        var newScrollOffsetSec = Math.Max(0, timeAtCursor - (canvasX / newPxPerSec));

        _viewport.PxPerSec        = newPxPerSec;
        _viewport.ScrollOffsetSec = newScrollOffsetSec;
    }

    // ── Tool preview (Ctrl hover) ────────────────────────────────────────

    /// <summary>
    /// Sync the tool-mode preview with (cursor location, modifiers). Only
    /// the canvas area (items X ≥ HeaderWidth, Y within tracks) counts —
    /// hovering the left header column, minimap, or toolbar doesn't
    /// pre-light the create buttons.
    ///
    /// Idempotent: writes null→null without raising spurious change
    /// notifications thanks to the service's ObservableProperty equality
    /// check.
    /// </summary>
    private void UpdateToolPreview(Point posInItems, KeyModifiers modifiers)
    {
        var inCanvas = posInItems.X >= HeaderWidth
                    && posInItems.Y >= 0
                    && (Vm?.Tracks is { Count: > 0 } t
                        && posInItems.Y < t.Count * RowHeight);

        if (!inCanvas || !modifiers.HasFlag(KeyModifiers.Control))
        {
            ClearToolPreview();
            return;
        }

        // Committed mode is already Create — no need to preview something
        // that's already the real state. Leaves sub-type untouched.
        if (_toolMode.Tool == ToolMode.Create)
        {
            _toolMode.PreviewTool       = null;
            _toolMode.PreviewCreateType = modifiers.HasFlag(KeyModifiers.Shift)
                ? CreateType.Stage
                : CreateType.Section;
            return;
        }

        _toolMode.PreviewTool       = ToolMode.Create;
        _toolMode.PreviewCreateType = modifiers.HasFlag(KeyModifiers.Shift)
            ? CreateType.Stage
            : CreateType.Section;
    }

    private void ClearToolPreview()
    {
        if (_toolMode.PreviewTool is null && _toolMode.PreviewCreateType is null) return;
        _toolMode.PreviewTool       = null;
        _toolMode.PreviewCreateType = null;
    }

    // ── Pointer events ───────────────────────────────────────────────────

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var rawPos = e.GetPosition(this);
        _log.LogDebug(
            $"[PRESS-ENTRY] raw.X={rawPos.X:F2} raw.Y={rawPos.Y:F2} " +
            $"_isAnimating={_isAnimating} _dragKind={_dragKind} " +
            $"tool={_toolMode.Tool} createType={_toolMode.CreateType} mods={e.KeyModifiers}");

        if (_isAnimating)
        {
            _log.LogDebug("[PRESS-EXIT] reason=animating");
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _log.LogDebug("[PRESS-EXIT] reason=not-left-button");
            return;
        }

        // The block editor is a modal-ish overlay — pointer work inside it
        // (TextBox focus, close button, meta chips) must not leak into
        // canvas gestures, and a click inside the editor area must not be
        // treated as "empty space" that clears selection.
        if (IsInsideBlockEditor(rawPos))
        {
            _log.LogDebug("[PRESS-EXIT] reason=inside-block-editor");
            return;
        }

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null)
        {
            _log.LogDebug("[PRESS-EXIT] reason=items-null");
            return;
        }

        var pos = e.GetPosition(items);
        if (pos.Y < 0)
        {
            _log.LogDebug($"[PRESS-EXIT] reason=y-negative pos.Y={pos.Y:F2}");
            return;
        }

        var tracks = Vm?.Tracks;
        if (tracks is null)
        {
            _log.LogDebug("[PRESS-EXIT] reason=tracks-null");
            return;
        }

        var trackIdx = (int)(pos.Y / RowHeight);
        if (trackIdx < 0 || trackIdx >= tracks.Count)
        {
            // Below the last track row. If the editor is open, close it —
            // the "click outside" dismiss affordance. Otherwise dead space.
            if (BlockEditor.IsVisible)
            {
                _log.LogDebug("[PRESS-EMPTY] closing block editor (below tracks)");
                CloseBlockEditor();
            }
            _log.LogDebug($"[PRESS-EXIT] reason=trackIdx-oob idx={trackIdx} count={tracks.Count}");
            return;
        }

        var track = tracks[trackIdx];

        // Left 200 px — reorder gesture.
        if (pos.X >= 0 && pos.X < HeaderWidth)
        {
            _dragKind     = DragKind.Reorder;
            _dragging     = track;
            _dragStart    = pos;
            _isDragging   = false;
            _clickOffsetY = pos.Y - trackIdx * RowHeight;

            _log.LogDebug($"[PRESS-REORDER] track={track.Name} idx={trackIdx}");

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Canvas press. Three possibilities based on tool + modifiers:
        //   (1) Ctrl / Ctrl+Shift       → create gesture (override)
        //   (2) Create tool active      → create gesture (normal)
        //   (3) Select tool, no Ctrl    → click-to-select / click-to-clear
        var resolved = ResolveCreateType(e.KeyModifiers);
        if (resolved is null)
        {
            // Select-mode pick gesture. Hit-test against blocks on the row.
            var (_, hitBlock) = HitBlock(pos);
            if (hitBlock is not null)
            {
                _log.LogDebug($"[PRESS-SELECT] hit '{hitBlock.Label}' on '{track.Name}'");
                _selection.SelectedTrack = track;
                _selection.SelectedBlock = hitBlock;
            }
            else
            {
                _log.LogDebug("[PRESS-SELECT] miss — clearing selection");
                CloseBlockEditor();
            }
            e.Handled = true;
            return;
        }

        // Create gesture. Snap the anchor past any block it lands inside so
        // the drag starts growing from the block's EndSec instead of being
        // rejected. Blocks further right get bumped at commit.
        var canvasX   = pos.X - HeaderWidth;
        var anchorSec = _viewport.PxToTime(canvasX);

        foreach (var b in track.Sections)
        {
            if (anchorSec > b.StartSec && anchorSec < b.EndSec)
            {
                anchorSec = b.EndSec;
                break;
            }
        }

        var minSec = LeftBound(track, anchorSec);

        _dragKind          = DragKind.Create;
        _createTrack       = track;
        _createTrackIdx    = trackIdx;
        _createAnchorSec   = anchorSec;
        _createType        = resolved.Value;
        _createClampMinSec = minSec;
        _dragStart         = pos;
        _isDragging        = false;

        _log.LogDebug(
            $"[CREATE-PRESS] anchorSec={anchorSec:F3} clampMin={minSec:F3} " +
            $"track={track.Name} type={resolved.Value}");

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;
        var pos = e.GetPosition(items);
        _lastPointerInItems = pos;

        // Update tool preview even during idle hovering. Cheap — the
        // service short-circuits identical writes — but gives instant
        // feedback when the user presses/releases Ctrl over the canvas.
        if (_dragKind == DragKind.None)
            UpdateToolPreview(pos, e.KeyModifiers);

        if (_dragKind == DragKind.None || _isAnimating) return;

        // Click-vs-drag disambiguation — same threshold for both paths.
        if (!_isDragging)
        {
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            if ((dx * dx) + (dy * dy) < DragThresholdPx * DragThresholdPx) return;

            _isDragging = true;
            _log.LogDebug($"[MOVE-BEGIN] dragKind={_dragKind}");

            if (_dragKind == DragKind.Reorder) BeginReorderVisuals();
            else                               BeginCreateVisuals();
        }

        if (_dragKind == DragKind.Reorder)
        {
            UpdateReorderPreview(pos);
            UpdateDropIndicator(pos);
        }
        else
        {
            UpdateCreatePreview(pos);
        }
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        _log.LogDebug($"[RELEASE-ENTRY] _dragKind={_dragKind} _isDragging={_isDragging}");

        if (_dragKind == DragKind.None) { Reset(); return; }

        _inReleaseHandler = true;
        try
        {
            e.Pointer.Capture(null);

            if (!_isDragging)
            {
                _log.LogDebug("[RELEASE-CLICK] no drag past threshold, cancelling");
                EndReorderVisuals();
                ClearCreateVisuals();
                Reset();
                return;
            }

            if (_dragKind == DragKind.Reorder)
                await HandleReorderReleaseAsync(e);
            else
                HandleCreateRelease(e);

            Reset();
        }
        finally
        {
            _inReleaseHandler = false;
        }
    }

    // ── Create gesture ───────────────────────────────────────────────────

    /// <summary>
    /// Decide whether a press should start a create gesture.
    ///   · Ctrl          → Section (override)
    ///   · Ctrl + Shift  → Stage   (override)
    ///   · Tool == Create → service's CreateType
    ///   · Otherwise     → null
    /// </summary>
    private CreateType? ResolveCreateType(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            return modifiers.HasFlag(KeyModifiers.Shift)
                ? CreateType.Stage
                : CreateType.Section;
        }

        if (_toolMode.Tool == ToolMode.Create)
            return _toolMode.CreateType;

        return null;
    }

    private static double LeftBound(ITrackViewModel track, double anchorSec)
    {
        double min = 0;
        foreach (var b in track.Sections)
            if (b.EndSec <= anchorSec && b.EndSec > min) min = b.EndSec;
        return min;
    }

    private void BeginCreateVisuals()
    {
        if (_createTrack is null) return;

        var accent = new SolidColorBrush(Color.Parse(_createTrack.Color));

        if (_createType == CreateType.Stage)
        {
            CreatePreview.Background      = Brushes.Transparent;
            CreatePreview.BorderBrush     = accent;
            CreatePreview.BorderThickness = new Thickness(2);
        }
        else
        {
            CreatePreview.Background      = new SolidColorBrush(Color.Parse(_createTrack.BlockBg));
            CreatePreview.BorderBrush     = accent;
            CreatePreview.BorderThickness = new Thickness(2);
        }

        CreatePreview.IsVisible = true;
    }

    private void UpdateCreatePreview(Point posInItems)
    {
        if (_createTrack is null) return;

        var (startSec, endSec) = ResolveDragInterval(posInItems);

        var startPxInCanvas = _viewport.TimeToPx(startSec);
        var endPxInCanvas   = _viewport.TimeToPx(endSec);

        var widthPx = Math.Max(0, endPxInCanvas - startPxInCanvas);
        var leftPx  = HeaderWidth + startPxInCanvas;
        var top     = _createTrackIdx * RowHeight + 4;

        CreatePreview.Width  = widthPx;
        CreatePreview.Margin = new Thickness(leftPx, top, 0, 0);

        var durationSec = endSec - startSec;
        CreatePreviewDurationText.Text      = FormatDuration(durationSec);
        CreatePreviewDurationText.IsVisible = widthPx >= 40;
    }

    private static string FormatDuration(double sec)
    {
        if (sec < 60) return $"{sec:0.0}s";
        var m = (int)(sec / 60);
        var s = sec - (m * 60);
        return $"{m}:{s:00.0}";
    }

    private (double start, double end) ResolveDragInterval(Point posInItems)
    {
        var pointerTimeSec = _viewport.PxToTime(posInItems.X - HeaderWidth);

        var a = _createAnchorSec;
        var b = pointerTimeSec;
        var startSec = Math.Min(a, b);
        var endSec   = Math.Max(a, b);

        startSec = Math.Max(startSec, _createClampMinSec);
        return (startSec, endSec);
    }

    private void ClearCreateVisuals() => CreatePreview.IsVisible = false;

    private void HandleCreateRelease(PointerReleasedEventArgs e)
    {
        if (_createTrack is null) { ClearCreateVisuals(); return; }

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) { ClearCreateVisuals(); return; }

        var pos = e.GetPosition(items);
        var (startSec, endSec) = ResolveDragInterval(pos);

        _log.LogDebug(
            $"[CREATE-RELEASE] startSec={startSec:F3} endSec={endSec:F3} dur={endSec - startSec:F3}");

        ClearCreateVisuals();

        var durationSec = endSec - startSec;
        if (durationSec < MinCreateDurSec)
        {
            _log.LogDebug($"[BLOCK-REJECTED] reason=too-short dur={durationSec:F3}");
            return;
        }

        IStageViewModel block = _createType == CreateType.Stage
            ? new StageViewModel
            {
                Label       = "Yeni stage",
                Bg          = _createTrack.BlockBg,
                AccentColor = _createTrack.Color,
                StartSec    = startSec,
                DurationSec = durationSec,
            }
            : new SectionViewModel
            {
                Label       = "Yeni section",
                Bg          = _createTrack.BlockBg,
                AccentColor = _createTrack.Color,
                StartSec    = startSec,
                DurationSec = durationSec,
                Dirty       = true,
            };

        _log.LogDebug($"[BLOCK-CREATED] type={_createType} track={_createTrack.Name}");

        var action = new CreateBlockAction(_createTrack, block);
        action.Execute();
        _history.Push(action);

        _selection.SelectedTrack = _createTrack;
        _selection.SelectedBlock = block;
        _toolMode.Tool           = ToolMode.Select;
    }

    // ── Reorder gesture ──────────────────────────────────────────────────

    private async Task HandleReorderReleaseAsync(PointerReleasedEventArgs e)
    {
        if (_dragging is null || Vm is null) { EndReorderVisuals(); return; }

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) { EndReorderVisuals(); return; }

        var pos     = e.GetPosition(items);
        var fromIdx = Vm.Tracks.IndexOf(_dragging);

        var (target, isBottom) = Hit(pos);
        int toIdx;
        if (target is not null && target != _dragging)
            toIdx = Vm.Tracks.IndexOf(target);
        else if (isBottom)
            toIdx = Vm.Tracks.Count - 1;
        else { EndReorderVisuals(); return; }

        if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx)
        {
            EndReorderVisuals();
            return;
        }

        await PlayReleaseAsync(items, fromIdx, toIdx);
    }

    private async Task PlayReleaseAsync(ItemsControl items, int fromIdx, int toIdx)
    {
        _isAnimating = true;

        DragPreview.IsVisible = false;
        ClearDropIndicators();

        var belowSource = ContainersInRange(items, fromIdx + 1, Vm!.Tracks.Count - 1);
        if (belowSource.Count > 0)
            await MoveTransition.RunAsync(belowSource, -RowHeight, CloseSourceDuration);

        var openTargets = new List<Control>();
        for (int i = 0; i < Vm.Tracks.Count; i++)
        {
            if (i == fromIdx) continue;
            bool include = fromIdx < toIdx
                ? i > toIdx
                : i >= toIdx && i < fromIdx;
            if (include && items.ContainerFromIndex(i) is Control c)
                openTargets.Add(c);
        }
        if (openTargets.Count > 0)
            await MoveTransition.RunAsync(openTargets, +RowHeight, OpenTargetDuration);

        var allContainers = ContainersInRange(items, 0, Vm.Tracks.Count - 1);
        MoveTransition.ResetOffsets(allContainers);

        Vm.Reorder(fromIdx, toIdx);

        _dragging!.IsDragging = false;
        await Task.Delay(FadeInDuration);
    }

    private void BeginReorderVisuals()
    {
        if (_dragging is null) return;

        _dragging.IsDragging = true;
        DragPreviewAccent.Background = new SolidColorBrush(Color.Parse(_dragging.Color));
        DragPreviewLabel.Text        = _dragging.Name;
        DragPreview.IsVisible        = true;
    }

    private void EndReorderVisuals()
    {
        if (_dragging is not null) _dragging.IsDragging = false;
        DragPreview.IsVisible = false;
        ClearDropIndicators();
    }

    private void UpdateReorderPreview(Point posInItems)
    {
        if (DragPreview.RenderTransform is not TranslateTransform tr)
        {
            tr = new TranslateTransform();
            DragPreview.RenderTransform = tr;
        }
        tr.X = 0;
        tr.Y = posInItems.Y - _clickOffsetY;
    }

    private void UpdateDropIndicator(Point posInItems)
    {
        ClearDropIndicators();
        if (_dragging is null || Vm?.Tracks is null) return;

        var (target, isBottom) = Hit(posInItems);
        if (target is not null && target != _dragging)
        {
            target.DropIndicator  = DropIndicator.Top;
            target.IndicatorBrush = new SolidColorBrush(Color.Parse(_dragging.Color));
        }
        else if (isBottom && Vm.Tracks.Count > 0)
        {
            var last = Vm.Tracks[^1];
            if (last != _dragging)
            {
                last.DropIndicator  = DropIndicator.Bottom;
                last.IndicatorBrush = new SolidColorBrush(Color.Parse(_dragging.Color));
            }
        }
    }

    private void ClearDropIndicators()
    {
        if (Vm?.Tracks is null) return;
        foreach (var t in Vm.Tracks)
        {
            t.DropIndicator  = DropIndicator.None;
            t.IndicatorBrush = null;
        }
    }

    // ── Shared helpers ───────────────────────────────────────────────────

    private static List<Control> ContainersInRange(ItemsControl items, int startInclusive, int endInclusive)
    {
        var list = new List<Control>();
        for (int i = startInclusive; i <= endInclusive; i++)
            if (items.ContainerFromIndex(i) is Control c)
                list.Add(c);
        return list;
    }

    private void Reset()
    {
        _dragKind       = DragKind.None;
        _dragging       = null;
        _createTrack    = null;
        _createTrackIdx = 0;
        _isDragging     = false;
        _isAnimating    = false;
    }

    private (ITrackViewModel? target, bool isBottom) Hit(Point posInItems)
    {
        var tracks = Vm?.Tracks;
        if (tracks is null || tracks.Count == 0) return (null, false);

        var idx = (int)(posInItems.Y / RowHeight);
        if (idx < 0)             return (null, false);
        if (idx >= tracks.Count) return (null, true);
        return (tracks[idx], false);
    }
}
