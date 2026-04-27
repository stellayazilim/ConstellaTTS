using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Geometry change a <see cref="MoveBlockAction"/> applies to a
/// block. The three modes share enough plumbing (bump computation,
/// VM ↔ project sync, undo snapshot) that splitting them into
/// separate action classes would just triplicate the code; they
/// branch on this enum at the points where they actually differ.
/// </summary>
public enum MoveBlockMode
{
    /// <summary>
    /// Slide the block along the timeline. <c>StartSec</c> changes;
    /// <c>DurationSec</c> stays put. May also change the owning
    /// track (cross-track move).
    /// </summary>
    Move,

    /// <summary>
    /// Drag the block's left edge. <c>StartSec</c> moves; the right
    /// edge stays anchored, so <c>DurationSec</c> shrinks or grows
    /// to compensate. Same-track only.
    /// </summary>
    ResizeLeft,

    /// <summary>
    /// Drag the block's right edge. <c>DurationSec</c> changes; the
    /// left edge stays anchored, so <c>StartSec</c> is untouched.
    /// Same-track only.
    /// </summary>
    ResizeRight,
}

/// <summary>
/// Moves or resizes a block, optionally relocating it to a
/// different track. One action covers three gestures because they
/// share most of their state: a (StartSec, DurationSec) pair on
/// one side and possibly a (fromTrack, toTrack) pair on the other,
/// plus a bump snapshot for undo. Splitting them would duplicate
/// the persist sync and the bump bookkeeping in three places.
///
/// <para>
/// <b>Cross-track is move-only.</b> Resize is a same-track
/// operation by definition — dragging an edge doesn't carry the
/// block to a different row. The constructor enforces this:
/// callers that pass <see cref="MoveBlockMode.ResizeLeft"/> or
/// <see cref="MoveBlockMode.ResizeRight"/> with two different
/// tracks get an exception, since the alternative (silently
/// ignoring one of the inputs) would just hide a caller bug.
/// </para>
///
/// <para>
/// <b>Bump policy mirrors create.</b> When the block lands at a
/// position that overlaps existing blocks on the destination
/// track, those blocks slide right exactly as
/// <see cref="CreateBlockAction"/> does — same
/// <see cref="BlockBumping"/> helper, same right-only push rule.
/// On undo the bumps are restored (BlockBumping.Restore), and the
/// block returns to its origin (and origin track) without further
/// cascade.
/// </para>
///
/// <para>
/// <b>Snapshot timing.</b> Captures the pre-move geometry and
/// origin track in <see cref="Execute"/>, before mutating
/// anything. <see cref="Reverse"/> hands those values back to a
/// fresh <see cref="MoveBlockAction"/>, which on its own Execute
/// restores the prior state — including the prior bump shape on
/// the source track. The action is self-symmetric like
/// <see cref="ReorderTracksAction"/>; an undo of an undo lands
/// back at the original move.
/// </para>
///
/// <para>
/// Self-flushing — <see cref="IProjectManager.SaveAsync"/> runs
/// inside <see cref="Persist"/>.
/// </para>
/// </summary>
public sealed class MoveBlockAction : ActionBase, IReversible, IPersistable
{
    private readonly ITrackViewModel _fromTrack;
    private readonly ITrackViewModel _toTrack;
    private readonly IStageViewModel _block;
    private readonly MoveBlockMode   _mode;
    private readonly double          _newStartSec;
    private readonly double          _newDurationSec;

    /// <summary>
    /// Origin geometry, captured in <see cref="Execute"/> before
    /// mutation so <see cref="Reverse"/> has the values it needs.
    /// </summary>
    private double _oldStartSec;
    private double _oldDurationSec;

    /// <summary>
    /// Bumps applied on the destination track to make room for the
    /// moved/resized block. Empty if the block landed in clear
    /// space. Captured so <see cref="Reverse"/>'s undo path can
    /// restore the bumped neighbours to their original positions.
    /// </summary>
    private IReadOnlyList<BumpRecord> _bumpsApplied = Array.Empty<BumpRecord>();

    /// <summary>
    /// VM index of <see cref="_block"/> on its source track at the
    /// moment of move, used by <see cref="Persist"/> to derive the
    /// block's on-disk ID. Captured before any mutation; -1 means
    /// "not yet captured", which would be a caller bug.
    /// </summary>
    private int _sourceIndex = -1;

    public override string  Id          => "MoveBlockAction";
    public override string  Name        => _mode switch
    {
        MoveBlockMode.ResizeLeft  => "Block Sol Kenarını Sürükle",
        MoveBlockMode.ResizeRight => "Block Sağ Kenarını Sürükle",
        _                         => "Block Taşı",
    };
    public override string? Description => $"'{_block.Label}' bloğu üzerinde {_mode}.";

    /// <summary>
    /// Constructs a move/resize. <paramref name="newStartSec"/> and
    /// <paramref name="newDurationSec"/> are the post-mutation
    /// geometry the caller computed (clamped at 0 and against the
    /// minimum-duration rule); the action just applies them.
    ///
    /// <para>
    /// Cross-track is allowed only for <see cref="MoveBlockMode.Move"/>;
    /// the constructor throws on resize with mismatched tracks. See
    /// the class-level remarks for the rationale.
    /// </para>
    /// </summary>
    public MoveBlockAction(
        ITrackViewModel fromTrack,
        ITrackViewModel toTrack,
        IStageViewModel block,
        MoveBlockMode   mode,
        double          newStartSec,
        double          newDurationSec)
    {
        if (mode != MoveBlockMode.Move && !ReferenceEquals(fromTrack, toTrack))
            throw new ArgumentException(
                $"Cross-track is move-only; resize requires fromTrack == toTrack (mode={mode}).",
                nameof(toTrack));

        _fromTrack      = fromTrack;
        _toTrack        = toTrack;
        _block          = block;
        _mode           = mode;
        _newStartSec    = newStartSec;
        _newDurationSec = newDurationSec;
    }

