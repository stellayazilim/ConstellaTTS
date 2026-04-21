using System.Collections.Concurrent;

namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Manages active BufferStreamer instances per generation job.
/// Acts as the index — raw audio data lives in BufferStreamer.
/// IPCService is the only writer. Players and ViewModels read via GetStreamer().
/// </summary>
public sealed class SoundService : ISoundService
{
    private readonly ConcurrentDictionary<string, BufferStreamer> _streamers = new();
    private readonly string _tempDir;
    private readonly string _outputDir;

    public SoundService(string tempDir, string outputDir)
    {
        _tempDir   = tempDir;
        _outputDir = outputDir;

        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outputDir);
    }

    public BufferStreamer StartJob(string jobId)
    {
        var outputPath = Path.Combine(_outputDir, $"{jobId}.opus");
        var streamer   = new BufferStreamer(jobId, _tempDir, outputPath);

        _streamers[jobId] = streamer;

        // Remove from index when finalized — discard the out value explicitly
        streamer.Finalized += _ =>
        {
            _streamers.TryRemove(jobId, out var removed);
            removed?.Dispose();
        };

        JobStarted?.Invoke(jobId, streamer);
        return streamer;
    }

    public void Append(string jobId, byte[] pcm, bool final)
    {
        if (_streamers.TryGetValue(jobId, out var streamer))
            streamer.Append(pcm, final);
    }

    public BufferStreamer? GetStreamer(string jobId) =>
        _streamers.GetValueOrDefault(jobId);

    public event Action<string, BufferStreamer>? JobStarted;
}
