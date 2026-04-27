using ConstellaTTS.SDK.Audio;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Imports one or more source audio files into the active project's
/// <c>samples/</c> directory, transcoding each to 16-bit PCM WAV
/// through <see cref="ISampleProcessor"/>. Replaces the older
/// tmp-staging upload — files now land directly in the project
/// folder, so there's no second copy step and no tmp cleanup
/// concern.
///
/// <para>
/// <b>Payload.</b> The data argument is an
/// <see cref="IReadOnlyList{T}"/> of absolute source paths. The
/// view-model collects them from the file picker / drag-drop /
/// clipboard sources and hands the list over as a single execute
/// call; this action processes them sequentially. Anything else
/// (including <c>null</c>) is rejected — actions don't try to be
/// helpful with malformed payloads, the call site knows what it
/// passed.
/// </para>
///
/// <para>
/// <b>Name collisions.</b> Output filename is the source's stem
/// with a <c>.wav</c> extension. If the target already exists, a
/// numeric suffix is appended (<c>foo.wav</c>, <c>foo (2).wav</c>,
/// <c>foo (3).wav</c>, …) the same way Windows Explorer disambiguates
/// — overwriting silently would lose data, hard-failing on the
/// first collision would make batch imports painful.
/// </para>
///
/// <para>
/// <b>Two completion signals.</b> The base
/// <c>AsyncExecutionCompleted</c> fires for the success / failure
/// question — a status bar binds to that. <see cref="SamplesUploaded"/>
/// fires only on success, carrying the exact paths the action
/// wrote to disk; the sample service binds to that and folds them
/// into its in-memory catalogue without a directory rescan.
/// </para>
/// </summary>
public sealed class SampleUploadAction : AsyncActionBase, ISampleUploadAction
{
    private readonly ISampleProcessor _processor;
    private readonly IProjectManager  _projectManager;

    public override string  Id          => "SampleUpload";
    public override string  Name        => "Sample Yükle";
    public override string? Description => "Seçilen ses dosyalarını projenin sample klasörüne kaydeder.";

    public event EventHandler<SamplesUploadedEventArgs>? SamplesUploaded;

    public SampleUploadAction(
        ISampleProcessor processor,
        IProjectManager  projectManager)
    {
        _processor      = processor;
        _projectManager = projectManager;
    }

    public override async Task ExecuteAsync(object? data = null)
    {
        if (data is not IReadOnlyList<string> paths)
            throw new ArgumentException(
                $"{nameof(SampleUploadAction)} expects an IReadOnlyList<string> payload of source paths.",
                nameof(data));

        if (paths.Count == 0) return;

        var project = _projectManager.Active
            ?? throw new InvalidOperationException(
                "No active project; cannot import samples.");

        var samplesDir = project.SamplesPath;
        Directory.CreateDirectory(samplesDir);

        // Track the resolved output paths as we go and publish them
        // in one batch when the loop finishes successfully. Building
        // the list incrementally is the only way to know what got
        // written — the processor's per-file ProcessFinished event
        // could be tapped instead, but routing it through this action
        // keeps the wiring at the action level (one publisher, one
        // subscriber) instead of forcing every consumer to filter the
        // processor's wider event for its own batch.
        var written = new List<string>(paths.Count);

        // Sequential rather than parallel: the decoder + encoder are
        // each CPU-bound, and running N transcodes at once would just
        // contend on the same threadpool workers without speeding the
        // batch up meaningfully. Sequential also keeps the collision-
        // resolution logic correct — two parallel imports of the same
        // base name would race past ResolveOutputPath and stomp each
        // other.
        foreach (var source in paths)
        {
            var output = ResolveOutputPath(samplesDir, source);
            await _processor.ProcessAsync(source, output).ConfigureAwait(false);
            written.Add(output);
        }

        // Only fire on full-batch success. A partial-batch story
        // (publish what survived, surface what didn't) is a future
        // refinement that would change the contract of this event;
        // until then, callers that need that granularity should
        // observe the processor's per-file events directly.
        SamplesUploaded?.Invoke(this, new SamplesUploadedEventArgs(written));
    }

    /// <summary>
    /// Picks a non-colliding target path under <paramref name="samplesDir"/>
    /// for a source file. Tries the bare stem first, then
    /// <c>name (2).wav</c>, <c>name (3).wav</c>, and so on. The
    /// counter is bounded by a sanity ceiling to avoid spinning if
    /// the directory is in some pathological state.
    /// </summary>
    private static string ResolveOutputPath(string samplesDir, string sourcePath)
    {
        var stem   = Path.GetFileNameWithoutExtension(sourcePath);
        var direct = Path.Combine(samplesDir, $"{stem}.wav");
        if (!File.Exists(direct)) return direct;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(samplesDir, $"{stem} ({i}).wav");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException(
            $"Could not find an unused name for '{stem}.wav' in {samplesDir}.");
    }
}
