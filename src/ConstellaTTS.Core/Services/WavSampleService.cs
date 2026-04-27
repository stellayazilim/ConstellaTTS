using System.Collections.ObjectModel;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Audio;
using ConstellaTTS.SDK.Audio.Wav;
using ConstellaTTS.SDK.Projects;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// File-system-backed <see cref="ISampleService"/>. Holds the live
/// in-memory list of samples for the active project, hydrates from
/// disk when the active project changes or a refresh is requested,
/// and accepts incremental updates from importers between scans.
///
/// <para>
/// <b>WAV-only by design.</b> The samples directories scanned only
/// ever hold files this application's pipeline wrote (16-bit PCM
/// WAV at the moment). A non-WAV file there would be foreign —
/// either a manual drop the user shouldn't be making or a future
/// format we haven't taught the service to read — so each scan
/// filters strictly on <c>*.wav</c> and skips anything else without
/// complaining.
/// </para>
///
/// <para>
/// <b>Catalogue covers the active project + its libraries.</b>
/// Every refresh walks the project's own <c>samples/</c> directory
/// plus, for each entry in <c>SampleLibraries</c>, the referenced
/// project's <c>samples/</c> directory. The library entries are
/// absolute paths to <c>project.ctts</c> files; the directory to
/// scan is the parent of that path. Resolution is flat — a
/// referenced library's own <c>SampleLibraries</c> aren't followed,
/// so the user gets exactly the set they asked for.
/// </para>
///
/// <para>
/// <b>Hybrid refresh.</b> Project switches and the public
/// <see cref="Refresh"/> trigger a full scan; the upload pipeline
/// uses <see cref="OnSamplesAdded"/> for incremental updates so
/// every import doesn't pay the rescan cost. <see cref="Refresh"/>
/// is also the right answer when the user drops a WAV into the
/// folder via the file manager — the service can't observe that
/// directly, but a manual rescan picks it up.
/// </para>
///
/// <para>
/// <b>Header reads are best-effort.</b> A corrupt or partially-
/// written WAV throws inside <see cref="WavStreamReader"/>'s parse;
/// we catch, log, and skip the file. The user sees a slightly
/// shorter list rather than a crashed panel — and the broken file
/// stays on disk for them to inspect. Read errors are logged at
/// warning level so they're visible without drowning the log.
/// </para>
/// </summary>
public sealed class WavSampleService : ISampleService
{
    private readonly IProjectManager           _projectManager;
    private readonly ILogger<WavSampleService> _log;

    private readonly ObservableCollection<Sample> _samples = new();
    public IReadOnlyList<Sample> Samples => _samples;

    public event EventHandler<SampleEventArgs>? SampleAdded;
    public event EventHandler<SampleEventArgs>? SampleRemoved;

    public WavSampleService(
        IProjectManager           projectManager,
        ILogger<WavSampleService> log)
    {
        _projectManager = projectManager;
        _log            = log;

        // Initial state: an active project may already exist by the
        // time this service is constructed (the launcher → DAW path
        // sets it before the DAW window's view-models resolve), so
        // populate from it now rather than waiting for the next
        // ActiveChanged event that will never come.
        _projectManager.ActiveChanged += OnActiveChanged;
        Refresh();
    }

    private void OnActiveChanged(object? sender, IConstellaProject? project) => Refresh();

    public void Refresh()
    {
        // Snapshot for the removal pass — Clear() would wipe the
        // collection before subscribers got to see what's leaving.
        var leaving = _samples.ToArray();
        _samples.Clear();
        foreach (var s in leaving)
            SampleRemoved?.Invoke(this, new SampleEventArgs(s));

        var project = _projectManager.Active;
        if (project is null) return;

        // Own samples first, then each referenced library's. Order
        // matters only for the initial visual: the project's own
        // samples land at the top of the list, libraries underneath.
        ScanDirectory(project.SamplesPath);

        foreach (var libraryProjectFile in project.SampleLibraries)
        {
            var libraryDir = ResolveLibrarySamplesDirectory(libraryProjectFile);
            if (libraryDir is null) continue;
            ScanDirectory(libraryDir);
        }
    }

