namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Writes byte content to disk at a given path. No knowledge of encoding or
/// codec — pure byte transport. Implementations expose both buffered and
/// streaming variants; callers choose by payload size.
/// </summary>
public interface IFileWriter
{
    Task WriteAsync(byte[] content, string path);

    Task WriteAsync(Stream content, string path);
}
