
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
using ConstellaTTS.Domain;
using ConstellaTTS.SDK.Engine;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Timeline;
using ConstellaTTS.SDK.UI.Animation;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.UI.Tools;
using ConstellaTTS.SDK.ViewModelContracts;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Track list + timeline view. Dispatches three pointer gestures from one
/// capture based on press location and tool mode:
///
///   · REORDER — press on the left 200 px header of any row. Floating
///     cursor-attached preview + 3-stage release animation. Always
///     available regardless of tool mode.
///
///   · CREATE — press on the canvas area (X ≥ 200 px) with Create tool
///     active, or while holding Ctrl (= Section) / Ctrl+Shift (= Stage)
///     as a transient override from Select mode. Paints a preview
///     rectangle; on release creates a block and appends it.
///
///   · SELECT — press on canvas with Select tool, no Ctrl. Hit-tests
///     blocks on the row; on hit selects, on miss clears. Header
///     click in Select mode (no drag) selects the track itself,
///     driving the Delete button's "delete track" mode.
///
/// Track header also supports right-click → inline rename (IsEditing
/// flag flips TextBlock↔TextBox; LostFocus / Enter commit, Escape
/// reverts to the pre-edit name).
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

    private const double ZoomFactorPerNotch = 1.15;
    private const double MinPxPerSec        = 4;
    private const double MaxPxPerSec        = 400;
    private const double ScrollStepSec      = 2;

    private static readonly TimeSpan CloseSourceDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan OpenTargetDuration  = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeInDuration      = TimeSpan.FromMilliseconds(180);

    private enum DragKind { None, Reorder, Create }

    private readonly IToolModeService          _toolMode;
    private readonly ITimelineViewport         _viewport;
    private readonly IHistoryManager           _history;
    private readonly ISelectionService         _selection;
    private readonly IEngineCatalog            _engineCatalog;
    private readonly IViewportHistoryRecorder  _viewportRecorder;
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

    private bool _inReleaseHandler;

    private Point?   _lastPointerInItems;
    private TopLevel? _keyListenerTopLevel;

    // Inline rename snapshot so Escape can revert to the pre-edit name.
    // Two-way binding writes straight into Name as the user types, so
    // without this Escape would keep whatever partial value was there.
    private (ITrackViewModel track, string name)? _renameSnapshot;

    public TrackListView(
        IToolModeService          toolMode,
        ITimelineViewport         viewport,
        IHistoryManager           history,
        ISelectionService         selection,
        IEngineCatalog            engineCatalog,
        IViewportHistoryRecorder  viewportRecorder,
        ILoggerFactory            loggerFactory)
    {
        _toolMode         = toolMode;
        _viewport         = viewport;
        _history          = history;
        _selection        = selection;
        _engineCatalog    = engineCatalog;
        _viewportRecorder = viewportRecorder;
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

        // Flush any pending scroll session so its history entry isn't
        // lost when the view detaches mid-burst (e.g. project reload,
        // tab switch). The recorder is a singleton shared with the
        // minimap; flushing here closes whichever input source's
        // session was open at detach time.
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
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl
                       or Key.LeftShift or Key.RightShift))
            return;

        if (_lastPointerInItems is null) return;
        UpdateToolPreview(_lastPointerInItems.Value, e.KeyModifiers);
    }

    private void BindMinimap()
    {
        Minimap?.SetTracks(Vm?.Tracks);
        // Pass the shared recorder to the minimap so its pan and
        // select-range gestures contribute to the same coalesced undo
        // session as the track-canvas wheel handler. Idempotent if the
        // tracks collection rebinds without the recorder changing.
        Minimap?.SetViewportRecorder(_viewportRecorder);
    }

    // ── Block editor overlay ─────────────────────────────────────────────

    private bool _suppressLabelEcho;

    /// <summary>
    /// True while RefreshBlockEditor is pushing VM values into the
    /// section-only controls. All section-control change handlers
    /// short-circuit while this is set to break the VM→UI→VM echo that
    /// would otherwise oscillate when a freshly-selected block's values
    /// land in Slider/TextBox/ComboBox and immediately fire ValueChanged.
    /// </summary>
    private bool _suppressSectionEcho;

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshBlockEditor();
    }

    private void OnViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Reposition / reopen the editor whenever the viewport moves.
        // We can't bail when the editor is currently hidden — it might
        // need to come back into view because the user (or a Ctrl+Z
        // ViewportChangeAction) just scrolled the selected block back
        // into the visible range. RefreshBlockEditor handles both cases:
        // it shows + repositions the editor when the block is in view,
        // and quietly hides it when it isn't.
        if (_selection.SelectedBlock is null) return;
        RefreshBlockEditor();
    }

    private void RefreshBlockEditor()
    {
        var block = _selection.SelectedBlock;
        var track = _selection.SelectedTrack;

        // Editor only opens for block selection; track-only selection is
        // handled separately by the context bar's Delete-track button.
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

        // Section-only chip strip elements (sample, seed) and the engine
        // combo + slider footer all toggle together. Stage blocks just
        // get duration/start/end + label + close button — no engine
        // wiring, no sample binding.
        var isSection = block is ISectionViewModel;
        SamplePickerButton.IsVisible = isSection;
        SeedRow.IsVisible            = isSection;
        EngineCombo.IsVisible        = isSection;
        SectionOnlyPanel.IsVisible   = isSection;

        if (block is ISectionViewModel section)
            PushSectionValuesToUi(section);

        PositionBlockEditor();
    }

    /// <summary>
    /// Push the section's current values into the editor controls. Wraps
    /// the writes in <see cref="_suppressSectionEcho"/> so the change
    /// handlers don't bounce them straight back into the VM.
    /// </summary>
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

            SamplePickerLabel.Text = section.VoiceSample is { } s
                ? FormatSampleLabel(s)
                : "None";
        }
        finally
        {
            _suppressSectionEcho = false;
        }
    }

    /// <summary>
    /// Render a seed value for the chip's text slot. The chip used to
    /// show "auto" for 0, but the advance-mode dropdown now owns that
    /// semantic (Random / Fixed / etc. determine what happens between
    /// renders), so the chip is plain numeric. 0 stays as "0" — a
    /// legitimate user-selectable value, not a special placeholder.
    /// </summary>
    private static string FormatSeed(int seed) => seed.ToString();

    /// <summary>
    /// Render a Sample as a short pickable label — the trailing path
    /// segment is enough to recognise it without overflowing the button.
    /// </summary>
    private static string FormatSampleLabel(Sample sample)
    {
        var path = sample.RawAudioPath;
        if (string.IsNullOrEmpty(path)) return $"Sample {sample.Id.ToString()[..8]}";
        var slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return slash >= 0 ? path[(slash + 1)..] : path;
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
            // Block has scrolled outside the visible viewport — just hide
            // the overlay; don't clear the selection. The user might scroll
            // back, in which case we want the editor to reopen seamlessly
            // rather than the selection being silently dropped (which would
            // also pollute the undo stack with a SelectAction every scroll).
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
    //
    // Each handler:
    //   1. Bails if echo is suppressed (RefreshBlockEditor is pushing).
    //   2. Bails if the selection isn't a section — the controls are
    //      hidden in that case but events can still fire transiently
    //      while the selection switches.
    //   3. Writes the new value to the VM and flips Dirty=true so the
    //      block's left-edge yellow strip lights up.

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

        // Round to one decimal so the value chip and the VM stay consistent
        // (avoids 0.7000000000001 noise from the underlying double slider).
        var value = Math.Round(e.NewValue, 1);
        section.Temperature       = value;
        section.Dirty             = true;
        TemperatureValueText.Text = value.ToString("0.0");
    }

    /// <summary>
    /// Adjust seed by a step. Clamped at 0 (auto) on the low end so
    /// pressing ◀ past zero just lands on "auto" rather than going
    /// negative. No upper clamp — ints are large enough that the user
    /// can't realistically reach the limit by clicking.
    /// </summary>
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

        // Pick from the positive int range so the seed never lands on 0
        // (which means "auto"). System.Random is fine here — we don't
        // need cryptographic-quality randomness for an RNG seed.
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

    /// <summary>
    /// Apply the section's <see cref="SeedAdvanceMode"/> after a
    /// successful generation. Called from <see cref="OnGenerateClick"/>
    /// once the engine returns audio; isolated so future automation
    /// (the listen-while-generating toggle) can call it too without
    /// duplicating the policy logic.
    /// </summary>
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
        // Hook for the TTS pipeline. Today this is a no-op: the IPC daemon
        // wiring is in place but no real engine adapter exists yet, so
        // there's nothing to actually generate against. Logging the click
        // is enough until the first adapter lands — then this becomes a
        // call into the IIPCService with the section's parameter bundle.
        if (_selection.SelectedBlock is not ISectionViewModel section) return;
        _log.LogInformation(
            "[GENERATE] engine={Engine} seed={Seed} mode={Mode} emotion={Emotion} temp={Temp:F1} sample={Sample}",
            section.EngineId, section.Seed, section.SeedMode, section.Emotion, section.Temperature,
            section.VoiceSample?.RawAudioPath ?? "(none)");

        // Apply the post-generation seed strategy. In Fixed mode this is
        // a no-op; the other modes nudge the seed so the next render
        // produces a fresh take without the user having to touch the
        // chip manually.
        AdvanceSeed(section);
    }

    private void OnSamplePickerClick(object? sender, RoutedEventArgs e)
    {
        // For now, cycle through the available samples on each click as a
        // minimum-viable picker. Replacing this with a popup or modal
        // window listing samples is a follow-up turn — the wiring (VM
        // field, label refresh) is already in place.
        if (_selection.SelectedBlock is not ISectionViewModel section) return;
     
   

        var current = section.VoiceSample;


        section.Dirty          = true;
    }

    private void CloseBlockEditor()
    {
        // Direct selection clear without history — used by tear-down paths
        // where pushing a SelectAction would be incorrect (e.g. detaching
        // the view, project reload). User-initiated dismissal goes through
        // ApplySelection(null, null) so it lands on the undo stack.
        _selection.SelectedBlock = null;
        _selection.SelectedTrack = null;
    }

    /// <summary>
    /// True if <paramref name="posInTrackListView"/> falls inside the
    /// block editor overlay's bounds. The overlay lives in the inner
    /// <c>Panel</c> (Grid.Row=1), so its <c>Bounds</c> are relative to
    /// that panel — not the outer UserControl. Without translating, the
    /// top-edge offset of every other ancestor row (32 px ruler today,
    /// plus any future chrome) shifts the comparison and clicks on
    /// neighbouring tracks register as "inside the editor" and get
    /// swallowed. Translating the editor's top-left into TrackListView
    /// coordinates with <see cref="Visual.TranslatePoint"/> normalises
    /// the comparison so the hit zone matches what the user actually
    /// sees on screen.
    /// </summary>
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

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_inReleaseHandler) return;
        if (_dragKind == DragKind.None) return;

        _log.LogDebug($"[CAPTURE-LOST] _dragKind={_dragKind} _isDragging={_isDragging}");

        EndReorderVisuals();
        ClearCreateVisuals();
        Reset();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _lastPointerInItems = null;
        ClearToolPreview();
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

        // Open / extend a viewport-history session. The recorder
        // snapshots the FROM viewport state on the first Touch of a
        // burst and pushes a single ViewportChangeAction once the
        // user has paused for the settle threshold. Same recorder is
        // shared with the minimap, so a wheel-burst here followed by
        // a minimap drag both contribute to the user's scroll history
        // through the same coalescing path.
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
            if (_selection.SelectedBlock is not null || _selection.SelectedTrack is not null)
            {
                _log.LogDebug("[PRESS-EMPTY] clearing selection (below tracks)");
                ApplySelection(null, null);
            }
            _log.LogDebug($"[PRESS-EXIT] reason=trackIdx-oob idx={trackIdx} count={tracks.Count}");
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

            _log.LogDebug($"[PRESS-REORDER] track={track.Name} idx={trackIdx}");

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Canvas press: create gesture or click-select.
        var resolved = ResolveCreateType(e.KeyModifiers);
        if (resolved is null)
        {
            var (_, hitBlock) = HitBlock(pos);
            if (hitBlock is not null)
            {
                _log.LogDebug($"[PRESS-SELECT] hit '{hitBlock.Label}' on '{track.Name}'");
                ApplySelection(track, hitBlock);
            }
            else
            {
                _log.LogDebug("[PRESS-SELECT] miss — clearing selection");
                ApplySelection(null, null);
            }
            e.Handled = true;
            return;
        }

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

        if (_dragKind == DragKind.None)
            UpdateToolPreview(pos, e.KeyModifiers);

        if (_dragKind == DragKind.None || _isAnimating) return;

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

                // Click on track header (Reorder gesture, no motion) in Select
                // mode → promote to a track selection. This mirrors the block
                // click-to-select flow: same Delete button, different target.
                if (_dragKind == DragKind.Reorder
                    && _dragging is not null
                    && _toolMode.Tool == ToolMode.Select)
                {
                    _log.LogDebug($"[SELECT-TRACK] track={_dragging.Name}");
                    ApplySelection(_dragging, null);
                }

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

    // ── Track-header actions: add / rename ───────────────────────────────

    /// <summary>
    /// Open the Add Track dialog and, on confirm, append a new track with
    /// the user's chosen name. Default pre-fills to "Track N" so a quick
    /// Enter accepts a reasonable placeholder — but the user can type
    /// anything before confirming.
    /// </summary>
    private async void OnAddTrackClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null || Vm is null) return;

        var defaultName = $"Track {Vm.Tracks.Count + 1}";
        var name = await AddTrackDialog.ShowAsync(owner, defaultName);
        if (name is null) return;

        Vm.AddTrack(name);
    }

    /// <summary>
    /// Right-click on a track header → flip the row into rename mode
    /// (alternative entry point alongside double-click).
    /// Left-click falls through to OnPressed and starts a reorder gesture
    /// (which is further promoted to a track-select in OnReleased if the
    /// user didn't actually drag, and the current tool is Select).
    ///
    /// Focus and select-all are deferred via <see cref="Dispatcher"/>
    /// because the TextBox IsVisible flips in the same tick — the
    /// TextBox has to finish layout before it can receive focus.
    /// </summary>
    private void OnTrackHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        if (ctrl.DataContext is not ITrackViewModel track) return;

        var props = e.GetCurrentPoint(ctrl).Properties;
        if (!props.IsRightButtonPressed) return;

        BeginInlineRename(ctrl, track);
        e.Handled = true;
    }

    /// <summary>
    /// Double-click on a track header → inline rename. Mirrors the
    /// way file-explorer-style UIs let the user rename a row by
    /// double-clicking its label, and matches Kerim's earlier
    /// expectation that the existing right-click affordance was a
    /// fallback rather than the primary path. Left single click is
    /// still reorder-or-select; double-click escalates to rename.
    ///
    /// Avalonia's <c>DoubleTapped</c> fires after the second pointer
    /// release, so the first PointerPressed has already started a
    /// reorder gesture. We don't try to undo that — the user hasn't
    /// moved past the drag threshold so the gesture is harmless and
    /// the next OnReleased will end it cleanly.
    /// </summary>
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
        // value into Name; just exit edit mode.
        CommitRename(track);
    }

    private void OnTrackRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not ITrackViewModel track) return;

        if (e.Key == Key.Enter)
        {
            CommitRename(track);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Revert: two-way binding has been pushing characters into
            // Name as the user typed, so restore the pre-edit snapshot.
            if (_renameSnapshot is var (snapTrack, snapName) && snapTrack == track)
                track.Name = snapName;
            CommitRename(track);
            e.Handled = true;
        }
    }

    private void CommitRename(ITrackViewModel track)
    {
        // Empty rename collapses back to a placeholder rather than leaving
        // an invisible-label track lying around.
        if (string.IsNullOrWhiteSpace(track.Name))
            track.Name = "Track";

        track.IsEditing = false;
        _renameSnapshot = null;
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