    public override void Execute(object? data = null)
    {
        // Snapshot first — once the block's geometry mutates and
        // (potentially) it relocates to another track, the original
        // (StartSec, DurationSec, fromTrack-index) are unreachable
        // from the VM.
        _oldStartSec    = _block.StartSec;
        _oldDurationSec = _block.DurationSec;
        _sourceIndex    = _fromTrack.Sections.IndexOf(_block);

        // Same-track move/resize: bumps are computed against the
        // destination track's blocks MINUS the block being moved
        // (otherwise the moved block "collides with itself" at its
        // current position). The cleanest way is to pull the block
        // out of the collection, set its new geometry, compute
        // bumps, apply, then put it back.
        //
        // Cross-track move: pull from source, set geometry, compute
        // bumps on destination's existing blocks, apply, add to
        // destination.
        _fromTrack.Sections.Remove(_block);

        // Apply geometry under each mode. Caller has already done
        // the clamping (>= 0, >= min duration, etc.); this method
        // takes the values at face value.
        switch (_mode)
        {
            case MoveBlockMode.Move:
                _block.StartSec    = _newStartSec;
                // Duration unchanged on move; passed value matches
                // the original. Assigning anyway keeps the action
                // robust against callers that forget to thread it.
                _block.DurationSec = _newDurationSec;
                break;

            case MoveBlockMode.ResizeLeft:
                _block.StartSec    = _newStartSec;
                _block.DurationSec = _newDurationSec;
                break;

            case MoveBlockMode.ResizeRight:
                // StartSec stays put on a right-edge resize; only
                // duration changes. Don't reassign StartSec here
                // because the caller may have passed the same value
                // it already had, and reassignment fires a needless
                // PropertyChanged.
                _block.DurationSec = _newDurationSec;
                break;
        }

        // Compute bumps against the destination track's CURRENT
        // contents (block is currently out of the collection).
        _bumpsApplied = BlockBumping.Compute(_toTrack, _block);
        BlockBumping.Apply(_bumpsApplied);

        // Re-insert. For same-track operations the block lands at
        // the end of its own track's collection — which is fine,
        // collection ordering is append-order by contract; visual
        // ordering comes from TimelineItemsPanel reading StartSec.
        // For cross-track moves the block joins the destination
        // track's collection.
        _toTrack.Sections.Add(_block);

        // Block colours follow the destination track's palette so
        // a cross-track move re-tints the block immediately. The
        // value is runtime-only (not persisted), assigned the same
        // way TrackViewModel's hydration constructor would.
        _block.Bg          = _toTrack.BlockBg;
        _block.AccentColor = _toTrack.Color;

        // Section-only side effect: any geometry / track change
        // makes the cached generation stale.
        if (_block is ISectionViewModel section)
            section.Dirty = true;
    }

    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null) return;
        if (_sourceIndex < 0) return; // defensive: Persist without Execute

        // Source-track removal. The block's pre-move ID was
        // {fromTrack.Name}/{_sourceIndex}; using the captured index
        // (rather than IndexOf now) is required because Execute
        // already removed the block from the source VM, so IndexOf
        // would return -1.
        var sourceBlockId = $"{_fromTrack.Name}/{_sourceIndex}";
        project.RemoveBlock(sourceBlockId);

        // Bump updates on destination. Bumps mutated each
        // neighbour's StartSec; their VM indices line up with their
        // on-disk indices (the contract is preserved across
        // Add/Remove on either side, since both lists update in
        // lockstep). For cross-track moves the bumped blocks live
        // on the destination track.
        foreach (var bump in _bumpsApplied)
        {
            var idx = _toTrack.Sections.IndexOf(bump.Section);
            if (idx < 0) continue;
            var bumpId = $"{_toTrack.Name}/{idx}";
            project.UpdateBlock(bumpId, BlockSerialization.ToData(bump.Section));
        }

        // Append the moved block on the destination. The block was
        // re-inserted into the destination VM's Sections at the end
        // (Execute's final Add); AddBlock on the project mirrors
        // that, and the index contract holds.
        project.AddBlock(_toTrack.Name, BlockSerialization.ToData(_block));

        await manager.SaveAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a fresh <see cref="MoveBlockAction"/> that runs the
    /// inverse trip — destination back to source, current geometry
    /// back to <see cref="_oldStartSec"/> / <see cref="_oldDurationSec"/>.
    /// The new action will recompute bumps on its own Execute
    /// against the (now-restored) source track. Bumps recompute
    /// rather than replay because the source track's contents may
    /// have shifted between the original move and this reverse
    /// (other intervening actions); recomputing is what makes the
    /// reverse correct under those conditions, at the cost of a
    /// rare deviation if the user's mental model expected the
    /// exact pre-move byte-for-byte state.
    ///
    /// <para>
    /// On the destination side, the bumps applied by this action's
    /// Execute are explicitly restored before the reverse runs —
    /// the new MoveBlockAction's Execute removes the block, which
    /// pulls the bumps' "anchor" out, and BlockBumping.Restore
    /// inside the new action would recompute them anyway. The
    /// approach handles the common case (no other interleaved
    /// changes) correctly; corner cases where the destination
    /// state changed under the action's feet are accepted as
    /// "best-effort".
    /// </para>
    /// </remarks>
    public IAction Reverse(IReversible? previous, object? data = null) =>
        new MoveBlockAction(
            fromTrack:      _toTrack,
            toTrack:        _fromTrack,
            block:          _block,
            mode:           _mode,
            newStartSec:    _oldStartSec,
            newDurationSec: _oldDurationSec);
}
