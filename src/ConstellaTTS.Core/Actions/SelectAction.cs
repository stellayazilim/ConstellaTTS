
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Records a selection change so Ctrl+Z can step backwards through the
/// "what was selected" timeline. Captures both the previous and the new
/// (track, block) pair at construction time; Execute applies the new
/// pair, Reverse returns a SelectAction that swaps them so undo/redo
/// loops indefinitely.
///
/// <para>
/// <b>Why selection is on the undo stack at all.</b> Earlier we kept
/// selection out of history on the assumption that Ctrl+Z should be
/// reserved for structural changes (block create / delete / move).
/// Practice argued the other way: users frequently click into a block,
/// glance at it, then want to bounce back to the block they were
/// editing before — Ctrl+Z is the natural verb for that. Putting
/// selection on the same stack lets one keystroke serve both intents.
/// </para>
///
/// <para>
/// <b>One Ctrl+Z, one selection step.</b> Each click produces its own
/// undo entry; we don't merge consecutive selections into a single
/// entry. The reasoning is twofold: a single Ctrl+Z stepping back
/// exactly one selection matches the precedent set by Spine2D, Vim's
/// jumplist, and most IDE navigation histories — users mentally model
/// "go back" as "the last thing I did", not "everything I did since
/// I last touched something else". And merging requires the action to
/// inspect what the previous undo entry was, which is the history
/// manager's job, not the action's; keeping the action stateless and
/// self-contained is the simpler design.
/// </para>
///
/// <para>
/// <b>What gets snapshotted.</b> Concrete VM references for both the
/// track and the block. We don't track-by-id because a future
/// remove-track / restore-track flow would let identical IDs point at
/// new instances; reference identity matches the lifetime semantics of
/// every other action in the codebase (see CreateBlockAction). If the
/// referenced block has been deleted by the time Reverse runs, the
/// SelectionService writes accept null fine — the editor just closes
/// the overlay, which is the correct behaviour for "go back to a
/// selection that no longer exists".
/// </para>
/// </summary>
public sealed class SelectAction : ActionBase, IReversible
{
    private readonly ISelectionService _selection;

    private readonly ITrackViewModel? _fromTrack;
    private readonly IStageViewModel? _fromBlock;
    private readonly ITrackViewModel? _toTrack;
    private readonly IStageViewModel? _toBlock;

    public override string  Id   => "SelectAction";
    public override string  Name => "Seçim";

    public SelectAction(
        ISelectionService selection,
        ITrackViewModel?  fromTrack,
        IStageViewModel?  fromBlock,
        ITrackViewModel?  toTrack,
        IStageViewModel?  toBlock)
    {
        _selection = selection;
        _fromTrack = fromTrack;
        _fromBlock = fromBlock;
        _toTrack   = toTrack;
        _toBlock   = toBlock;
    }

    /// <summary>
    /// Apply the "to" pair to the selection service. Used both at
    /// initial creation (the click handler builds + executes + pushes
    /// in sequence) and during redo (Reverse on the inverse returns a
    /// fresh SelectAction whose Execute restores the forward state).
    /// </summary>
    public override void Execute(object? data = null)
    {
        // Setting block before track would briefly leave a state where
        // the block belongs to the old track, which the editor overlay
        // could read and reposition incorrectly mid-update. Track first,
        // block second keeps the pair consistent.
        _selection.SelectedTrack = _toTrack;
        _selection.SelectedBlock = _toBlock;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a SelectAction whose from/to are swapped. The caller
    /// (history manager) will execute it, restoring the prior
    /// selection; pushing it onto the redo stack means the same
    /// instance, when reversed again, produces a forward SelectAction
    /// with the original direction.
    /// </remarks>
    public IAction Reverse(IReversible? previous, params object[] args) =>
        new SelectAction(_selection, _toTrack, _toBlock, _fromTrack, _fromBlock);
}
