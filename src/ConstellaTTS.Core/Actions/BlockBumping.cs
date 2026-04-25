
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Represents a single block being pushed to the right as a side-effect
/// of a new block's insertion. The action that caused the bump keeps
/// these records so Undo can restore the original positions.
/// </summary>
public readonly record struct BumpRecord(IStageViewModel Section, double OriginalStartSec, double NewStartSec);

/// <summary>
/// Collision-resolution logic for block creation: when a new block is
/// added and overlaps existing blocks on the same track, the overlapping
/// blocks slide right to make room, cascading through the sequence.
///
/// The rule is directional — blocks only move to the RIGHT, never left.
/// The create gesture's anchor-snap (pressing inside an existing block
/// snaps the anchor to that block's EndSec) guarantees the new block
/// never starts to the LEFT of a block it overlaps, so leftward bumping
/// is not needed.
///
/// Kind-agnostic: operates on <see cref="IStageViewModel"/>, so sections
/// and stages collide and bump each other identically. The iteration
/// covers the entire Sections collection; there's no Section-vs-Stage
/// filter anywhere.
/// </summary>
public static class BlockBumping
{
    /// <summary>
    /// Compute the set of existing blocks that need to slide right to make
    /// room for <paramref name="newBlock"/> at its (StartSec, EndSec). The
    /// new block itself is not included in the returned list; the caller
    /// inserts it separately after applying the bumps.
    /// </summary>
    /// <remarks>
    /// Pure function — no mutation. The caller applies bumps by assigning
    /// <c>Section.StartSec = NewStartSec</c> via <see cref="Apply"/>.
    ///
    /// Algorithm. Sort existing blocks by StartSec, walk left-to-right,
    /// maintain a rightmost-edge watermark that begins at
    /// <c>newBlock.EndSec</c>. Any existing block that starts before the
    /// watermark overlaps the combined (newBlock + cascade) span and is
    /// pushed to the watermark. The watermark then advances to that
    /// block's new EndSec, which may trigger further cascades.
    ///
    /// Starting the watermark at <c>newBlock.EndSec</c> is the key — the
    /// new block anchors the push front without needing to insert it into
    /// the sort (which was bug-prone: stable sort ties made it land AFTER
    /// an existing block with the same StartSec, and the algorithm then
    /// failed to push the existing block).
    /// </remarks>
    public static List<BumpRecord> Compute(ITrackViewModel track, IStageViewModel newBlock)
    {
        var bumps     = new List<BumpRecord>();
        var rightEdge = newBlock.EndSec;

        // Existing blocks only — newBlock hasn't been added to the
        // collection yet (the caller does that after applying bumps).
        var existing = track.Sections.OrderBy(s => s.StartSec).ToList();

        foreach (var s in existing)
        {
            // Entirely to the left of the new block — won't collide.
            // Adjacent edges count as "left of" via <= (no bumping needed
            // when s.EndSec exactly touches newBlock.StartSec).
            if (s.EndSec <= newBlock.StartSec) continue;

            // Starts at or beyond the current right edge — no overlap,
            // and because the list is sorted, nothing further right can
            // overlap either. Done.
            if (s.StartSec >= rightEdge) break;

            // Overlap. Push this block forward to the current right edge;
            // its new EndSec extends the edge for the next iteration.
            bumps.Add(new BumpRecord(s, s.StartSec, rightEdge));
            rightEdge = rightEdge + s.DurationSec;
        }

        return bumps;
    }

    /// <summary>Apply bumps: move each recorded section to its new start.</summary>
    public static void Apply(IReadOnlyList<BumpRecord> bumps)
    {
        foreach (var b in bumps)
            b.Section.StartSec = b.NewStartSec;
    }

    /// <summary>
    /// Restore bumped sections to their original positions, used during
    /// undo. Sections no longer in <paramref name="track"/> are skipped
    /// defensively — they may have been removed by some other action
    /// between the original bump and this restore call.
    /// </summary>
    public static void Restore(ITrackViewModel track, IReadOnlyList<BumpRecord> bumps)
    {
        foreach (var b in bumps)
        {
            if (track.Sections.Contains(b.Section))
                b.Section.StartSec = b.OriginalStartSec;
        }
    }
}
