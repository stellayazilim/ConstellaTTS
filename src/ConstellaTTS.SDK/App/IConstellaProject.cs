namespace ConstellaTTS.SDK.App;

/// <summary>
/// Represents an open project in the application. Single-document
/// model — at most one instance is alive at a time, owned by
/// <see cref="Projects.IProjectManager"/>. Carries identity plus the
/// list of external sample libraries the project pulls into its
/// catalogue.
///
/// <para>
/// <b>Live document, not snapshot.</b> The project mutates as the
/// user works. Library references are added and removed through
/// <see cref="AddSampleLibrary"/> / <see cref="RemoveSampleLibrary"/>;
/// future edits will touch other fields. <see cref="Projects.IProjectManager.SaveAsync"/>
/// reads the current state and writes it to disk; there is no
/// separate "dirty" flag because saves are explicit (and cheap
/// enough that triggering one after every relevant mutation is
/// fine).
/// </para>
///
/// <para>
/// <b>The sample list is the filesystem.</b> The project intentionally
/// does not carry a list of sample filenames. The <c>samples/</c>
/// directory and any referenced library's <c>samples/</c> directory
/// are the source of truth; <see cref="Audio.ISampleService"/> scans
/// them, the catalogue is whatever's there. Storing a parallel list
/// in the manifest would only create a synchronisation burden the
/// filesystem already handles correctly.
/// </para>
/// </summary>
public interface IConstellaProject
{
    /// <summary>Absolute path to the project's root folder on disk.</summary>
    string Path { get; init; }

    /// <summary>
    /// Display name of the project. Currently sourced from the
    /// project folder's leaf name; a future iteration may decouple
    /// the on-disk folder name from a user-editable display name
    /// stored in the manifest.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Absolute path to the project's <c>samples/</c> subdirectory —
    /// where <see cref="Audio.ISampleProcessor"/>-style ingestion
    /// writes its WAV output, where <see cref="Audio.ISampleService"/>
    /// enumerates the live catalogue, and where the engine looks
    /// them up at runtime.
    ///
    /// <para>
    /// Centralised here rather than computed at every call site for
    /// two reasons: it keeps the layout convention ("samples live
    /// under <c>&lt;project&gt;/samples</c>") in one place, and it
    /// makes it trivial to evolve later — if the layout ever needs
    /// to gain per-language or per-character subdirectories, a single
    /// override on the project type rewires everyone.
    /// </para>
    ///
    /// <para>
    /// The directory is not created here. Callers that write into it
    /// are responsible for calling <c>Directory.CreateDirectory</c>;
    /// callers that only read tolerate absence by treating it as an
    /// empty catalogue.
    /// </para>
    /// </summary>
    string SamplesPath => System.IO.Path.Combine(Path, "samples");

    /// <summary>
    /// Absolute path to the project's <c>tmp/</c> subdirectory —
    /// scratch space for in-progress artefacts that haven't been
    /// committed to the project yet (active microphone recordings
    /// before the user names them, partial uploads, anything else
    /// that should disappear if the app crashes mid-operation).
    ///
    /// <para>
    /// Living under the project root rather than the OS temp
    /// directory keeps the user's system clean (a deleted project
    /// takes its scratch with it) and surfaces in-progress files in
    /// the user's familiar file manager view if they ever want to
    /// poke around. Application startup is responsible for clearing
    /// stale entries here — anything left behind from a previous
    /// crashed session was, by definition, never confirmed by the
    /// user.
    /// </para>
    ///
    /// <para>
    /// Like <see cref="SamplesPath"/>, the directory itself is not
    /// created here; callers that write into it call
    /// <c>Directory.CreateDirectory</c> on demand.
    /// </para>
    /// </summary>
    string TmpPath => System.IO.Path.Combine(Path, "tmp");

    /// <summary>
    /// Absolute paths to other <c>project.ctts</c> files whose
    /// sample directories should be pulled into this project's
    /// catalogue alongside its own <c>samples/</c>. The project's
    /// own samples are always implicit — the library list is the
    /// <i>extra</i> projects to merge in.
    ///
    /// <para>
    /// Read-only on the surface; mutation goes through
    /// <see cref="AddSampleLibrary"/> / <see cref="RemoveSampleLibrary"/>
    /// so future change notifications and validation can sit on the
    /// project rather than every caller.
    /// </para>
    /// </summary>
    IReadOnlyList<string> SampleLibraries { get; }

    /// <summary>
    /// Adds a path to a sibling <c>project.ctts</c> file as a
    /// sample library reference. Idempotent: a path already present
    /// is silently ignored. Match is ordinal-ignore-case to align
    /// with Windows filesystem semantics.
    /// </summary>
    void AddSampleLibrary(string projectFilePath);

    /// <summary>
    /// Removes a library reference. No-op if the path isn't present.
    /// Does not delete the referenced project; only this project's
    /// link to it.
    /// </summary>
    void RemoveSampleLibrary(string projectFilePath);

    // --- Tracks & blocks --------------------------------------------

