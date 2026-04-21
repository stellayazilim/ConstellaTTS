namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Per-job streaming audio buffer.
/// Owns a BufferWriteStream (single writer) and vends BufferReadStreams (multiple readers).
///
/// Lifecycle:
///   1. Instantiated when a generation job starts (tts.start).
///   2. Append() called per incoming PCM chunk.
///   3. Readers call CreateReader() at any time and consume via ReadChunksAsync.
///   4. On final chunk: PCM → Opus encode, temp file deleted, Finalized raised.
///   5. Dispose() cleans up remaining temp files.
/// </summary>
public sealed class BufferStreamer : IDisposable
{
    private readonly BufferWriteStream _writer;
    private readonly string            _outputPath;
    private readonly AudioFormat       _format;
    private volatile int  _totalBytes;
    private volatile bool _finalized;
    private bool _disposed;

    /// <summary>Unique job ID this streamer belongs to.</summary>
    public string JobId { get; }

    /// <summary>Audio format of the raw PCM data.</summary>
    public AudioFormat Format => _format;

    /// <summary>Total PCM bytes written so far.</summary>
    public int TotalBytes => _totalBytes;

    /// <summary>Total duration available so far in seconds.</summary>
    public double TotalSeconds => _format.BytesToSeconds(_totalBytes);

    /// <summary>True after the final chunk has been processed and Opus file is written.</summary>
    public bool IsFinalized => _finalized;

    /// <summary>Raised after each Append — provides total bytes available.</summary>
    public event Action<int>? BytesAvailable;

    /// <summary>Raised once Opus encoding is complete — provides the output file path.</summary>
    public event Action<string>? Finalized;

    public BufferStreamer(string jobId, string tempDir, string outputPath,
        AudioFormat? format = null)
    {
        JobId       = jobId;
        _outputPath = outputPath;
        _format     = format ?? new AudioFormat();
        _writer     = new BufferWriteStream(tempDir, jobId);
    }

    // Write path — IPCService dispatch loop only

    /// <summary>Appends a raw PCM chunk. If final is true, triggers Opus encoding.</summary>
    public void Append(byte[] pcm, bool final)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _writer.Write(pcm);
        _totalBytes += pcm.Length;

        BytesAvailable?.Invoke(_totalBytes);

        if (final)
            FinalizeInternal();
    }

    // Read path — any consumer

    /// <summary>Creates a reader starting at the given byte offset.</summary>
    public BufferReadStream CreateReader(int atOffset = 0) =>
        new(_writer.FilePath,
            getTotalBytes: () => _totalBytes,
            isFinalized:   () => _finalized,
            format:        _format,
            initialOffset: atOffset);

    /// <summary>Creates a reader starting at the given timeline position in seconds.</summary>
    public BufferReadStream CreateReader(double atSeconds) =>
        CreateReader(_format.SecondsToBytes(atSeconds));

    // Finalize

    private void FinalizeInternal()
    {
        if (_finalized) return;
        _finalized = true;

        _writer.Dispose();

        EncodeToOpus(_writer.FilePath, _outputPath);

        try { File.Delete(_writer.FilePath); } catch { /* best effort */ }

        Finalized?.Invoke(_outputPath);
    }

    /// <summary>
    /// Encodes raw PCM to Opus.
    /// TODO: replace with Concentus or libopus P/Invoke.
    /// </summary>
    private static void EncodeToOpus(string rawPath, string outputPath)
    {
        File.Copy(rawPath, outputPath, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
        try { if (File.Exists(_writer.FilePath)) File.Delete(_writer.FilePath); } catch { }
    }
}
