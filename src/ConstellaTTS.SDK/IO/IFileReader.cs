namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Reads file contents from disk. No knowledge of encoding or codec — pure
/// byte transport. Implementations expose both buffered and streaming
/// variants; callers choose by payload size.
/// </summary>
public interface IFileReader
{
    Task<byte[]> ReadAsync(string path);

    Task<Stream> ReadStreamAsync(string path);
}