    /// <summary>
    /// Tracks placed on the timeline, in display order. Read-only on
    /// the surface; mutation goes through the dedicated track and
    /// block methods below so a single point of authority can drive
    /// change notifications and keep <see cref="TrackData.Order"/>
    /// values contiguous.
    ///
    /// <para>
    /// <b>Element identity is by reference.</b> Callers that want to
    /// mutate a single block in-place do so by holding the
    /// <see cref="TrackData"/> / <see cref="BlockData"/> instance
    /// they got from this list and editing its fields; the project
    /// is the same object both before and after, so the next save
    /// picks up the change automatically. <see cref="UpdateBlock"/>
    /// is for the case where the caller has a fresh
    /// <see cref="BlockData"/> built elsewhere and wants to swap it
    /// in atomically.
    /// </para>
    /// </summary>
    IReadOnlyList<TrackData> Tracks { get; }

    /// <summary>
    /// Append a track to the end of the list. The track's
    /// <see cref="TrackData.Order"/> is overwritten to match its new
    /// position so the contiguous 0..N-1 invariant holds; whatever
    /// the caller passed in is ignored. Names are not deduplicated
    /// here — the caller (typically the Add Track dialog) is
    /// responsible for ensuring uniqueness, since name is the
    /// primary identifier and a duplicate would break block ID
    /// resolution.
    /// </summary>
    void AddTrack(TrackData track);

    /// <summary>
    /// Insert a track at the given index. Same rules as
    /// <see cref="AddTrack"/> regarding <see cref="TrackData.Order"/>
    /// (overwritten to match position) and name uniqueness (caller's
    /// responsibility), but the track lands at
    /// <paramref name="index"/> and the rest of the list shifts
    /// right to make room. Out-of-range indices are clamped against
    /// the current list size: a negative index becomes 0, an index
    /// past the end becomes the end (equivalent to <see cref="AddTrack"/>).
    ///
    /// <para>
    /// <b>Why a separate method.</b> The user-facing add path is
    /// always an append (the toolbar's "+ Track" button puts new
    /// rows at the bottom, matching the read-order of every
    /// timeline-style UI). Insertion-at-index is the undo-of-remove
    /// path: the user removed a row from the middle of the list
    /// and wants Ctrl+Z to put it back in the same slot, not at
    /// the end. Splitting the two methods keeps the common case
    /// readable and lets the rare case carry its own contract.
    /// </para>
    /// </summary>
    void InsertTrack(int index, TrackData track);

    /// <summary>
    /// Remove a track by name. Drops the track and every block on
    /// it; remaining tracks are renumbered so <see cref="TrackData.Order"/>
    /// stays contiguous. No-op if no track with the given name
    /// exists. Match is ordinal-case-sensitive — names are
    /// user-typed identifiers, not paths, so a Lyra/lyra mix-up
    /// should surface as a no-op rather than a silent merge.
    /// </summary>
    void RemoveTrack(string trackName);

    /// <summary>
    /// Rename a track. Updates the entry in place; block IDs that
    /// referenced the old name become stale (block IDs are derived
    /// from <c>{trackName}/{blockIndex}</c> and computed at use
    /// time, never persisted, so the staleness only matters for
    /// in-flight handles a caller may be holding mid-operation).
    /// No-op if the old name doesn't resolve to a track or if the
    /// new name already belongs to a different track — collision
    /// would require a merge the project can't decide on its own.
    /// </summary>
    void RenameTrack(string oldName, string newName);

    /// <summary>
    /// Move the track at index <paramref name="fromIndex"/> to index
    /// <paramref name="toIndex"/>; renumbers <see cref="TrackData.Order"/>
    /// across the list so the contiguous 0..N-1 invariant holds.
    /// Indices are clamped against the current list size; an
    /// out-of-range pair is a no-op rather than an exception, to
    /// match the equally-tolerant view-model <c>Reorder</c>.
    /// </summary>
    void ReorderTracks(int fromIndex, int toIndex);

    /// <summary>
    /// Append a block to the named track. The block is added at the
    /// end of the track's <see cref="TrackData.Blocks"/> list. The
    /// block ID for subsequent operations is
    /// <c>{trackName}/{newIndex}</c> where <c>newIndex</c> is the
    /// position the block lands at. No-op if the track doesn't exist;
    /// the caller (an action) is expected to have just created or
    /// looked up the track.
    /// </summary>
    void AddBlock(string trackName, BlockData block);

    /// <summary>
    /// Remove a block by composite ID. <paramref name="blockId"/> is
    /// <c>{trackName}/{blockIndex}</c> as produced by the rest of the
    /// system; the method parses it, locates the track, and removes
    /// the block at that index. Subsequent blocks on the same track
    /// shift down by one position — their indices change, so any
    /// IDs the caller is holding for this track are stale after this
    /// call. No-op on malformed IDs, missing tracks, or
    /// out-of-range indices.
    /// </summary>
    void RemoveBlock(string blockId);

    /// <summary>
    /// Replace a block's data atomically. <paramref name="blockId"/>
    /// is <c>{trackName}/{blockIndex}</c>; the block at that
    /// position is overwritten with <paramref name="updated"/>. The
    /// block's position in the list is preserved — indices stay
    /// stable, IDs stay valid. Used when a caller has a fresh
    /// <see cref="BlockData"/> built elsewhere (e.g. an action
    /// rebuilding the row from view-model state). For incremental
    /// edits on a single field, callers can also mutate the
    /// existing instance in place; both shapes work, this one is
    /// the atomic-swap option.
    /// </summary>
    void UpdateBlock(string blockId, BlockData updated);
}
