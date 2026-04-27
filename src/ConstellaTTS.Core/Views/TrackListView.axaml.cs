
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.Core.Misc.Logging;
using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.Engine;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.Timeline;
using ConstellaTTS.SDK.UI.Animation;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.UI.Tools;
using ConstellaTTS.SDK.ViewModelContracts;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Track list + timeline view. Dispatches five pointer gestures from one
/// capture based on press location and tool mode:
///
///   · REORDER — press on the left 200 px header of any row. Floating
///     cursor-attached preview + 3-stage release animation. Always
///     available regardless of tool mode.
///
///   · CREATE — press on empty canvas (no block hit) with Create tool
///     active, or while holding Ctrl (= Section) / Ctrl+Shift (= Stage)
///     as a transient override from Select mode. Paints a preview
///     rectangle; on release creates a block and appends it.
///
///   · MOVE — press on the middle of an existing block and drag.
///     Floating preview at the destination position; on release the
///     block slides to the new position (and possibly a new track
///     vertically). Bumps colliding blocks rightward via
///     <see cref="BlockBumping"/>, same rule as create.
///
///   · RESIZE-LEFT / RESIZE-RIGHT — press inside a block within
///     <see cref="EdgeResizePx"/> pixels of its left or right edge,
///     and drag. The opposite edge stays anchored, the dragged edge
///     follows the cursor in the time domain (clamped at 0 and at
///     the minimum-duration rule). Same bump policy as move.
///
///   · SELECT — click (no drag) on canvas. Hit-tests blocks; on hit
///     selects, on miss clears. Header click in Select mode (no
///     drag) selects the track itself.
///
/// Track header also supports right-click → inline rename and double-
/// click → inline rename.
///
/// Block geometry is time-domain (StartSec / DurationSec); pixels are
/// viewport projections computed at the gesture boundary.
/// </summary>
public partial class TrackListView : UserControl
{
    private const int    RowHeight         = 56;
    private const int    HeaderWidth       = 200;
    private const int    DragThresholdPx   = 5;
    private const double MinCreateDurSec   = 0.2;

    /// <summary>
    /// Width of the resize hot-zone on each block edge, in pixels.
    /// A press inside this band on the left edge starts a
    /// <see cref="DragKind.ResizeLeft"/> gesture; on the right edge,
    /// <see cref="DragKind.ResizeRight"/>. A press in the middle
    /// region starts <see cref="DragKind.Move"/>. Kept narrow (a
    /// few pixels) per Kerim's preference — just enough that the
    /// cursor lands on it deliberately when aiming at the edge,
    /// not by accident when aiming at the body.
    /// </summary>
    private const double EdgeResizePx     = 3;

    private const double ZoomFactorPerNotch = 1.15;
    private const double MinPxPerSec        = 4;
    private const double MaxPxPerSec        = 400;
    private const double ScrollStepSec      = 2;

    private static readonly TimeSpan CloseSourceDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan OpenTargetDuration  = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeInDuration      = TimeSpan.FromMilliseconds(180);

    private enum DragKind { None, Reorder, Create, Move, ResizeLeft, ResizeRight }

    private readonly IToolModeService          _toolMode;
    private readonly ITimelineViewport         _viewport;
    private readonly IHistoryManager           _history;
    private readonly ISelectionService         _selection;
    private readonly IEngineCatalog            _engineCatalog;
    private readonly IViewportHistoryRecorder  _viewportRecorder;
    private readonly IProjectManager           _projectManager;
    private readonly ILogger                   _log;

    private DragKind _dragKind;
    private bool     _isDragging;
    private bool     _isAnimating;
    private Point    _dragStart;

    private ITrackViewModel? _dragging;
    private double           _clickOffsetY;

    private ITrackViewModel? _createTrack;
    private int              _createTrackIdx;
    private double           _createAnchorSec;
    private CreateType       _createType;
    private double           _createClampMinSec;

    // ── Move / resize state ─────────────────────────────────────────────
    //
    // Captured on press, used during preview update on move, applied
    // on release to build the MoveBlockAction. Reset() clears all of
    // these alongside the create/reorder fields so a fresh gesture
    // starts from a blank slate.

    private IStageViewModel? _moveBlock;
    private ITrackViewModel? _moveSourceTrack;
    private int              _moveSourceTrackIdx;
    private ITrackViewModel? _moveTargetTrack;
    private int              _moveTargetTrackIdx;
    private double           _moveOriginalStartSec;
    private double           _moveOriginalDurationSec;

    /// <summary>
    /// Distance (in seconds) from the dragged edge / block-start to
    /// the press point. For Move this is "how far inside the block
    /// the user clicked" so the block sticks to the cursor at that
    /// offset. For ResizeLeft / ResizeRight this is the offset
    /// between cursor and the edge being dragged, always small.
    /// </summary>
    private double           _moveCursorOffsetSec;

    /// <summary>
    /// Live pre-bump preview values, updated each pointer-move tick.
    /// On release, these become the (newStartSec, newDurationSec)
    /// passed to <see cref="MoveBlockAction"/>; the action runs the
    /// canonical bump computation as part of its Execute.
    /// </summary>
    private double           _movePreviewStartSec;
    private double           _movePreviewDurationSec;

    private bool _inReleaseHandler;

    private Point?   _lastPointerInItems;
    private TopLevel? _keyListenerTopLevel;

    // Inline rename snapshot so Escape can revert to the pre-edit name,
    // and so the post-edit RenameTrackAction can be dispatched with
    // both the old and new values.
    private (ITrackViewModel track, string name)? _renameSnapshot;

    /// <summary>
    /// Currently-highlighted drop target during a sample drag, or
    /// null if no section is under the pointer (or the pointer is
    /// over a stage / empty canvas). Tracked at view-level so the
    /// previous target's IsDropTarget flag can be cleared cleanly
    /// when the pointer moves to a new section — without this,
    /// dragging across multiple sections in a row would leave
    /// stale highlights behind.
    /// </summary>
    private ISectionViewModel? _activeDropTarget;

