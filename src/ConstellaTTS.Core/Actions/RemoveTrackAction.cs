using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Remove a track (and every block on it) from the timeline.
/// Symmetric counterpart to <see cref="AddTrackAction"/> — the two
/// close the undo/redo loop:
///
///   • Removing a track captures its full <see cref="TrackData"/>
///     snapshot (name, order, blocks) just before the deletion, so
///     <see cref="Reverse"/> can hand a fresh
///     <see cref="AddTrackAction"/> the data it needs to restore
///     the row exactly where it was.
///
///   • Adding a track via <see cref="AddTrackAction"/> reverses
///     into a <see cref="RemoveTrackAction"/> targeting the
///     freshly-created VM. That remove's snapshot is empty
///     (no blocks yet), so the round-trip stays consistent
///     without special-casing.
///
/// <para>
/// <b>Snapshot timing.</b> Snapshot is taken inside
/// <see cref="Execute"/>, just before the VM removal — at that
/// point the VM still holds the track's blocks and current name,
/// and snapshotting from VM-state (rather than from the persisted
/// project) keeps the action self-contained even if Persist runs
/// against a different project instance later. The original
/// list-index is captured the same way; <see cref="Reverse"/>
/// hands both to the resurrected <see cref="AddTrackAction"/>.
/// </para>
///
/// <para>
/// Self-flushing — the manager's <see cref="IProjectManager.SaveAsync"/>
/// is called from inside <see cref="Persist"/>; the caller's
/// pattern stays a clean three-step <c>Execute → Persist → Push</c>.
/// </para>
/// </summary>
public sealed class RemoveTrackAction : ActionBase, IReversible, IPersistable
{
    private readonly TrackListViewModel _listVm;
    private readonly ITrackViewModel    _track;

    /// <summary>
    /// Pre-removal state captured inside <see cref="Execute"/> for
    /// the undo path. Null before Execute runs.
    /// </summary>
    private TrackData? _snapshot;

    /// <summary>
    /// VM index of <see cref="_track"/> just before it was removed.
    /// Captured inside <see cref="Execute"/>; -1 means "not yet
    /// captured" (Persist or Reverse called without Execute would
    /// itself be a caller bug).
    /// </summary>
    private int _removedIndex = -1;

    public override string  Id          => "RemoveTrackAction";
    public override string  Name        => "Track Sil";
    public override string? Description => $"'{_track.Name}' adlı track'i kaldırır.";

    public RemoveTrackAction(TrackListViewModel listVm, ITrackViewModel track)
    {
        _listVm = listVm;
        _track  = track;
    }

    public override void Execute(object? data = null)
    {
        // Snapshot first — once the VM is removed, both the index
        // and the block list become unreachable from this action.
        _removedIndex = _listVm.Tracks.IndexOf(_track);
        _snapshot     = new TrackData
        {
            Name   = _track.Name,
            Order  = _track.Order,
            Blocks = _track.Sections.Select(BlockSerialization.ToData).ToList(),
        };

        // RemoveTrack also renumbers Order across the remaining
        // tracks (see TrackListViewModel.RemoveTrack), keeping the
        // contiguous 0..N-1 invariant on the VM side. The
        // corresponding project-side renumber happens inside
        // ConstellaProject.RemoveTrack during Persist below.
        _listVm.RemoveTrack(_track);
    }

    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null || _snapshot is null) return;

        // Project-side remove. The track's name is the primary key
        // here; ordinal-case-sensitive match is the contract on
        // IConstellaProject.RemoveTrack, and the snapshot was taken
        // from the same VM Execute removed from, so the names line
        // up.
        project.RemoveTrack(_snapshot.Name);

        await manager.SaveAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns an <see cref="AddTrackAction"/> carrying both the
    /// snapshot and the original list-index. The reconstructed
    /// track lands at the same slot it was removed from — Ctrl+Z
    /// of a delete-from-the-middle puts the row back where the
    /// user expects to find it, with all of its blocks intact.
    /// </remarks>
    public IAction Reverse(IReversible? previous, object? data = null)
    {
        if (_snapshot is null)
            throw new InvalidOperationException(
                "RemoveTrackAction.Reverse called before Execute populated the snapshot.");

        return new AddTrackAction(_listVm, _snapshot, _removedIndex);
    }
}
