using System;
using System.Collections.Generic;
using ConstellaTTS.SDK;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Actions;

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
/// </summary>
public sealed class RemoveBlockAction : ActionBase, IReversible
{
    private readonly ITrackViewModel             _track;
    private readonly IStageViewModel             _block;
    private readonly IReadOnlyList<BumpRecord>?  _bumpsToRestore;

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
        // Remove the block first so bumped sections can freely move back
        // without transiently colliding with the block we're about to remove.
        _track.Sections.Remove(_block);

        if (_bumpsToRestore is not null && _bumpsToRestore.Count > 0)
            BlockBumping.Restore(_track, _bumpsToRestore);
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
    public IAction Reverse(IReversible? previous, params object[] args) =>
        new CreateBlockAction(_track, _block);
}
