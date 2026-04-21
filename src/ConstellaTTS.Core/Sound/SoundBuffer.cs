namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Represents the current state of a streaming audio generation job.
///
/// While chunks are arriving, Data holds the accumulated bytes and FilePath is null.
/// Once the buffer limit is reached or the stream ends (IsFinal = true),
/// the data is flushed to disk and FilePath is set. After that Data is empty —
/// consumers should read from FilePath instead.
/// </summary>
public sealed class SoundBuffer
{
    /// <summary>Unique job ID — matches the IPC message ID that started the generation.</summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>Target track index on the timeline.</summary>
    public int TrackId { get; init; }

    /// <summary>Target section ID within the track.</summary>
    public string SectionId { get; init; } = string.Empty;

    /// <summary>
    /// Accumulated audio bytes while the stream is in progress.
    /// Empty after the buffer has been flushed to disk.
    /// </summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    /// Path to the flushed audio file on disk.
    /// Null while chunks are still accumulating in memory.
    /// Set once the buffer limit is reached or IsFinal is true.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>True when the Python daemon has sent the last chunk for this job.</summary>
    public bool IsFinal { get; init; }

    /// <summary>True when the audio data has been flushed to disk.</summary>
    public bool IsFlushed => FilePath is not null;
}
