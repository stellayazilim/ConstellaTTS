namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Record of a single file ingested into the temporary upload area.
/// Carries enough information to display the file in the UI, locate the
/// working copy in tmp, and re-ingest from the original source if a
/// previously-undone upload is redone.
/// </summary>
/// <param name="OriginalPath">Absolute path of the source file at upload time.</param>
/// <param name="TmpPath">Absolute path of the working copy under the application tmp directory.</param>
/// <param name="DisplayName">User-facing file name (typically <c>Path.GetFileName(OriginalPath)</c>).</param>
public sealed record UploadedFile(string OriginalPath, string TmpPath, string DisplayName);
