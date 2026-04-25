using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.IO;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Reverses a <see cref="FileUploadAction"/>: deletes the working
/// copies under the tmp directory that the upload produced. Symmetric
/// counterpart to <see cref="FileUploadAction"/>.
///
/// <para>
/// <b>Redo path.</b> <see cref="IReversible.Reverse"/> hands back a
/// fresh <see cref="FileUploadAction"/> seeded from the original
/// source paths captured at first upload. Redo therefore re-ingests
/// from the user's filesystem rather than restoring the previous tmp
/// copies — simpler, no soft-delete staging area to maintain. The
/// trade-off: if a source has been moved or deleted between undo and
/// redo, the re-upload raises
/// <see cref="SDK.Exceptions.SourceFileNotFoundException"/> and the
/// redo fails loudly.
/// </para>
/// </summary>
public sealed class FileUploadReverseAction : ActionBase, IReversible
{
    private readonly IReadOnlyList<UploadedFile> _uploaded;
    private readonly IFileWriter                 _fileWriter;
    private readonly string                      _tmpRoot;

    public override string  Id          => "FileUploadReverseAction";
    public override string  Name        => "Dosya Yükleme Geri Al";
    public override string? Description => $"{_uploaded.Count} dosya kaldırılır.";

    public FileUploadReverseAction(
        IReadOnlyList<UploadedFile> uploaded,
        IFileWriter                 fileWriter,
        string                      tmpRoot)
    {
        _uploaded   = uploaded;
        _fileWriter = fileWriter;
        _tmpRoot    = tmpRoot;
    }

    public override void Execute(object? data = null)
    {
        foreach (var file in _uploaded)
        {
            // Best-effort delete: if the file is already gone (e.g. the
            // user wiped tmp out-of-band) we don't fail. The on-disk
            // side converges to "absent" either way.
            if (File.Exists(file.TmpPath))
                File.Delete(file.TmpPath);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a fresh <see cref="FileUploadAction"/> built from the
    /// original source paths captured during first upload. The new
    /// action's Execute will re-read the source files and produce a
    /// new tmp copy set; the resulting <see cref="UploadedFile"/>
    /// records will have the same <c>OriginalPath</c> and
    /// <c>TmpPath</c> as before (the dotted naming is deterministic)
    /// but are fresh instances — a subsequent undo therefore deletes
    /// by tmp path, which converges correctly.
    /// </remarks>
    public IAction Reverse(IReversible? previous, params object[] args)
    {
        var sourcePaths = _uploaded.Select(u => u.OriginalPath).ToList();
        return new FileUploadAction(sourcePaths, _fileWriter, _tmpRoot);
    }
}
