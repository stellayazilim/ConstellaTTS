namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Manages active BufferStreamer instances per generation job.
/// IPCService writes, players and ViewModels read.
/// </summary>
public interface ISoundService
{
    /// <summary>Creates and registers a new BufferStreamer for the given job.</summary>
    BufferStreamer StartJob(string jobId);

    /// <summary>Appends a PCM chunk to the job's streamer.</summary>
    void Append(string jobId, byte[] pcm, bool final);

    /// <summary>Returns the active streamer for a job, or null if not found.</summary>
    BufferStreamer? GetStreamer(string jobId);

    /// <summary>Raised when a new generation job starts. Subscribe here to get the streamer.</summary>
    event Action<string, BufferStreamer> JobStarted;
}
