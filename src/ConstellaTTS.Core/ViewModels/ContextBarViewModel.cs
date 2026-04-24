using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.UI.Tools;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// ViewModel for the context bar — hosts the tool mode radio group
/// (Select / Create) and, underneath Create, the sub-type radio
/// (Section / Stage). The sub-radio only surfaces while Create is
/// active; in Select mode it's hidden to avoid misleading affordances.
///
/// Dual state plumbing:
///   · IToolModeService  — Tool + CreateType + preview overrides.
///     Section/Stage highlights bind to the effective (preview-aware)
///     values so that the brief Ctrl hover preview lights up the matching
///     sub-type button even while committed Tool is Select.
///   · ISelectionService — drives Delete button visibility.
///
/// Delete command goes through the history stack via RemoveBlockAction,
/// so Ctrl+Z brings the block back with its bumps intact.
/// </summary>
public sealed partial class ContextBarViewModel : ObservableObject
{
    private readonly IToolModeService   _tools;
    private readonly ISelectionService  _selection;
    private readonly IHistoryManager    _history;
    private readonly TrackListViewModel _trackList;

    public ContextBarViewModel(
        IToolModeService   tools,
        ISelectionService  selection,
        IHistoryManager    history,
        TrackListViewModel trackList)
    {
        _tools     = tools;
        _selection = selection;
        _history   = history;
        _trackList = trackList;

        _tools.PropertyChanged     += OnToolsChanged;
        _selection.PropertyChanged += OnSelectionChanged;
    }

    // ── Static placeholders (unchanged — real data later) ────────────────
    public string SectionLabel { get; init; } = "Giriş · 2 track · 0:32";
    public string BlockCount   { get; init; } = "blocks 4";
    public string Hints        { get; init; } =
        "Ctrl+Scroll=zoom · Minimap'te seç=fit · Block'a tıkla=editör";

    // ── Active-state flags for Classes.active bindings ───────────────────
    // Read from EffectiveTool / EffectiveCreateType so that the preview
    // override (Ctrl hover on canvas) visually lights up the right button
    // while the user decides whether to actually draw.
    public bool IsSelectTool => _tools.EffectiveTool == ToolMode.Select;
    public bool IsCreateTool => _tools.EffectiveTool == ToolMode.Create;

    /// <summary>
    /// Section/Stage buttons only surface in Create mode. Hiding (rather
    /// than disabling) removes the visual clutter in Select mode where
    /// the sub-type has no meaning.
    /// </summary>
    public bool IsSubTypeVisible => _tools.EffectiveTool == ToolMode.Create;

    public bool IsSection => IsCreateTool && _tools.EffectiveCreateType == CreateType.Section;
    public bool IsStage   => IsCreateTool && _tools.EffectiveCreateType == CreateType.Stage;

    /// <summary>
    /// True when the user has a block selected AND is in Select mode.
    /// Controls the visibility of the toolbar Delete button — keeping it
    /// out of Create mode avoids accidental deletes while drawing.
    /// </summary>
    public bool IsDeleteVisible => IsSelectTool && _selection.SelectedBlock is not null;

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Activate Select tool. CreateType is preserved so returning
    /// to Create keeps the user's last sub-selection.</summary>
    [RelayCommand]
    private void ActivateSelect() => _tools.Tool = ToolMode.Select;

    /// <summary>Activate Create tool without touching the sub-type.</summary>
    [RelayCommand]
    private void ActivateCreate() => _tools.Tool = ToolMode.Create;

    /// <summary>
    /// Select Section as the create sub-type. Also flips the primary tool
    /// to Create if it wasn't already, matching user intent: "I want to
    /// draw a section" implies "I want Create mode".
    /// Re-clicking is a no-op; to switch type, click Stage.
    /// </summary>
    [RelayCommand]
    private void SelectSection()
    {
        if (_tools.Tool != ToolMode.Create) _tools.Tool = ToolMode.Create;
        _tools.CreateType = CreateType.Section;
    }

    /// <summary>Select Stage as the create sub-type. Same semantics as <see cref="SelectSection"/>.</summary>
    [RelayCommand]
    private void SelectStage()
    {
        if (_tools.Tool != ToolMode.Create) _tools.Tool = ToolMode.Create;
        _tools.CreateType = CreateType.Stage;
    }

    /// <summary>
    /// Delete the currently-selected block. Runs through the history
    /// stack (RemoveBlockAction) so Ctrl+Z restores it. Clears the
    /// selection after commit — editor overlay closes automatically.
    /// </summary>
    [RelayCommand]
    private void DeleteSelection()
    {
        var block = _selection.SelectedBlock;
        var track = _selection.SelectedTrack;
        if (block is null || track is null) return;

        var action = new RemoveBlockAction(track, block);
        action.Execute();
        _history.Push(action);

        _selection.SelectedBlock = null;
        _selection.SelectedTrack = null;
    }

    /// <summary>
    /// Append a new empty track. Not history-tracked yet — track-level
    /// actions will fold into the undo stack when the action infrastructure
    /// grows to cover structural changes.
    /// </summary>
    [RelayCommand]
    private void AddTrack() => _trackList.AddTrack();

    // ── Service change plumbing ──────────────────────────────────────────

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any tool/preview change flips derived bools. Re-notify all of
        // them; XAML re-evaluates style classes + visibility.
        OnPropertyChanged(nameof(IsSelectTool));
        OnPropertyChanged(nameof(IsCreateTool));
        OnPropertyChanged(nameof(IsSubTypeVisible));
        OnPropertyChanged(nameof(IsSection));
        OnPropertyChanged(nameof(IsStage));
        OnPropertyChanged(nameof(IsDeleteVisible));
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDeleteVisible));
    }
}
