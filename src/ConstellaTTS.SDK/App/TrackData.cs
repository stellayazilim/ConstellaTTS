using MessagePack;

namespace ConstellaTTS.SDK.App;

/// <summary>
/// On-disk shape of a single track — its identity, its position in the
/// track list, and the blocks placed on it. Mirrors the live
/// <c>TrackViewModel</c>, but limited to fields that actually need to
/// survive a session boundary.
///
/// <para>
/// <b>Name is the primary identifier.</b> Block IDs are formed from
/// <c>{trackName}/{blockIndex}</c>; the persisted track collection is
/// rewritten on every relevant mutation (so a rename is a fresh write
/// rather than a fragile in-place patch). The integer Id field on the
/// view-model is a runtime convenience, not stored — view-model
/// hydration reassigns ids 0..N-1 from the position in the list.
/// </para>
///
/// <para>
/// <b>Colours are not persisted.</b> See <see cref="BlockData"/> for
/// the full rationale. The track's accent and tinted block background
/// come from the runtime palette in <c>TrackListViewModel</c>, picked
/// at the position the track ends up in. Storing them would lock a
/// freshly-opened project to whatever theme was active when it was
/// last saved — a worse outcome than letting the palette re-stripe
/// the timeline cleanly on every open.
/// </para>
///
/// <para>
/// <b>Blocks live here, not in a top-level list.</b> Track→blocks is a
/// strict containment relationship (a block always belongs to exactly
/// one track), so flattening into a top-level <c>Blocks</c> list with
/// a foreign key would invent a join the data doesn't actually need.
/// Embedded keeps the manifest's logical shape lined up with the UI's
/// (track row contains its blocks) and makes the JSON / MessagePack
/// dump readable at a glance for debugging.
/// </para>
/// </summary>
[MessagePackObject]
public sealed class TrackData
{
    /// <summary>
    /// Display name and primary identity (e.g. "Narrator", "Lyra").
    /// Mutable across saves — a rename rewrites the track's entry in
    /// place; block IDs that referenced the old name are stale until
    /// the next manifest write recomputes them, but block IDs are
    /// only used as transient handles between an action and its
    /// caller, never persisted.
    /// </summary>
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Position in the track list, 0..N-1. Persisted because the
    /// MessagePack list's order is the on-disk order; redundancy
    /// here lets the manifest survive a hand-edit that reorders
    /// entries without renumbering, and lines up with the
    /// <c>byte Order</c> property the view-model carries.
    /// </summary>
    [Key(1)]
    public byte Order { get; set; }

    /// <summary>
    /// Blocks on this track, in time order. The list itself owns
    /// the blocks — there is no top-level block table.
    /// </summary>
    [Key(2)]
    public List<BlockData> Blocks { get; set; } = new();
}
