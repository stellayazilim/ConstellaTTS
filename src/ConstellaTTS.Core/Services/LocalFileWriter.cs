using ConstellaTTS.SDK.IO;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Default <see cref="IFileWriter"/> implementation that writes to the
/// local filesystem. Creates the target directory if missing and
/// overwrites any existing file at the destination — collision policy
/// is owned by the caller (typically the dotted-fullpath naming scheme
/// in <see cref="Actions.FileUploadAction"/> already disambiguates by
/// source location, so a same-path write means a deliberate refresh).
/// </summary>
public sealed class LocalFileWriter : IFileWriter
{
    public async Task WriteAsync(byte[] content, string path)
    {
        EnsureDirectory(path);
        await File.WriteAllBytesAsync(path, content);
    }

    public async Task WriteAsync(Stream content, string path)
    {
        EnsureDirectory(path);

        // FileMode.Create truncates if the file already exists; combined
        // with FileShare.None it gives us a clean, exclusive overwrite.
        await using var target = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await content.CopyToAsync(target);
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