    public TrackListView(
        IToolModeService          toolMode,
        ITimelineViewport         viewport,
        IHistoryManager           history,
        ISelectionService         selection,
        IEngineCatalog            engineCatalog,
        IViewportHistoryRecorder  viewportRecorder,
        IProjectManager           projectManager,
        ILoggerFactory            loggerFactory)
    {
        _toolMode         = toolMode;
        _viewport         = viewport;
        _history          = history;
        _selection        = selection;
        _engineCatalog    = engineCatalog;
        _viewportRecorder = viewportRecorder;
        _projectManager   = projectManager;
        _log              = loggerFactory.CreateLogger(LogCategory.WindowProcess);
        InitializeComponent();
        Setup();
    }

    private TrackListViewModel? Vm => DataContext as TrackListViewModel;

    /// <summary>
    /// Apply a selection change through the history stack. Captures the
    /// current (track, block) pair as the "from" state, the requested
    /// pair as the "to" state, and pushes a <see cref="SelectAction"/>.
    /// Consecutive selection changes collapse into one undo entry via
    /// the action's IMergeable implementation, so a click-fest leaves
    /// just one Ctrl+Z step pointing back at the selection the user
    /// had before they started.
    ///
    /// No-ops if the new pair equals the current one — keeps the undo
    /// stack from growing during transient re-selects (e.g. clicking
    /// the same block again).
    /// </summary>
    private void ApplySelection(ITrackViewModel? toTrack, IStageViewModel? toBlock)
    {
        var fromTrack = _selection.SelectedTrack;
        var fromBlock = _selection.SelectedBlock;

        if (ReferenceEquals(fromTrack, toTrack) && ReferenceEquals(fromBlock, toBlock))
            return;

        var action = new SelectAction(_selection, fromTrack, fromBlock, toTrack, toBlock);
        action.Execute();
        _history.Push(action);
    }

    /// <summary>
    /// Run an action through the canonical
    /// <c>Execute → Persist → Push</c> pattern. The action's
    /// <see cref="ConstellaTTS.SDK.UI.Actions.IPersistable.Persist"/>
    /// is self-flushing, so a successful return leaves the manifest
    /// in sync with the VM and the history stack carrying the
    /// reversible.
    ///
    /// <para>
    /// <b>Persist failures don't block the push.</b> The VM mutation
    /// has already happened; refusing to push the action onto the
    /// history stack would leave the user with an un-undoable
    /// change. The persist exception is logged and the action goes
    /// onto the stack as if it had succeeded — the next save
    /// attempt (any subsequent action's persist, or an explicit
    /// flush) will retry the manifest write against the current
    /// VM state.
    /// </para>
    /// </summary>
    private async Task DispatchAsync<TAction>(TAction action)
        where TAction : ConstellaTTS.SDK.UI.Actions.IAction,
                        ConstellaTTS.SDK.History.IReversible,
                        ConstellaTTS.SDK.UI.Actions.IPersistable
    {
        action.Execute();
        try
        {
            await action.Persist(_projectManager);
        }
        catch (Exception ex)
        {
            // Property access on `action` would be ambiguous here
            // because IAction and IReversible both declare Id/Name;
            // up-casting to IAction picks the action-side metadata
            // (which is what the log wants — the user-facing
            // action name, not the history-entry name).
            var asAction = (ConstellaTTS.SDK.UI.Actions.IAction)action;
            _log.LogError(ex, "Persist failed for action [{Id}] {Name}", asAction.Id, asAction.Name);
        }
        _history.Push(action);
    }

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
        _toolMode.PropertyChanged  += OnToolModeChanged;

        BlockEditorCloseButton.Click     += (_, _) => ApplySelection(null, null);
        BlockEditorLabelText.TextChanged += OnBlockEditorLabelChanged;

