namespace ConstellaTTS.SDK.Exceptions;

/// <summary>
/// Thrown when an operation needs to read from a source file path but the
/// file is no longer present. Most commonly raised when an undone upload
/// is being redone and the original file the user dropped has since been
/// moved, deleted, or otherwise made unreachable.
/// </summary>
public sealed class SourceFileNotFoundException : ConstellaException
{
    public string SourcePath { get; }

    public SourceFileNotFoundException(string sourcePath)
        : base($"Kaynak dosya bulunamadı: {sourcePath}")
    {
        SourcePath = sourcePath;
    }

    public SourceFileNotFoundException(string sourcePath, Exception inner)
        : base($"Kaynak dosya bulunamadı: {sourcePath}", inner)
    {
        SourcePath = sourcePath;
    }
}
