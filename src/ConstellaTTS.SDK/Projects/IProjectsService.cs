namespace ConstellaTTS.SDK.Projects;

/// <summary>
/// Owns the application's project registry: the persisted list of
/// project folders the user has opened or created. Reads and writes
/// the registry file under the application root and exposes a small
/// CRUD-style surface for the launcher and project manager to call.
///
/// <para>
/// <b>Single source of truth.</b> The registry file on disk is the
/// authority. Implementations may cache the entry list in memory for
/// fast reads, but every mutation flushes back to disk before
/// returning so a crash at any point leaves a consistent file.
/// </para>
///
/// <para>
/// <b>Identity.</b> Entries are identified by their absolute
/// <see cref="ProjectEntry.Path"/>. Adding an entry whose path already
/// exists in the registry updates the existing row in-place rather
/// than creating a duplicate; this is the desired behaviour when the
/// user re-opens a project (refresh <c>LastOpened</c>, keep
/// <c>Pinned</c>).
/// </para>
/// </summary>
public interface IProjectsService
{
    /// <summary>
    /// Returns the current registry contents. Order is implementation-
    /// defined; callers that need a specific ordering (pinned-first,
    /// most-recent-first) should sort the returned list themselves.
    /// </summary>
    Task<IReadOnlyList<ProjectEntry>> GetAllAsync();

    /// <summary>
    /// Inserts a new entry or updates an existing one matched by
    /// <see cref="ProjectEntry.Path"/>. Persists the change before
    /// returning.
    /// </summary>
    Task UpsertAsync(ProjectEntry entry);

    /// <summary>
    /// Removes the entry whose <see cref="ProjectEntry.Path"/> matches
    /// the argument. No-op if no such entry exists.
    /// </summary>
    Task RemoveAsync(string projectPath);
}
