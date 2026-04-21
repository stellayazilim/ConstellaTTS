namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Writes raw PCM chunks to a temp file sequentially.
/// Single writer — only one instance per job should exist.
/// </summary>
public sealed class BufferWriteStream : IDisposable
{
    private readonly FileStream _fs;
    private bool _disposed;

    /// <summary>Absolute path of the temp raw file being written.</summary>
    public string FilePath { get; }

    /// <summary>Total bytes written so far.</summary>
    public int BytesWritten { get; private set; }

    public BufferWriteStream(string tempDir, string jobId)
    {
        Directory.CreateDirectory(tempDir);
        FilePath = Path.Combine(tempDir, $"{jobId}.raw");

        _fs = new FileStream(
            FilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,   // BufferReadStream can read simultaneously
            bufferSize: 4096,
            useAsync: false);
    }

    /// <summary>
    /// Appends a PCM chunk and flushes to disk immediately so readers see it.
    /// </summary>
    public void Write(byte[] chunk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fs.Write(chunk);
        _fs.Flush();
        BytesWritten += chunk.Length;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fs.Dispose();
    }
}
