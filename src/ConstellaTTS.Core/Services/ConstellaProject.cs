using ConstellaTTS.SDK.App;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Default in-memory <see cref="IConstellaProject"/>. Identity is the
/// project's folder path on disk; the display name is derived from
/// the path's leaf segment so the on-disk folder and the in-app name
/// can never drift apart. Sample-library references and the live
/// track collection are mutable through the dedicated methods on
/// the interface.
///
/// <para>
/// <b>No save plumbing here.</b> The project doesn't write its own
/// manifest — that's <see cref="ProjectManager.SaveAsync"/>'s job.
/// <see cref="SDK.UI.Actions.IPersistable"/> actions reach the
/// project through the manager (their <c>Persist</c> method takes
/// an <see cref="SDK.Projects.IProjectManager"/>) and the call site
/// orchestrates flush timing; the project itself is just the data.
/// </para>
/// </summary>
internal sealed class ConstellaProject : IConstellaProject
{
    private readonly List<string>    _sampleLibraries = new();
    private readonly List<TrackData> _tracks          = new();

    public required string Path { get; init; }

    /// <summary>
    /// Project name = leaf segment of <see cref="Path"/>. Trims
    /// trailing separators so a path like <c>D:\Stories\YildizYolcusu\</c>
    /// still yields <c>YildizYolcusu</c>.
    /// </summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(
        System.IO.Path.DirectorySeparatorChar,
        System.IO.Path.AltDirectorySeparatorChar));

    // SamplesPath / TmpPath are mirrored from the interface's default
    // implementations rather than left implicit because the manager
    // accesses them through the concrete <c>ConstellaProject</c>
    // type (it needs <c>ReplaceSampleLibraries</c>, which is
    // <c>internal</c> on the impl). Default interface members are
    // only visible through the interface type — mirroring the
    // getters here lets the manager avoid casting on every access
    // and keeps the layout convention in one obvious place.
    public string SamplesPath => System.IO.Path.Combine(Path, "samples");
    public string TmpPath     => System.IO.Path.Combine(Path, "tmp");

    public IReadOnlyList<string>    SampleLibraries => _sampleLibraries;
    public IReadOnlyList<TrackData> Tracks          => _tracks;

    // --- Sample libraries -------------------------------------------

    public void AddSampleLibrary(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath)) return;

        // Idempotent: caller doesn't have to pre-check. Ordinal-
        // ignore-case lines up with Windows filesystem semantics; a
        // case-sensitive host would already have rejected the
        // duplicate as a separate path.
        foreach (var existing in _sampleLibraries)
        {
            if (string.Equals(existing, projectFilePath, StringComparison.OrdinalIgnoreCase))
                return;
        }
        _sampleLibraries.Add(projectFilePath);
    }

    public void RemoveSampleLibrary(string projectFilePath)
    {
        for (int i = 0; i < _sampleLibraries.Count; i++)
        {
            if (string.Equals(_sampleLibraries[i], projectFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _sampleLibraries.RemoveAt(i);
                return;
            }
        }
    }

    // --- Tracks ------------------------------------------------------

    public void AddTrack(TrackData track)
    {
        if (track is null) return;

        // Caller-provided Order is overwritten to match the actual
        // append position. Keeps the contiguous 0..N-1 invariant the
        // interface promises, regardless of what the caller filled in.
        track.Order = (byte)_tracks.Count;
        _tracks.Add(track);
    }

    public void InsertTrack(int index, TrackData track)
    {
        if (track is null) return;

        // Clamp rather than throw — matches the interface contract
        // and keeps the behaviour aligned with ReorderTracks's
        // permissive bounds policy. A negative or past-the-end
        // index lands at the nearest valid edge.
        if (index < 0)            index = 0;
        if (index > _tracks.Count) index = _tracks.Count;

        _tracks.Insert(index, track);
        RenumberTrackOrder();
    }

    public void RemoveTrack(string trackName)
    {
        var idx = IndexOfTrack(trackName);
        if (idx < 0) return;

        _tracks.RemoveAt(idx);
        RenumberTrackOrder();
    }

    public void RenameTrack(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(newName)) return;

        var idx = IndexOfTrack(oldName);
        if (idx < 0) return;

        // Reject collision with a different track. A no-op is the
        // safest answer the data layer can give — merging two tracks
        // into one is a destructive operation that needs a UI
        // confirmation, not a silent rename.
        for (int i = 0; i < _tracks.Count; i++)
        {
            if (i == idx) continue;
            if (string.Equals(_tracks[i].Name, newName, StringComparison.Ordinal))
                return;
        }

        _tracks[idx].Name = newName;
    }

    public void ReorderTracks(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= _tracks.Count) return;
        if (toIndex   < 0 || toIndex   >= _tracks.Count) return;

        var t = _tracks[fromIndex];
        _tracks.RemoveAt(fromIndex);
        _tracks.Insert(toIndex, t);
        RenumberTrackOrder();
    }

    // --- Blocks ------------------------------------------------------

    public void AddBlock(string trackName, BlockData block)
    {
        if (block is null) return;

        var idx = IndexOfTrack(trackName);
        if (idx < 0) return;

        _tracks[idx].Blocks.Add(block);
    }

    public void RemoveBlock(string blockId)
    {
        if (!TryParseBlockId(blockId, out var trackName, out var blockIndex))
            return;

        var trackIdx = IndexOfTrack(trackName);
        if (trackIdx < 0) return;

        var blocks = _tracks[trackIdx].Blocks;
        if (blockIndex < 0 || blockIndex >= blocks.Count) return;

        blocks.RemoveAt(blockIndex);
    }

    public void UpdateBlock(string blockId, BlockData updated)
    {
        if (updated is null) return;
        if (!TryParseBlockId(blockId, out var trackName, out var blockIndex))
            return;

        var trackIdx = IndexOfTrack(trackName);
        if (trackIdx < 0) return;

        var blocks = _tracks[trackIdx].Blocks;
        if (blockIndex < 0 || blockIndex >= blocks.Count) return;

        blocks[blockIndex] = updated;
    }

    // --- Internal hydration hooks -----------------------------------

    /// <summary>
    /// Convenience hook used by <see cref="ProjectManager"/> during
    /// open: replaces the in-memory library list with the one that
    /// was just deserialized from the manifest. Internal because
    /// it's not part of the public mutating surface — outside callers
    /// go through <see cref="AddSampleLibrary"/> /
    /// <see cref="RemoveSampleLibrary"/>.
    /// </summary>
    internal void ReplaceSampleLibraries(IEnumerable<string> libraries)
    {
        _sampleLibraries.Clear();
        _sampleLibraries.AddRange(libraries);
    }

    /// <summary>
    /// Sister hook to <see cref="ReplaceSampleLibraries"/>: replaces
    /// the in-memory track list with one deserialized from the
    /// manifest. The incoming entries are taken as-is; their
    /// <see cref="TrackData.Order"/> values are renumbered to match
    /// list position so a malformed manifest (gaps, duplicates) ends
    /// up consistent in memory regardless.
    /// </summary>
    internal void ReplaceTracks(IEnumerable<TrackData> tracks)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        RenumberTrackOrder();
    }

    // --- Helpers ----------------------------------------------------

    /// <summary>
    /// Locate a track by name. Returns -1 when no match. Match is
    /// ordinal-case-sensitive — see the interface contract for the
    /// rationale.
    /// </summary>
    private int IndexOfTrack(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (int i = 0; i < _tracks.Count; i++)
        {
            if (string.Equals(_tracks[i].Name, name, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Restamp <see cref="TrackData.Order"/> across the list so it
    /// matches list position. Called after every reorder / remove /
    /// hydrate to keep the contiguous 0..N-1 invariant.
    /// </summary>
    private void RenumberTrackOrder()
    {
        for (int i = 0; i < _tracks.Count; i++)
            _tracks[i].Order = (byte)i;
    }

    /// <summary>
    /// Parse a <c>{trackName}/{blockIndex}</c> ID. Returns false on
    /// any malformed input (no slash, missing parts, non-integer
    /// index, negative index). The track name is allowed to contain
    /// further slashes — only the LAST slash is treated as the
    /// separator, so a hypothetical track named "Foo/Bar" still
    /// resolves correctly. (Not currently relied on by the UI, but
    /// the rule is the only consistent one once names are
    /// user-typed.)
    /// </summary>
    private static bool TryParseBlockId(string blockId, out string trackName, out int blockIndex)
    {
        trackName  = string.Empty;
        blockIndex = -1;

        if (string.IsNullOrEmpty(blockId)) return false;

        var sep = blockId.LastIndexOf('/');
        if (sep <= 0 || sep >= blockId.Length - 1) return false;

        var indexPart = blockId.AsSpan(sep + 1);
        if (!int.TryParse(indexPart, out var idx) || idx < 0) return false;

        trackName  = blockId.Substring(0, sep);
        blockIndex = idx;
        return true;
    }
}
