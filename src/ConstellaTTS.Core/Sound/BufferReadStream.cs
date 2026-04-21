namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Reads byte sequences from a raw PCM temp file.
/// Multiple instances can read the same file concurrently (FileShare.ReadWrite).
/// Each instance maintains its own cursor for sequential reads.
///
/// Two read styles:
///   Seek:       ReadAt(offset, length)             — random access by byte offset
///               ReadAt(atSeconds, length)           — random access by timeline position
///   Streaming:  ReadChunksAsync(chunkSize, ct)     — async enumerable, waits for new bytes
///
/// Timeline seek:
///   streamer.CreateReader(atSeconds: 1.5) starts at 1.5s in the audio.
///   streamer.CreateReader(atOffset: 0)   starts at the beginning.
/// </summary>
public sealed class BufferReadStream : IDisposable
{
    private readonly string       _filePath;
    private readonly Func<int>    _getTotalBytes;
    private readonly Func<bool>   _isFinalized;
    private readonly AudioFormat  _format;
    private bool _disposed;

    /// <summary>Current sequential read cursor in bytes.</summary>
    public int Cursor { get; private set; }

    /// <summary>Current cursor position expressed as a timeline position in seconds.</summary>
    public double CursorSeconds => _format.BytesToSeconds(Cursor);

    public BufferReadStream(
        string      filePath,
        Func<int>   getTotalBytes,
        Func<bool>  isFinalized,
        AudioFormat format,
        int         initialOffset = 0)
    {
        _filePath      = filePath;
        _getTotalBytes = getTotalBytes;
        _isFinalized   = isFinalized;
        _format        = format;
        Cursor         = initialOffset;
    }

    // Seek

    /// <summary>Moves the cursor to the given byte offset.</summary>
    public void SeekTo(int byteOffset) => Cursor = byteOffset;

    /// <summary>Moves the cursor to the given timeline position in seconds.</summary>
    public void SeekTo(double seconds) => Cursor = _format.SecondsToBytes(seconds);

    // Random access — does not move cursor

    /// <summary>Reads up to length bytes at the given byte offset.</summary>
    public byte[] ReadAt(int byteOffset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var available = _getTotalBytes() - byteOffset;
        if (available <= 0) return [];

        var toRead = Math.Min(length, available);

        using var fs = OpenReadOnly();
        fs.Seek(byteOffset, SeekOrigin.Begin);

        var buf  = new byte[toRead];
        var read = 0;
        while (read < toRead)
        {
            var n = fs.Read(buf, read, toRead - read);
            if (n == 0) break;
            read += n;
        }

        return buf[..read];
    }

    /// <summary>Reads up to length bytes at the given timeline position in seconds.</summary>
    public byte[] ReadAt(double atSeconds, int length) =>
        ReadAt(_format.SecondsToBytes(atSeconds), length);

    // Sequential read — advances cursor

    /// <summary>Reads the next length bytes from the cursor and advances it.</summary>
    public byte[] Read(int length)
    {
        var data = ReadAt(Cursor, length);
        Cursor += data.Length;
        return data;
    }

    // Async streaming

    /// <summary>
    /// Streams chunks sequentially from the current cursor position.
    /// Waits for new data if the writer hasn't finished yet.
    /// Completes when the stream is finalized and all bytes are consumed.
    ///
    /// Example — play from playhead position:
    ///   var reader = streamer.CreateReader(atSeconds: 1.5);
    ///   await foreach (var chunk in reader.ReadChunksAsync(ct: ct))
    ///       audioPlayer.Feed(chunk);
    /// </summary>
    public async IAsyncEnumerable<byte[]> ReadChunksAsync(
        int chunkSize = 4096,
        TimeSpan? pollInterval = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var poll = pollInterval ?? TimeSpan.FromMilliseconds(10);

        while (!ct.IsCancellationRequested)
        {
            var available = _getTotalBytes() - Cursor;

            if (available >= chunkSize)
            {
                yield return Read(chunkSize);
                continue;
            }

            if (_isFinalized())
            {
                if (available > 0)
                    yield return Read(available);
                yield break;
            }

            await Task.Delay(poll, ct);
        }
    }

    private FileStream OpenReadOnly() =>
        new(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public void Dispose() => _disposed = true;
}
