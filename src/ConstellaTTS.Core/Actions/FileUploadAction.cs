using ConstellaTTS.SDK.Exceptions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.IO;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Ingests one or more files into the application tmp directory. Each
/// source path is copied under a flattened "dotted full path" name so
/// that two files with the same leaf name from different source
/// directories don't collide.
///
/// <para>
/// <b>No view-model coupling.</b> The action's only side effect is on
/// disk: it writes byte content into tmp via <see cref="IFileWriter"/>
/// and records the resulting <see cref="UploadedFile"/> entries on
/// itself for the reverse path. A higher-level view-model can scan
/// the tmp folder later — the on-disk state is the source of truth
/// for this slice.
/// </para>
///
/// <para>
/// <b>Reversibility.</b> Symmetric counterpart to
/// <see cref="FileUploadReverseAction"/> — undo deletes the tmp
/// copies; a subsequent redo runs a fresh
/// <see cref="FileUploadAction"/> built from the original source
/// paths captured at first execution. If a source is no longer
/// reachable on redo, a <see cref="SourceFileNotFoundException"/>
/// propagates out and the history layer surfaces the failure.
/// </para>
///
/// <para>
/// <b>Sync execute.</b> <see cref="IAction.Execute"/> is synchronous
/// across the codebase; this action blocks the calling thread on the
/// underlying async file write via <c>GetAwaiter().GetResult()</c>.
/// Sample files are typically small (voice-clone references in the
/// MB range); if larger payloads become common, the broader
/// <c>IAction</c> contract will need an async variant.
/// </para>
/// </summary>
public sealed class FileUploadAction : ActionBase, IReversible
{
    private readonly IReadOnlyList<string> _sourcePaths;
    private readonly IFileWriter           _fileWriter;
    private readonly string                _tmpRoot;

    private IReadOnlyList<UploadedFile> _uploaded = Array.Empty<UploadedFile>();

    public override string  Id          => "FileUploadAction";
    public override string  Name        => "Dosya Yükle";
    public override string? Description => $"{_sourcePaths.Count} dosya yüklenir.";

    /// <summary>
    /// Snapshot of the files actually copied into tmp by the most recent
    /// <see cref="Execute"/>. Empty before Execute runs. Exposed for
    /// callers that want to surface the result (e.g. a future view-model
    /// list) without re-scanning the tmp directory.
    /// </summary>
    public IReadOnlyList<UploadedFile> Uploaded => _uploaded;

    public FileUploadAction(
        IEnumerable<string> sourcePaths,
        IFileWriter         fileWriter,
        string              tmpRoot)
    {
        _sourcePaths = sourcePaths as IReadOnlyList<string> ?? sourcePaths.ToList();
        _fileWriter  = fileWriter;
        _tmpRoot     = tmpRoot;
    }

    public override void Execute(object? data = null)
    {
        Directory.CreateDirectory(_tmpRoot);

        var uploaded = new List<UploadedFile>(_sourcePaths.Count);

        foreach (var sourcePath in _sourcePaths)
        {
            var fullPath   = Path.GetFullPath(sourcePath);
            var dottedName = ToDottedName(fullPath);
            var targetPath = Path.Combine(_tmpRoot, dottedName);

            CopyToTmp(fullPath, targetPath);

            uploaded.Add(new UploadedFile(
                OriginalPath: fullPath,
                TmpPath:      targetPath,
                DisplayName:  Path.GetFileName(fullPath)));
        }

        _uploaded = uploaded;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a <see cref="FileUploadReverseAction"/> carrying the
    /// snapshot of files actually uploaded by this Execute. The reverse
    /// action deletes the tmp copies; its own <see cref="IReversible.Reverse"/>
    /// produces a fresh <see cref="FileUploadAction"/> seeded from the
    /// original source paths so a redo can re-ingest the files.
    /// </remarks>
    public IAction Reverse(IReversible? previous, params object[] args) =>
        new FileUploadReverseAction(_uploaded, _fileWriter, _tmpRoot);

    /// <summary>
    /// Flattens an absolute path into a single file-system-safe segment so
    /// that two files with the same leaf name from different source
    /// directories don't collide in tmp. Drive-letter colons are dropped
    /// and path separators (both flavours) are collapsed to dots.
    /// </summary>
    /// <example>
    /// <c>C:\Users\Kerim\samples\kick.wav</c>
    /// → <c>C.Users.Kerim.samples.kick.wav</c>
    /// </example>
    private static string ToDottedName(string fullPath) =>
        fullPath
            .Replace(":", string.Empty)
            .Replace('\\', '.')
            .Replace('/', '.')
            .TrimStart('.');

    private void CopyToTmp(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
            throw new SourceFileNotFoundException(sourcePath);

        // Stream the source into IFileWriter so the writer owns the
        // semantics of "where bytes go" and this action stays a pure
        // orchestration step. Override on collision by design — the
        // dotted-fullpath naming scheme already disambiguates by source
        // location, so a collision here means the same source file is
        // being re-uploaded and refreshing the copy is the right answer.
        using var sourceStream = File.OpenRead(sourcePath);
        _fileWriter.WriteAsync(sourceStream, targetPath).GetAwaiter().GetResult();
    }
}