    public void OnSamplesAdded(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return;

        foreach (var path in paths)
        {
            // Importers report paths that should already live under
            // the active project's samples directory. We don't enforce
            // that — and don't want to silently reject if it's
            // slightly off — but we do skip anything that isn't on
            // disk by the time we see it; the next full Refresh would
            // reconcile if needed.
            if (!File.Exists(path))
            {
                _log.LogDebug(
                    "OnSamplesAdded: {Path} not found on disk; skipping.", path);
                continue;
            }

            // Guard against duplicate notifications (e.g. an importer
            // re-emitting after a retry). Ordinal-ignore-case matches
            // Windows filesystem semantics; on case-sensitive hosts
            // the file system itself would already have rejected the
            // duplicate write.
            if (AlreadyTracked(path)) continue;

            AppendFromPath(path);
        }
    }

    /// <summary>
    /// Walks one directory and appends every WAV it finds. Tolerates
    /// missing or empty directories silently — both are valid states
    /// (a fresh project has an empty <c>samples/</c>; a referenced
    /// library may have moved). The catalogue stays consistent
    /// either way.
    /// </summary>
    private void ScanDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        // EnumerateFiles enumerates lazily, so for very large
        // directories we don't pay for the full listing up front.
        // The order is filesystem-dependent; the UI sorts on its
        // own (by name today, by last-imported eventually).
        foreach (var path in Directory.EnumerateFiles(dir, "*.wav", SearchOption.TopDirectoryOnly))
        {
            if (AlreadyTracked(path)) continue;
            AppendFromPath(path);
        }
    }

    /// <summary>
    /// Maps a library entry (an absolute path to another project's
    /// <c>project.ctts</c>) to the samples directory we should
    /// actually scan: the sibling <c>samples/</c> next to the .ctts
    /// file. Returns <c>null</c> for malformed entries so the
    /// caller can skip without ceremony.
    /// </summary>
    private string? ResolveLibrarySamplesDirectory(string libraryProjectFile)
    {
        if (string.IsNullOrWhiteSpace(libraryProjectFile)) return null;

        var libraryRoot = Path.GetDirectoryName(libraryProjectFile);
        if (string.IsNullOrEmpty(libraryRoot))
        {
            _log.LogDebug(
                "Library path has no parent directory; skipping: {Path}",
                libraryProjectFile);
            return null;
        }

        return Path.Combine(libraryRoot, "samples");
    }

    private bool AlreadyTracked(string path)
    {
        foreach (var s in _samples)
            if (string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Reads a sample's header, builds the <see cref="Sample"/>
    /// record, appends it to the live collection, and fires
    /// <see cref="SampleAdded"/>. Used by both the full-scan and
    /// incremental paths so they stay consistent on validation,
    /// notification order, and skip behaviour.
    /// </summary>
    private void AppendFromPath(string path)
    {
        if (!TryRead(path, out var sample)) return;
        _samples.Add(sample);
        SampleAdded?.Invoke(this, new SampleEventArgs(sample));
    }

    /// <summary>
    /// Opens the file just long enough to read its header, then
    /// closes. <see cref="WavStreamReader"/> seeks past the data
    /// chunk during construction so we get format facts cheap; we
    /// dispose immediately to avoid holding a handle on a file the
    /// user might want to delete or rename from outside.
    /// </summary>
    private bool TryRead(string path, out Sample sample)
    {
        sample = null!;
        try
        {
            using var reader = new WavStreamReader(path);
            var fmt           = reader.Format;
            var bytes         = new FileInfo(path).Length;

            // Duration is total PCM bytes / (sample rate × channels ×
            // bytes-per-sample). 16-bit is hard-coded here because
            // that's what our writer produces; if the catalogue ever
            // contains files written by something else, we'll switch
            // to reading the bit depth off the reader.
            const int bytesPerSample = 2;
            var dataBytes = Math.Max(0, bytes - 44); // RIFF header is 44 bytes for our writer
            var frames    = dataBytes / (fmt.Channels * bytesPerSample);
            var duration  = (double)frames / fmt.SampleRate;

            sample = new Sample(
                Path:         path,
                OriginalName: System.IO.Path.GetFileNameWithoutExtension(path),
                DurationSec:  duration,
                SampleRate:   fmt.SampleRate,
                Channels:     fmt.Channels);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read WAV header for {Path}; skipping.", path);
            return false;
        }
    }
}
