
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Removes a specific block from a specific track, optionally restoring
/// sections that were previously bumped to the right by a
/// <see cref="CreateBlockAction"/>. Symmetric counterpart to
/// <see cref="CreateBlockAction"/> — the two close the undo/redo loop:
/// removing a created block produces a CreateBlockAction that puts it
/// back (and re-applies the same bumps), which itself reverses to this
/// class.
///
/// Holds concrete VM references so the same block is removed regardless
/// of any intervening reorders.
///
/// <para>
/// <b>Persist semantics.</b> The block's persisted-side ID is
/// <c>{trackName}/{indexAtTimeOfExecute}</c>. The index is captured
/// inside <see cref="Execute"/>, just before the VM removal, because
/// after the removal the block is no longer in the collection and
/// IndexOf would return -1. <see cref="Persist"/> uses the captured
/// index plus the bump snapshot to undo the create's persisted side
/// in the same order Execute touched the VM: remove the target
/// block first (so any bumped neighbours can shift back without
/// transiently colliding with it on disk), then update each bumped
/// neighbour's <see cref="BlockData.StartSec"/> back to its
/// original value.
/// </para>
///
/// <para>
/// Self-flushing — same shape as <see cref="CreateBlockAction"/>;
/// the caller's pattern stays a clean three-step
/// <c>Execute → Persist → Push</c>.
/// </para>
/// </summary>
public sealed class RemoveBlockAction : ActionBase, IReversible, IPersistable
{
    private readonly ITrackViewModel             _track;
    private readonly IStageViewModel             _block;
    private readonly IReadOnlyList<BumpRecord>?  _bumpsToRestore;

    /// <summary>
    /// VM index of <see cref="_block"/> just before it was removed.
    /// Captured inside <see cref="Execute"/> rather than the
    /// constructor so it reflects the live position at removal
    /// time — between construction and execute, undo of an earlier
    /// reorder could have shifted blocks within the same track.
    /// -1 means "not yet captured" (Persist called without Execute,
    /// which would itself be a caller bug).
    /// </summary>
    private int _removedIndex = -1;

    public override string  Id          => "RemoveBlockAction";
    public override string  Name        => "Block Sil";
    public override string? Description => $"{_track.Name} track'inden '{_block.Label}' bloğunu kaldırır.";

    /// <summary>
    /// Construct a plain block-removal with no bumps to restore. Used when
    /// the block was never the subject of a create-time bump (or when the
    /// caller doesn't have access to the bump snapshot).
    /// </summary>
    public RemoveBlockAction(ITrackViewModel track, IStageViewModel block)
        : this(track, block, bumpsToRestore: null) { }

    /// <summary>
    /// Construct a block-removal that also undoes a set of bumps applied
    /// when the block was created. Used as the inverse produced by
    /// <see cref="CreateBlockAction.Reverse"/>.
    /// </summary>
    public RemoveBlockAction(
        ITrackViewModel             track,
        IStageViewModel             block,
        IReadOnlyList<BumpRecord>?  bumpsToRestore)
    {
        _track          = track;
        _block          = block;
        _bumpsToRestore = bumpsToRestore;
    }

    public override void Execute(object? data = null)
    {
        // Capture the VM index BEFORE removal — Persist needs it to
        // build the block's on-disk ID, and once the block leaves
        // the collection IndexOf returns -1.
        _removedIndex = _track.Sections.IndexOf(_block);

        // Remove the block first so bumped sections can freely move back
        // without transiently colliding with the block we're about to remove.
        _track.Sections.Remove(_block);

        if (_bumpsToRestore is not null && _bumpsToRestore.Count > 0)
            BlockBumping.Restore(_track, _bumpsToRestore);
    }

    /// <inheritdoc />
    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null) return;
        if (_removedIndex < 0) return; // defensive: Persist called without Execute

        // Remove on disk first, mirroring the VM order. The persisted
        // block at index _removedIndex on this track is the same
        // logical block IndexOf returned in Execute — the index
        // contract between TrackData.Blocks and VM Sections (see
        // TrackViewModel hydration) makes the two indices
        // interchangeable.
        var blockId = $"{_track.Name}/{_removedIndex}";
        project.RemoveBlock(blockId);

        // Bumped neighbours had their StartSec restored in the VM
        // (BlockBumping.Restore moved them back leftward); push the
        // same change into the manifest. After RemoveBlock above
        // every remaining persisted block has shifted down by one
        // index if it was after the removed block in the on-disk
        // list, or stayed put if it was before. The same shift
        // happened in the VM when Sections.Remove ran in Execute.
        // Because the index contract pairs VM and on-disk lists
        // position-for-position, IndexOf in the VM gives exactly
        // the post-shift on-disk index for each bumped neighbour
        // — no manual offset bookkeeping needed.
        if (_bumpsToRestore is not null)
        {
            foreach (var bump in _bumpsToRestore)
            {
                var idx = _track.Sections.IndexOf(bump.Section);
                if (idx < 0) continue;
                var bumpId = $"{_track.Name}/{idx}";
                project.UpdateBlock(bumpId, BlockSerialization.ToData(bump.Section));
            }
        }

        await manager.SaveAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a <see cref="CreateBlockAction"/> targeting the same
    /// (track, block) pair. On redo, that action will re-run the bumping
    /// computation against the current collection state. Because the
    /// collection state was just restored to pre-create by this action's
    /// Execute, the recomputed bumps will match the original — idempotent
    /// round-trip.
    /// </remarks>
    public IAction Reverse(IReversible? previous, object? data = null) =>
        new CreateBlockAction(_track, _block);
}
