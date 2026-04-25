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
///   · ISelectionService — drives Delete button visibility and payload.
///
/// Delete is context-sensitive:
///   · Block selected → RemoveBlockAction through history (Ctrl+Z undoes).
///   · Track selected (no block) → TrackListViewModel.RemoveTrack.
///     Track removal isn't history-tracked yet; a RemoveTrackAction will
///     slot in once the action infrastructure covers structural changes.
///
/// Track add is owned by <c>TrackListViewModel</c> and surfaced inline
/// in the SECTIONS header via a click handler — keeping that action
/// next to its target list rather than in the global toolbar.
///
/// Earlier this VM exposed three placeholder strings for a section
/// summary label and a hint cheat-sheet — both removed when the
/// view stripped those columns. Per-section info doesn't belong on
/// a tool toolbar, and interaction hints are better placed in
/// tooltips and a help panel than in chrome.
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

    // ── Active-state flags ───────────────────────────────────────────────
    public bool IsSelectTool => _tools.EffectiveTool == ToolMode.Select;
    public bool IsCreateTool => _tools.EffectiveTool == ToolMode.Create;

    public bool IsSubTypeVisible => _tools.EffectiveTool == ToolMode.Create;

    public bool IsSection => IsCreateTool && _tools.EffectiveCreateType == CreateType.Section;
    public bool IsStage   => IsCreateTool && _tools.EffectiveCreateType == CreateType.Stage;

    /// <summary>
    /// True when there's something to delete AND we're in Select mode.
    /// Keeps Delete out of Create mode so accidental clicks while drawing
    /// can't destroy work. Accepts either a block or a track selection.
    /// </summary>
    public bool IsDeleteVisible =>
        IsSelectTool
        && (_selection.SelectedBlock is not null || _selection.SelectedTrack is not null);

    /// <summary>
    /// Dynamic label reflecting what Delete would remove. "Block Sil" when
    /// a block is selected, "Track Sil" when only a track. Empty when
    /// IsDeleteVisible is false — button is hidden so the text doesn't
    /// matter, but binding stays well-defined.
    /// </summary>
    public string DeleteLabel
    {
        get
        {
            if (_selection.SelectedBlock is not null) return "Block Sil";
            if (_selection.SelectedTrack is not null) return "Track Sil";
            return string.Empty;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateSelect() => _tools.Tool = ToolMode.Select;

    [RelayCommand]
    private void ActivateCreate() => _tools.Tool = ToolMode.Create;

    [RelayCommand]
    private void SelectSection()
    {
        if (_tools.Tool != ToolMode.Create) _tools.Tool = ToolMode.Create;
        _tools.CreateType = CreateType.Section;
    }

    [RelayCommand]
    private void SelectStage()
    {
        if (_tools.Tool != ToolMode.Create) _tools.Tool = ToolMode.Create;
        _tools.CreateType = CreateType.Stage;
    }

    /// <summary>
    /// Delete whatever the selection points to. Block wins over track if
    /// both are set (block is the more specific selection — its containing
    /// track is only set for editor positioning). Clears selection after
    /// commit so the editor overlay and Delete button both go away.
    /// </summary>
    [RelayCommand]
    private void DeleteSelection()
    {
        var block = _selection.SelectedBlock;
        var track = _selection.SelectedTrack;

        if (block is not null && track is not null)
        {
            var action = new RemoveBlockAction(track, block);
            action.Execute();
            _history.Push(action);
        }
        else if (track is not null)
        {
            // Plain remove for now — structural actions will join the
            // history stack once RemoveTrackAction lands.
            _trackList.RemoveTrack(track);
        }

        _selection.SelectedBlock = null;
        _selection.SelectedTrack = null;
    }

    // ── Service change plumbing ──────────────────────────────────────────

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        OnPropertyChanged(nameof(DeleteLabel));
    }
}
