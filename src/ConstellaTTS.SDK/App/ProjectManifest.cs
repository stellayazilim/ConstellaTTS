using MessagePack;

namespace ConstellaTTS.SDK.App;

/// <summary>
/// On-disk shape of a project's <c>project.ctts</c> file. POCO with
/// MessagePack attributes; <see cref="IConstellaProject"/> is the
/// in-memory live object that mirrors it. The two are kept distinct
/// so the live project can carry runtime-only concerns (event hooks,
/// derived properties, mutating methods) without bleeding them into
/// the persistence schema.
///
/// <para>
/// <b>Schema evolution rule.</b> Each member has a stable integer
/// <see cref="KeyAttribute"/>. New fields take a fresh number; old
/// numbers stay reserved even if the field is dropped, so a future
/// reader of an older manifest still maps the right bytes to the
/// right slot. Renaming a property is free as long as the key
/// number doesn't change. <see cref="Version"/> is bumped only when
/// the change is incompatible enough that older readers genuinely
/// can't make sense of the file (which integer-keyed MessagePack
/// makes rare).
/// </para>
///
/// <para>
/// <b>The samples directory is not enumerated here.</b> Earlier
/// iterations stored a flat list of sample filenames in the
/// manifest; that turned the file into a duplicate of the directory
/// listing, with all the synchronisation grief that implies (drag a
/// file in, manifest is wrong; delete a file, manifest is wrong).
/// The filesystem is the catalogue. The manifest only records
/// references to <i>other</i> projects whose sample directories
/// should be folded into this one's view.
/// </para>
/// </summary>
[MessagePackObject]
public sealed class ProjectManifest
{
    /// <summary>
    /// Schema version. Bumped only on backward-incompatible changes
    /// — adding fields with new <see cref="KeyAttribute"/> numbers
    /// doesn't count, integer keys handle that gracefully.
    /// </summary>
    [Key(0)]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Display name. Currently mirrors the project folder's leaf
    /// name; persisted here so a future iteration can let the user
    /// rename the project without renaming the folder.
    /// </summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute paths to sibling <c>project.ctts</c> files whose
    /// sample directories are pulled into this project's catalogue
    /// in addition to its own <c>samples/</c>. Each entry points at
    /// another project (a shared voice library, a character pack,
    /// an asset bundle); on load, the sample service scans every
    /// referenced project's <c>samples/</c> in turn and merges the
    /// results.
    ///
    /// <para>
    /// <b>Why absolute.</b> Library projects can live anywhere on
    /// the user's machine — a shared <c>G:\\voices\\lyra</c> shared
    /// across several games, an external SSD with a guest's voice
    /// pack, anywhere. Relative paths would only survive if both
    /// the host project and the library moved together, which is
    /// exactly the scenario libraries are designed not to require.
    /// A future "relink" UI can patch up broken absolute references
    /// after a reorganisation.
    /// </para>
    ///
    /// <para>
    /// <b>Flat resolution.</b> Only the directly-listed projects are
    /// scanned; their own <c>SampleLibraries</c> entries are not
    /// followed. Transitive resolution would need cycle detection
    /// and adds a discoverability problem (samples appearing the
    /// user doesn't remember pulling in); the user explicitly lists
    /// the libraries they want and gets exactly those.
    /// </para>
    /// </summary>
    [Key(2)]
    public List<string> SampleLibraries { get; set; } = new();

    /// <summary>
    /// Tracks placed on the timeline, in display order. Each track
    /// owns its blocks (stages and sections) directly through
    /// <see cref="TrackData.Blocks"/>; the manifest doesn't carry a
    /// top-level block table.
    ///
    /// <para>
    /// <b>Why embedded rather than a flat block table.</b> The
    /// track→blocks relationship is strict containment — a block
    /// always belongs to exactly one track and never moves between
    /// tracks without being conceptually re-created. A flat table
    /// with a track foreign key would invent a join the data doesn't
    /// need, and would split a single drag-edit into two writes
    /// (track entry plus block entry) for no gain. The embedded shape
    /// also matches the UI's mental model: the track row contains its
    /// blocks visually; the on-disk shape mirrors that.
    /// </para>
    ///
    /// <para>
    /// <b>List order is the on-disk order.</b> <see cref="TrackData.Order"/>
    /// is persisted alongside as a redundancy — useful if a hand-edit
    /// reorders the list without renumbering, and aligned with the
    /// view-model's <c>byte Order</c> property — but the canonical
    /// truth is the list position. The view-model rebuilds from the
    /// list and reassigns ids 0..N-1 on hydration.
    /// </para>
    /// </summary>
    [Key(3)]
    public List<TrackData> Tracks { get; set; } = new();
}
