using Avalonia.Platform.Storage;

namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Reads file references from the system clipboard. Used by paste operations
/// to ingest files copied from the OS file manager. Returns storage items
/// rather than raw paths so non-local sources (cloud, MTP, archives) can be
/// filtered at the caller via <see cref="IStorageItem.TryGetLocalPath"/>.
/// </summary>
public interface IClipboardReader
{
    Task<IEnumerable<IStorageItem>> ReadAsync();
}
