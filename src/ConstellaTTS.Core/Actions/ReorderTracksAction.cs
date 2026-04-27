using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Move a track from one list position to another. Self-symmetric —
/// <see cref="Reverse"/> returns a fresh
/// <see cref="ReorderTracksAction"/> with the indices swapped, so
/// undo / redo walk the same swap-pair indefinitely.
///
/// <para>
/// <b>Indices, not references.</b> The action targets indices on
/// purpose: the only state needed to identify a reorder is "from
/// slot N, to slot M", and an index pair survives the rest of the
/// undo stack interacting with the same track collection. A track
/// reference would also work for the move itself, but Reverse
/// would have to recompute the reverse-from index from current
/// state, and any add/remove between the original action and its
/// reversal would shift that index out from under it. Indices
/// stay simple.
/// </para>
///
/// <para>
/// <b>Permissive bounds.</b> Both the VM
/// (<see cref="TrackListViewModel.Reorder"/>) and the project
/// (<see cref="SDK.App.IConstellaProject.ReorderTracks"/>) treat
/// out-of-range indices as no-ops rather than throwing; this
/// action inherits that contract. A reverse computed against a
/// list whose size has shrunk degrades quietly.
/// </para>
///
/// <para>
/// Self-flushing — <see cref="IProjectManager.SaveAsync"/> is
/// called inside <see cref="Persist"/>.
/// </para>
/// </summary>
public sealed class ReorderTracksAction : ActionBase, IReversible, IPersistable
{
    private readonly TrackListViewModel _listVm;
    private readonly int                _fromIdx;
    private readonly int                _toIdx;

    public override string  Id          => "ReorderTracksAction";
    public override string  Name        => "Track Sırasını Değiştir";
    public override string? Description => $"Track {_fromIdx} → {_toIdx}.";

    public ReorderTracksAction(TrackListViewModel listVm, int fromIdx, int toIdx)
    {
        _listVm  = listVm;
        _fromIdx = fromIdx;
        _toIdx   = toIdx;
    }

    public override void Execute(object? data = null)
    {
        // The view-model's Reorder also handles the Order-renumber
        // pass; nothing extra to do here.
        _listVm.Reorder(_fromIdx, _toIdx);
    }

    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null) return;

        project.ReorderTracks(_fromIdx, _toIdx);
        await manager.SaveAsync();
    }

    /// <inheritdoc />
    public IAction Reverse(IReversible? previous, object? data = null) =>
        new ReorderTracksAction(_listVm, _toIdx, _fromIdx);
}