        // Section-only controls. The dropdown's items are populated once
        // here — the catalog is static for this session and the SelectedItem
        // is reassigned from RefreshBlockEditor as the user picks blocks.
        EngineCombo.ItemsSource          = _engineCatalog.Engines;
        EngineCombo.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(EngineDescriptor.DisplayName));
        EngineCombo.SelectionChanged    += OnEngineComboChanged;

        // Seed advance mode dropdown — a fixed enum, populated once.
        // Strategy fires AFTER each successful generation, so it doesn't
        // change the displayed seed until the user actually generates.
        SeedModeCombo.ItemsSource       = System.Enum.GetValues<SeedAdvanceMode>();
        SeedModeCombo.SelectionChanged += OnSeedModeChanged;

        SamplePickerButton.Click       += OnSamplePickerClick;
        EmotionSlider.ValueChanged     += OnEmotionSliderChanged;
        TemperatureSlider.ValueChanged += OnTemperatureSliderChanged;
        SeedDecrementButton.Click      += (_, _) => StepSeed(-1);
        SeedIncrementButton.Click      += (_, _) => StepSeed(+1);
        SeedRandomizeButton.Click      += OnSeedRandomizeClick;
        GenerateButton.Click           += OnGenerateClick;

        DataContextChanged += (_, _) => BindMinimap();
        BindMinimap();

        // Sample drag-drop pipeline. The UserControl carries
        // AllowDrop=True from XAML; these handlers turn raw
        // DragOver / DragLeave / Drop events into the section-
        // level drop-target highlighting and the AssignSampleAction
        // dispatch on commit. Registered with handledEventsToo so
        // events that bubble up already-handled (e.g. from a child
        // control marking them so) still reach the canvas — the
        // sample window is the only known emitter, and its drag
        // source doesn't pre-mark.
        AddHandler(DragDrop.DragOverEvent,  OnSampleDragOver,  handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnSampleDragLeave, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent,      OnSampleDrop,      handledEventsToo: true);
    }

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

        _viewportRecorder.Flush();

        if (_keyListenerTopLevel is not null)
        {
            _keyListenerTopLevel.KeyDown -= OnKeyStateChanged;
            _keyListenerTopLevel.KeyUp   -= OnKeyStateChanged;
            _keyListenerTopLevel = null;
        }
    }

    private void OnKeyStateChanged(object? sender, KeyEventArgs e)
    {
        // Ctrl/Shift drive the create-tool preview overlay; Alt drives
        // the move/resize hover cursor (a press that lacks Alt opens
        // the editor, a press WITH Alt starts a drag gesture, so the
        // cursor needs to update the moment the user holds the key
        // — not on the next pointer move).
        if (e.Key is not (Key.LeftCtrl  or Key.RightCtrl
                       or Key.LeftShift or Key.RightShift
                       or Key.LeftAlt   or Key.RightAlt))
            return;

        if (_lastPointerInItems is null) return;
        UpdateToolPreview(_lastPointerInItems.Value, e.KeyModifiers);
        UpdateHoverCursor (_lastPointerInItems.Value, e.KeyModifiers);
    }

    private void BindMinimap()
    {
        Minimap?.SetTracks(Vm?.Tracks);
        Minimap?.SetViewportRecorder(_viewportRecorder);
    }

    // ── Block editor overlay ─────────────────────────────────────────────

    private bool _suppressLabelEcho;
    private bool _suppressSectionEcho;

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshBlockEditor();
    }

    private void OnViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_selection.SelectedBlock is null) return;
        RefreshBlockEditor();
    }

    /// <summary>
    /// Refresh the hover cursor whenever the active tool changes.
    /// Without this hook, leaving Sample tool (e.g. closing the
    /// sample window or switching to Select / Create) would leave
    /// the canvas cursor stuck on <see cref="StandardCursorType.DragLink"/>
    /// until the next pointer move — noticeable on touchpads where
    /// the user might let the pointer rest after the tool toggle.
    /// Modifier-key changes are already covered by
    /// <see cref="OnKeyStateChanged"/>; this fills the gap for
    /// non-key tool transitions.
    /// </summary>
    private void OnToolModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IToolModeService.Tool)) return;
        if (_lastPointerInItems is null)
        {
            // Pointer isn't over the canvas; force a default arrow
            // so a tool toggle made via toolbar (with the cursor
            // parked over the toolbar button) doesn't carry a
            // stale tool-specific cursor when the pointer next
            // returns.
            Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }
        UpdateHoverCursor(_lastPointerInItems.Value, KeyModifiers.None);
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

        _suppressLabelEcho        = true;
        BlockEditorLabelText.Text = block.Label;
        _suppressLabelEcho        = false;

        var isSection = block is ISectionViewModel;
        SamplePickerButton.IsVisible = isSection;
        SeedRow.IsVisible            = isSection;
        EngineCombo.IsVisible        = isSection;
        SectionOnlyPanel.IsVisible   = isSection;

        if (block is ISectionViewModel section)
            PushSectionValuesToUi(section);

        PositionBlockEditor();
    }

    private void PushSectionValuesToUi(ISectionViewModel section)
    {
        _suppressSectionEcho = true;
        try
        {
            EngineCombo.SelectedItem = string.IsNullOrEmpty(section.EngineId)
                ? null
                : _engineCatalog.Find(section.EngineId);

            EmotionSlider.Value   = section.Emotion;
            EmotionValueText.Text = section.Emotion.ToString();

            TemperatureSlider.Value   = section.Temperature;
            TemperatureValueText.Text = section.Temperature.ToString("0.0");

            SeedValueText.Text = FormatSeed(section.Seed);
            SeedModeCombo.SelectedItem = section.SeedMode;

            SamplePickerLabel.Text = !string.IsNullOrEmpty(section.VoiceSampleRef)
                ? FormatSampleLabel(section.VoiceSampleRef)
                : "None";
        }
        finally
        {
            _suppressSectionEcho = false;
        }
    }

    private static string FormatSeed(int seed) => seed.ToString();

    private static string FormatSampleLabel(string sampleRef)
    {
        if (string.IsNullOrEmpty(sampleRef)) return "(unnamed)";

        var slash = Math.Max(sampleRef.LastIndexOf('/'), sampleRef.LastIndexOf('\\'));
        var name  = slash >= 0 ? sampleRef[(slash + 1)..] : sampleRef;

        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

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

        var available  = items.Bounds.Width;
        var visibleSec = (available - HeaderWidth) / Math.Max(0.001, _viewport.PxPerSec);
        var viewStart  = _viewport.ScrollOffsetSec;
        var viewEnd    = viewStart + visibleSec;
        if (block.EndSec < viewStart || block.StartSec > viewEnd)
        {
            BlockEditor.IsVisible = false;
            return;
        }

        var leftPx = HeaderWidth + _viewport.TimeToPx(block.StartSec);
        var topPx  = trackIdx * RowHeight + RowHeight;

        const double EditorWidth = 360;
        if (leftPx + EditorWidth > available)
            leftPx = available - EditorWidth - 8;
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
        if (block is ISectionViewModel section) section.Dirty = true;
    }

    // ── Section-only handlers ───────────────────────────────────────────────

    private void OnEngineComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSectionEcho) return;
        if (_selection.SelectedBlock is not ISectionViewModel section) return;

        var picked = EngineCombo.SelectedItem as EngineDescriptor;
        section.EngineId = picked?.Id ?? string.Empty;
        section.Dirty    = true;
    }

    private void OnEmotionSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSectionEcho) return;
        if (_selection.SelectedBlock is not ISectionViewModel section) return;

        var value = (int)Math.Round(e.NewValue);
        section.Emotion       = value;
        section.Dirty         = true;
        EmotionValueText.Text = value.ToString();
    }

    private void OnTemperatureSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSectionEcho) return;
        if (_selection.SelectedBlock is not ISectionViewModel section) return;

        var value = Math.Round(e.NewValue, 1);
        section.Temperature       = value;
        section.Dirty             = true;
        TemperatureValueText.Text = value.ToString("0.0");
    }

    private void StepSeed(int delta)
    {
        if (_selection.SelectedBlock is not ISectionViewModel section) return;

        var next = Math.Max(0, section.Seed + delta);
        section.Seed       = next;
        section.Dirty      = true;
        SeedValueText.Text = FormatSeed(next);
    }

    private void OnSeedRandomizeClick(object? sender, RoutedEventArgs e)
    {
        if (_selection.SelectedBlock is not ISectionViewModel section) return;

        var seed = Random.Shared.Next(1, int.MaxValue);
        section.Seed       = seed;
        section.Dirty      = true;
        SeedValueText.Text = FormatSeed(seed);
    }

    private void OnSeedModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSectionEcho) return;
        if (_selection.SelectedBlock is not ISectionViewModel section) return;
        if (SeedModeCombo.SelectedItem is not SeedAdvanceMode picked) return;

        section.SeedMode = picked;
        section.Dirty    = true;
    }

    private void AdvanceSeed(ISectionViewModel section)
    {
        var next = section.SeedMode switch
        {
            SeedAdvanceMode.Fixed     => section.Seed,
            SeedAdvanceMode.Increment => section.Seed + 1,
            SeedAdvanceMode.Decrement => Math.Max(0, section.Seed - 1),
            SeedAdvanceMode.Random    => Random.Shared.Next(1, int.MaxValue),
            _                         => section.Seed,
        };
        if (next == section.Seed) return;

        section.Seed = next;
        SeedValueText.Text = FormatSeed(next);
    }

    private void OnGenerateClick(object? sender, RoutedEventArgs e)
    {
        if (_selection.SelectedBlock is not ISectionViewModel section) return;
        _log.LogInformation(
            "[GENERATE] engine={Engine} seed={Seed} mode={Mode} emotion={Emotion} temp={Temp:F1} sample={Sample}",
            section.EngineId, section.Seed, section.SeedMode, section.Emotion, section.Temperature,
            section.VoiceSampleRef ?? "(none)");

        AdvanceSeed(section);
    }

    private void OnSamplePickerClick(object? sender, RoutedEventArgs e)
    {
    }

    private void CloseBlockEditor()
    {
        _selection.SelectedBlock = null;
        _selection.SelectedTrack = null;
    }

    private bool IsInsideBlockEditor(Point posInTrackListView)
    {
        if (!BlockEditor.IsVisible) return false;

        var topLeftInSelf = BlockEditor.TranslatePoint(new Point(0, 0), this);
        if (topLeftInSelf is null) return false;

        var b = BlockEditor.Bounds;
        var left   = topLeftInSelf.Value.X;
        var top    = topLeftInSelf.Value.Y;
        var right  = left + b.Width;
        var bottom = top  + b.Height;

        return posInTrackListView.X >= left && posInTrackListView.X <= right
            && posInTrackListView.Y >= top  && posInTrackListView.Y <= bottom;
    }

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

    /// <summary>
    /// Classify a press inside a hit block as resize-left, resize-right,
    /// or move based on its distance from the block's edges. Edge
    /// distances are computed in pixel space (using the current
    /// viewport's PxPerSec) so the hot-zone is a constant
    /// <see cref="EdgeResizePx"/> regardless of how zoomed in the user
    /// is — a 3px resize zone always feels like a 3px resize zone.
    /// </summary>
    private DragKind ClassifyBlockPress(Point posInItems, IStageViewModel block)
    {
        var canvasX  = posInItems.X - HeaderWidth;
        var leftPx   = _viewport.TimeToPx(block.StartSec);
        var rightPx  = _viewport.TimeToPx(block.EndSec);

        if (canvasX - leftPx <= EdgeResizePx)  return DragKind.ResizeLeft;
        if (rightPx - canvasX <= EdgeResizePx) return DragKind.ResizeRight;
        return DragKind.Move;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_inReleaseHandler) return;
        if (_dragKind == DragKind.None) return;

        _log.LogDebug($"[CAPTURE-LOST] _dragKind={_dragKind} _isDragging={_isDragging}");

        EndReorderVisuals();
        ClearCreateVisuals();
        ClearMoveVisuals();
        Reset();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _lastPointerInItems = null;
        ClearToolPreview();

        // Drop the hover-cursor back to the default arrow when the
        // pointer leaves the view. Without this the cursor would
        // stay frozen on whatever resize/move shape it last had,
        // even after the pointer is over an unrelated control.
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    // ── Wheel ────────────────────────────────────────────────────────────

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

        var pos = e.GetPosition(items);
        if (pos.X < HeaderWidth) return;

        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        _viewportRecorder.Touch();

        if (ctrl)
        {
            ApplyZoomAtCursor(pos, e.Delta.Y);
            e.Handled = true;
            return;
        }

        if (shift) return;

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

        if (_isAnimating) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsInsideBlockEditor(rawPos)) return;

        // Sample tool active — canvas is suspended for normal pointer
        // interaction. The only gesture that still works is sample
        // drag-drop, which travels through the DragDrop subsystem
        // (DragOver/Drop handlers) and is unaffected by this
        // PointerPressed early-exit. Reorder, click-select, create,
        // move/resize: all silenced until the user toggles the
        // tool back off.
        if (_toolMode.Tool == ToolMode.Sample) return;

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

        var pos = e.GetPosition(items);
        if (pos.Y < 0) return;

        var tracks = Vm?.Tracks;
        if (tracks is null) return;

        var trackIdx = (int)(pos.Y / RowHeight);
        if (trackIdx < 0 || trackIdx >= tracks.Count)
        {
            if (_selection.SelectedBlock is not null || _selection.SelectedTrack is not null)
                ApplySelection(null, null);
            return;
        }

        var track = tracks[trackIdx];

        // Left 200 px — reorder gesture press.
        if (pos.X >= 0 && pos.X < HeaderWidth)
        {
            _dragKind     = DragKind.Reorder;
            _dragging     = track;
            _dragStart    = pos;
            _isDragging   = false;
            _clickOffsetY = pos.Y - trackIdx * RowHeight;

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Canvas press. Sub-cases, in priority order:
        //   1. Ctrl/Ctrl+Shift — explicit create override.
        //   2. Alt + block hit — start move/resize based on edge
        //      distance. The block is NOT selected on press: the
        //      user is reaching for an edit gesture, not for the
        //      editor overlay, so opening the overlay would just
        //      flash it open at the start of every drag.
        //   3. Block hit (no Alt) — click-to-select. Opens the
        //      editor overlay; no drag gesture.
        //   4. Empty canvas with Create tool active — falls into
        //      the create gesture path below.
        //   5. Empty canvas, nothing else — clear selection.

        var resolved = ResolveCreateType(e.KeyModifiers);
        var (_, hitBlock) = HitBlock(pos);
        var altHeld = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (resolved is null && hitBlock is not null && altHeld)
        {
            // Alt + block hit: edit gesture (move/resize). Don't
            // touch selection — if the user releases without
            // dragging past the threshold, OnReleased treats the
            // gesture as a no-op and the previous selection (if
            // any) survives unchanged.
            var kind = ClassifyBlockPress(pos, hitBlock);
            _dragKind                = kind;
            _moveBlock               = hitBlock;
            _moveSourceTrack         = track;
            _moveSourceTrackIdx      = trackIdx;
            _moveTargetTrack         = track;
            _moveTargetTrackIdx      = trackIdx;
            _moveOriginalStartSec    = hitBlock.StartSec;
            _moveOriginalDurationSec = hitBlock.DurationSec;
            _movePreviewStartSec     = hitBlock.StartSec;
            _movePreviewDurationSec  = hitBlock.DurationSec;

            var pressTimeSec = _viewport.PxToTime(pos.X - HeaderWidth);
            _moveCursorOffsetSec = kind switch
            {
                DragKind.ResizeLeft  => pressTimeSec - hitBlock.StartSec,
                DragKind.ResizeRight => pressTimeSec - hitBlock.EndSec,
                _                    => pressTimeSec - hitBlock.StartSec,
            };

            _dragStart  = pos;
            _isDragging = false;

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (resolved is null)
        {
            // No Ctrl override. A block hit (no Alt) is a click-to-
            // select; an empty-canvas press clears selection.
            if (hitBlock is not null)
                ApplySelection(track, hitBlock);
            else
                ApplySelection(null, null);
            e.Handled = true;
            return;
        }

        // Create gesture path. Ctrl held; if the press also hit a
        // block, the Ctrl modifier wins — the user is explicitly
        // asking to create, not to move/resize.
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

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;
        var pos = e.GetPosition(items);
        _lastPointerInItems = pos;

        if (_dragKind == DragKind.None)
        {
            UpdateToolPreview(pos, e.KeyModifiers);
            UpdateHoverCursor (pos, e.KeyModifiers);
            return;
        }

        if (_isAnimating) return;

        if (!_isDragging)
        {
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            if ((dx * dx) + (dy * dy) < DragThresholdPx * DragThresholdPx) return;

            _isDragging = true;

            switch (_dragKind)
            {
                case DragKind.Reorder:                                BeginReorderVisuals(); break;
                case DragKind.Create:                                 BeginCreateVisuals();  break;
                case DragKind.Move
                  or DragKind.ResizeLeft
                  or DragKind.ResizeRight:                            BeginMoveVisuals();    break;
            }
        }

        switch (_dragKind)
        {
            case DragKind.Reorder:
                UpdateReorderPreview(pos);
                UpdateDropIndicator(pos);
                break;
            case DragKind.Create:
                UpdateCreatePreview(pos);
                break;
            case DragKind.Move:
            case DragKind.ResizeLeft:
            case DragKind.ResizeRight:
                UpdateMovePreview(pos);
                break;
        }
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragKind == DragKind.None) { Reset(); return; }

        _inReleaseHandler = true;
        try
        {
            e.Pointer.Capture(null);

            if (!_isDragging)
            {
                // Click on track header (Reorder gesture, no motion) in Select
                // mode → promote to a track selection.
                if (_dragKind == DragKind.Reorder
                    && _dragging is not null
                    && _toolMode.Tool == ToolMode.Select)
                {
                    ApplySelection(_dragging, null);
                }

                EndReorderVisuals();
                ClearCreateVisuals();
                ClearMoveVisuals();
                Reset();
                return;
            }

            switch (_dragKind)
            {
                case DragKind.Reorder:
                    await HandleReorderReleaseAsync(e);
                    break;
                case DragKind.Create:
                    await HandleCreateReleaseAsync(e);
                    break;
                case DragKind.Move:
                case DragKind.ResizeLeft:
                case DragKind.ResizeRight:
                    await HandleMoveReleaseAsync(e);
                    break;
            }

            Reset();
        }
        finally
        {
            _inReleaseHandler = false;
        }
    }

    // ── Create gesture ───────────────────────────────────────────────────

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

    private async Task HandleCreateReleaseAsync(PointerReleasedEventArgs e)
    {
        if (_createTrack is null) { ClearCreateVisuals(); return; }

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) { ClearCreateVisuals(); return; }

        var pos = e.GetPosition(items);
        var (startSec, endSec) = ResolveDragInterval(pos);

        ClearCreateVisuals();

        var durationSec = endSec - startSec;
        if (durationSec < MinCreateDurSec) return;

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

        var action = new CreateBlockAction(_createTrack, block);
        await DispatchAsync(action);

        _selection.SelectedTrack = _createTrack;
        _selection.SelectedBlock = block;
        _toolMode.Tool           = ToolMode.Select;
    }

    // ── Move / resize gesture ────────────────────────────────────────────

    private void BeginMoveVisuals()
    {
        if (_moveBlock is null || _moveSourceTrack is null) return;

        // Reuse the create-preview rectangle as the move/resize ghost
        // — same shape, same lifecycle, same hit-test-invisible
        // overlay. The rectangle gets the source track's colours so
        // the user sees what they're dragging; if a cross-track move
        // sends it elsewhere, the preview re-tints in
        // UpdateMovePreview as the cursor enters another row.
        var bg     = new SolidColorBrush(Color.Parse(_moveSourceTrack.BlockBg));
        var accent = new SolidColorBrush(Color.Parse(_moveSourceTrack.Color));

        CreatePreview.Background      = bg;
        CreatePreview.BorderBrush     = accent;
        CreatePreview.BorderThickness = new Thickness(2);
        CreatePreview.IsVisible       = true;
    }

    private void UpdateMovePreview(Point posInItems)
    {
        if (_moveBlock is null || _moveSourceTrack is null || Vm is null) return;

        var pointerTimeSec = _viewport.PxToTime(posInItems.X - HeaderWidth);

        // Resolve the new geometry from the gesture mode. Caller-side
        // clamping (>= 0, >= MinCreateDurSec) happens here so the
        // floating preview can show the actual final position rather
        // than a value the action would later overrule.
        switch (_dragKind)
        {
            case DragKind.Move:
            {
                var newStart = Math.Max(0, pointerTimeSec - _moveCursorOffsetSec);
                _movePreviewStartSec    = newStart;
                _movePreviewDurationSec = _moveOriginalDurationSec;
                break;
            }
            case DragKind.ResizeLeft:
            {
                // The right edge stays anchored at the original
                // EndSec; the left edge moves with the cursor minus
                // the press offset within the edge band. Clamp so
                // the resulting duration doesn't fall below the
                // minimum and the start stays at >= 0.
                var endSec     = _moveOriginalStartSec + _moveOriginalDurationSec;
                var rawStart   = pointerTimeSec - _moveCursorOffsetSec;
                var newStart   = Math.Min(Math.Max(0, rawStart), endSec - MinCreateDurSec);
                _movePreviewStartSec    = newStart;
                _movePreviewDurationSec = endSec - newStart;
                break;
            }
            case DragKind.ResizeRight:
            {
                // StartSec is anchored; the right edge moves. New
                // duration is endTime - startSec, clamped at min.
                var rawEnd   = pointerTimeSec - _moveCursorOffsetSec;
                var newEnd   = Math.Max(_moveOriginalStartSec + MinCreateDurSec, rawEnd);
                _movePreviewStartSec    = _moveOriginalStartSec;
                _movePreviewDurationSec = newEnd - _moveOriginalStartSec;
                break;
            }
        }

        // Cross-track only matters for Move — see MoveBlockAction's
        // constructor contract. For resize gestures the target track
        // stays pinned to the source.
        if (_dragKind == DragKind.Move)
        {
            var idx = (int)(posInItems.Y / RowHeight);
            if (idx >= 0 && idx < Vm.Tracks.Count)
            {
                var newTarget = Vm.Tracks[idx];
                if (!ReferenceEquals(newTarget, _moveTargetTrack))
                {
                    _moveTargetTrack    = newTarget;
                    _moveTargetTrackIdx = idx;

                    // Re-tint the floating preview to match the new
                    // destination, so a cross-track drag visually
                    // signals the intent before commit.
                    CreatePreview.Background  =
                        new SolidColorBrush(Color.Parse(newTarget.BlockBg));
                    CreatePreview.BorderBrush =
                        new SolidColorBrush(Color.Parse(newTarget.Color));
                }
            }
        }

        // Lay out the preview rectangle. Y is the destination
        // track's row; X / Width come from the time-domain values.
        var startPx = _viewport.TimeToPx(_movePreviewStartSec);
        var endPx   = _viewport.TimeToPx(_movePreviewStartSec + _movePreviewDurationSec);
        var widthPx = Math.Max(0, endPx - startPx);

        CreatePreview.Width  = widthPx;
        CreatePreview.Margin = new Thickness(
            HeaderWidth + startPx,
            _moveTargetTrackIdx * RowHeight + 4,
            0, 0);

        CreatePreviewDurationText.Text      = FormatDuration(_movePreviewDurationSec);
        CreatePreviewDurationText.IsVisible = widthPx >= 40;
    }

    private void ClearMoveVisuals() => CreatePreview.IsVisible = false;

    private async Task HandleMoveReleaseAsync(PointerReleasedEventArgs e)
    {
        ClearMoveVisuals();

        if (_moveBlock is null || _moveSourceTrack is null || _moveTargetTrack is null)
            return;

        // No-op if the gesture didn't actually change anything (the
        // drag passed the threshold but landed back on the original
        // position). Saves a manifest write and an undo entry.
        var samePos =
            Math.Abs(_movePreviewStartSec    - _moveOriginalStartSec)    < 1e-6 &&
            Math.Abs(_movePreviewDurationSec - _moveOriginalDurationSec) < 1e-6;
        var sameTrack = ReferenceEquals(_moveTargetTrack, _moveSourceTrack);

        if (samePos && sameTrack) return;

        var mode = _dragKind switch
        {
            DragKind.ResizeLeft  => MoveBlockMode.ResizeLeft,
            DragKind.ResizeRight => MoveBlockMode.ResizeRight,
            _                    => MoveBlockMode.Move,
        };

        // Resize is constrained to same-track by MoveBlockAction's
        // constructor, so any vertical drag during a resize gesture
        // is ignored: the target track is forced back to the source.
        var target = mode == MoveBlockMode.Move ? _moveTargetTrack : _moveSourceTrack;

        var action = new MoveBlockAction(
            fromTrack:      _moveSourceTrack,
            toTrack:        target,
            block:          _moveBlock,
            mode:           mode,
            newStartSec:    _movePreviewStartSec,
            newDurationSec: _movePreviewDurationSec);

        await DispatchAsync(action);
    }

    /// <summary>
    /// Update the cursor based on what's under the pointer when no
    /// drag is in progress. Edge / body cursors only appear while
    /// <see cref="KeyModifiers.Alt"/> is held AND the active tool is
    /// <see cref="ToolMode.Select"/> — those are the conditions
    /// under which a press would actually start a move/resize
    /// gesture. In Create tool the press resolves to a create
    /// (Alt is irrelevant), so showing a resize/move cursor would
    /// be misleading.
    /// </summary>
    private void UpdateHoverCursor(Point posInItems, KeyModifiers modifiers)
    {
        if (_toolMode.Tool == ToolMode.Sample)
        {
            // Canvas is a drop zone while the Sample tool is active
            // — a DragLink cursor signals that to the user even
            // before they begin a drag from the library window.
            // Plain clicks are silenced upstream (OnPressed early-
            // exit), so the cursor shouldn't suggest "clickable".
            Cursor = new Cursor(StandardCursorType.DragLink);
            return;
        }

        var canEdit = modifiers.HasFlag(KeyModifiers.Alt)
                   && _toolMode.Tool == ToolMode.Select;

        if (!canEdit)
        {
            Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }

        var (_, hit) = HitBlock(posInItems);
        if (hit is null)
        {
            Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }

        Cursor = ClassifyBlockPress(posInItems, hit) switch
        {
            DragKind.ResizeLeft  => new Cursor(StandardCursorType.LeftSide),
            DragKind.ResizeRight => new Cursor(StandardCursorType.RightSide),
            _                    => new Cursor(StandardCursorType.SizeAll),
        };
    }

    // ── Sample drag-drop ───────────────────────────────────────────────
    //
    // The Sample Library window is the only registered drag source
    // for the timeline; its payload format is
    // <see cref="SampleLibraryView.SampleDragFormat"/>. The handlers
    // below ignore drags carrying any other format so external
    // payloads (browser-dropped URLs, file-explorer drags) silently
    // pass through without disturbing the canvas.
    //
    // The drop-target highlight is driven through the section's
    // <c>IsDropTarget</c> observable: DragOver flips it true on the
    // section under the pointer (and false on whatever was previously
    // highlighted), DragLeave / Drop / failure paths clear it.
    // Tracking the active target in <see cref="_activeDropTarget"/>
    // means we don't have to walk every block to wipe stale
    // highlights — just the one that was last lit.

    /// <summary>
    /// Hit-test the canvas for a section under the pointer. Returns
    /// (track, section) when the pointer lands on a section; (track,
    /// null) when on a stage or empty canvas (track row identified
    /// but nothing droppable there); (null, null) when fully out of
    /// the canvas region. Mirrors <see cref="HitBlock"/> but narrows
    /// the result type to section, since stages aren't valid drop
    /// targets.
    /// </summary>
    private (ITrackViewModel? track, ISectionViewModel? section) HitSection(Point posInItems)
    {
        var (track, block) = HitBlock(posInItems);
        return (track, block as ISectionViewModel);
    }

    private void OnSampleDragOver(object? sender, DragEventArgs e)
    {
        // Avalonia 12 routes drag payloads through DataTransfer (the
        // old Data/IDataObject is gone). TryGetValue with our typed
        // application format returns null for any drag that doesn't
        // carry our payload — browser URL drags, OS file drags,
        // unrelated app drags all fall through cleanly.
        var sampleRef = e.DataTransfer?.TryGetValue(SampleLibraryView.SampleDragFormat);
        if (sampleRef is null)
        {
            e.DragEffects = DragDropEffects.None;
            HideSampleDragPreview();
            return;
        }

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null)
        {
            e.DragEffects = DragDropEffects.None;
            HideSampleDragPreview();
            return;
        }

        var pos = e.GetPosition(items);

        // Update the floating preview card's position regardless of
        // whether there's a valid drop target under the cursor — the
        // user benefits from seeing the dragged sample's name even
        // while parked over an empty area or a stage block.
        ShowSampleDragPreview(pos, sampleRef);

        var (_, section) = HitSection(pos);

        if (section is null)
        {
            // Pointer isn't over a valid drop target. Clear any
            // stale highlight from a previous tick of this same
            // drag and tell the source the drop wouldn't take.
            ClearActiveDropTarget();
            e.DragEffects = DragDropEffects.None;
            e.Handled     = true;
            return;
        }

        // New section under pointer — transfer the highlight. The
        // identity check skips the redundant flag flip when the
        // pointer stays inside the same section across many ticks.
        if (!ReferenceEquals(section, _activeDropTarget))
        {
            ClearActiveDropTarget();
            section.IsDropTarget = true;
            _activeDropTarget    = section;
        }

        // Link is the closest semantic to "section now references
        // this sample" — the file isn't copied or moved, just
        // pointed at by name. Matching the source's requested
        // effect keeps the cursor consistent.
        e.DragEffects = DragDropEffects.Link;
        e.Handled     = true;
    }

    private void OnSampleDragLeave(object? sender, RoutedEventArgs e)
    {
        // The pointer left the canvas (or the drag was cancelled
        // mid-flight). Drop the section highlight AND the floating
        // preview card; if it really was a leave and the user re-
        // enters, DragOver will set both again on the new section.
        ClearActiveDropTarget();
        HideSampleDragPreview();
    }

    private async void OnSampleDrop(object? sender, DragEventArgs e)
    {
        // Pull the typed sample reference up front. Same payload
        // contract as DragOver: TryGetValue returns null on any
        // non-sample drag, in which case the drop is silently
        // ignored.
        var newRef = e.DataTransfer?.TryGetValue(SampleLibraryView.SampleDragFormat);
        if (string.IsNullOrEmpty(newRef))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

        var pos = e.GetPosition(items);
        var (track, section) = HitSection(pos);

        // Always clear the highlight on drop, regardless of whether
        // the drop landed on a valid target. A successful drop will
        // re-set it briefly via DragOver on a subsequent drag, but
        // for THIS gesture the highlight has served its purpose.
        ClearActiveDropTarget();
        HideSampleDragPreview();

        if (track is null || section is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // No-op if the section already has this exact ref. Skips a
        // redundant manifest write and a confusing undo entry that
        // appears to do nothing.
        if (string.Equals(section.VoiceSampleRef, newRef, StringComparison.Ordinal))
        {
            e.DragEffects = DragDropEffects.Link;
            e.Handled     = true;
            return;
        }

        var action = new AssignSampleAction(
            track:  track,
            block:  section,
            oldRef: section.VoiceSampleRef,
            newRef: newRef);

        await DispatchAsync(action);

        // Refresh the editor overlay if it happens to be open on this
        // section — the sample chip's label needs to update to the
        // newly-assigned filename. RefreshBlockEditor is a cheap
        // VM-read; calling it unconditionally is simpler than
        // checking whether the dropped section equals the selected
        // one and matches the way other section mutators (move,
        // resize) refresh themselves.
        RefreshBlockEditor();

        e.DragEffects = DragDropEffects.Link;
        e.Handled     = true;
    }

    private void ClearActiveDropTarget()
    {
        if (_activeDropTarget is null) return;
        _activeDropTarget.IsDropTarget = false;
        _activeDropTarget              = null;
    }

    /// <summary>
    /// Position and show the floating sample-drag preview card. The
    /// card uses the same RenderTransform trick as the track-reorder
    /// preview — layout sits at (0, 0) but a TranslateTransform
    /// follows the cursor every DragOver tick. Offset 14 px right and
    /// 10 px down keeps the card out from under the cursor itself so
    /// the user can still see what's directly beneath.
    /// </summary>
    private void ShowSampleDragPreview(Point posInItems, string sampleRef)
    {
        SampleDragPreviewLabel.Text = FormatSampleLabel(sampleRef);

        if (SampleDragPreview.RenderTransform is not TranslateTransform tr)
        {
            tr = new TranslateTransform();
            SampleDragPreview.RenderTransform = tr;
        }
        tr.X = posInItems.X + 14;
        tr.Y = posInItems.Y + 10;

        SampleDragPreview.IsVisible = true;
    }

    private void HideSampleDragPreview()
    {
        if (!SampleDragPreview.IsVisible) return;
        SampleDragPreview.IsVisible = false;
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

        // Reorder via action so undo and persist both fire. The
        // action's Execute calls Vm.Reorder internally — same code
        // path the previous direct call used, just routed through
        // the action shell.
        var action = new ReorderTracksAction(Vm, fromIdx, toIdx);
        await DispatchAsync(action);

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

    // ── Track-header actions: add / rename ───────────────────────────────

    private async void OnAddTrackClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null || Vm is null) return;

        var defaultName = $"Track {Vm.Tracks.Count + 1}";
        var name = await AddTrackDialog.ShowAsync(owner, defaultName);
        if (name is null) return;

        var action = new AddTrackAction(Vm, name);
        await DispatchAsync(action);
    }

    private void OnTrackHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        if (ctrl.DataContext is not ITrackViewModel track) return;

        var props = e.GetCurrentPoint(ctrl).Properties;
        if (!props.IsRightButtonPressed) return;

        BeginInlineRename(ctrl, track);
        e.Handled = true;
    }

    private void OnTrackHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        if (ctrl.DataContext is not ITrackViewModel track) return;

        BeginInlineRename(ctrl, track);
        e.Handled = true;
    }

    private void BeginInlineRename(Control header, ITrackViewModel track)
    {
        _renameSnapshot = (track, track.Name);
        track.IsEditing = true;

        Dispatcher.UIThread.Post(() =>
        {
            var textBox = header.GetVisualDescendants()
                                .OfType<TextBox>()
                                .FirstOrDefault();
            if (textBox is not null)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnTrackRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not ITrackViewModel track) return;

        // LostFocus = commit. Two-way binding already pushed the final
        // value into Name; the action is dispatched against the
        // before/after pair captured at rename start.
        _ = CommitRenameAsync(track);
    }

    private void OnTrackRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not ITrackViewModel track) return;

        if (e.Key == Key.Enter)
        {
            _ = CommitRenameAsync(track);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Revert: two-way binding has been pushing characters into
            // Name as the user typed, so restore the pre-edit snapshot
            // BEFORE committing — this way no rename action is
            // dispatched and the manifest stays untouched.
            if (_renameSnapshot is var (snapTrack, snapName) && snapTrack == track)
                track.Name = snapName;
            _ = CommitRenameAsync(track, dispatchAction: false);
            e.Handled = true;
        }
    }

    private async Task CommitRenameAsync(ITrackViewModel track, bool dispatchAction = true)
    {
        // Empty rename collapses back to a placeholder rather than
        // leaving an invisible-label track lying around.
        if (string.IsNullOrWhiteSpace(track.Name))
            track.Name = "Track";

        var snapshot = _renameSnapshot;
        track.IsEditing = false;
        _renameSnapshot = null;

        if (!dispatchAction) return;
        if (snapshot is null) return;

        var (snapTrack, oldName) = snapshot.Value;
        if (!ReferenceEquals(snapTrack, track)) return;

        var newName = track.Name;
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        // Two-way binding has already set track.Name = newName. The
        // rename action's Execute is idempotent against that, and
        // the persist + history push happen here.
        var action = new RenameTrackAction(track, oldName, newName);
        await DispatchAsync(action);
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
        _dragKind                = DragKind.None;
        _dragging                = null;
        _createTrack             = null;
        _createTrackIdx          = 0;
        _moveBlock               = null;
        _moveSourceTrack         = null;
        _moveTargetTrack         = null;
        _moveSourceTrackIdx      = 0;
        _moveTargetTrackIdx      = 0;
        _moveOriginalStartSec    = 0;
        _moveOriginalDurationSec = 0;
        _moveCursorOffsetSec     = 0;
        _movePreviewStartSec     = 0;
        _movePreviewDurationSec  = 0;
        _isDragging              = false;
        _isAnimating             = false;
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
