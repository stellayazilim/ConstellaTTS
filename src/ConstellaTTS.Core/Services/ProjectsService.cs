using ConstellaTTS.Core.Misc;
using ConstellaTTS.SDK.Projects;
using MessagePack;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Filesystem-backed implementation of <see cref="IProjectsService"/>.
/// Persists the project registry as a MessagePack file at
/// <c>{AppPaths.UserDataRoot}/projects.ctts</c>. MessagePack is the
/// project-wide convention for persisted artefacts; keeping the
/// registry on the same format as project manifests means one
/// serialization stack to maintain.
///
/// <para>
/// <b>Local, not roaming.</b> The registry lives under
/// <c>%LOCALAPPDATA%\ConstellaTTS\</c> in deployed builds (and
/// the repo root in dev). Discord / VS Code / GitHub Desktop
/// pattern — per-machine, per-user state in a writable directory
/// that survives upgrades and uninstalls cleanly. Project content
/// itself stays portable through whatever syncing mechanism the
/// user prefers (git is the obvious one given the .ctts +
/// samples/ layout); only this registry is per-machine, and it
/// gets rebuilt naturally as the user opens projects on a new
/// machine.
/// </para>
///
/// <para>
/// <b>Concurrency.</b> Reads and writes are serialized through a
/// single <see cref="SemaphoreSlim"/> so concurrent callers don't
/// race on the file. The registry is small enough that a global
/// lock is fine; we are not optimising for multi-writer throughput.
/// </para>
///
/// <para>
/// <b>Atomicity.</b> Writes go to a sibling temp file first and are
/// then renamed over the live file via <see cref="File.Move(string, string, bool)"/>
/// (atomic on the same volume). A crash mid-write leaves either the
/// previous registry intact or — in the unlucky window between flush
/// and rename — a stray <c>.tmp</c> file the next boot can clean up.
/// The live file is never observed half-written.
/// </para>
/// </summary>
public sealed class ProjectsService : IProjectsService
{
    private readonly string        _registryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProjectsService()
    {
        // User-data directory — portable in dev (repo root),
        // <c>%LOCALAPPDATA%\ConstellaTTS\</c> in deployed builds.
        // Stays out of <c>%APPDATA%</c> (Roaming) on purpose: the
        // registry holds absolute paths to projects on this
        // machine, so domain-roaming the file across machines
        // would just produce broken entries.
        _registryPath = Path.Combine(AppPaths.UserDataRoot, "projects.ctts");
    }

    public async Task<IReadOnlyList<ProjectEntry>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await ReadRegistryAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(ProjectEntry entry)
    {
        await _gate.WaitAsync();
        try
        {
            var entries = (await ReadRegistryAsync()).ToList();

            // Path-based identity — re-opening a project refreshes the
            // existing row instead of producing a duplicate.
            var existingIdx = entries.FindIndex(e =>
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));

            if (existingIdx >= 0)
                entries[existingIdx] = entry;
            else
                entries.Add(entry);

            await WriteRegistryAsync(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string projectPath)
    {
        await _gate.WaitAsync();
        try
        {
            var entries = (await ReadRegistryAsync()).ToList();
            var removed = entries.RemoveAll(e =>
                string.Equals(e.Path, projectPath, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                await WriteRegistryAsync(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ProjectEntry>> ReadRegistryAsync()
    {
        if (!File.Exists(_registryPath))
            return Array.Empty<ProjectEntry>();

        await using var stream = File.OpenRead(_registryPath);

        // An empty file (e.g. created by a crashed write that never
        // got past the rename) is treated as an empty registry rather
        // than letting the deserializer throw. Saves the user from
        // having to manually delete the file to recover.
        if (stream.Length == 0)
            return Array.Empty<ProjectEntry>();

        var entries = await MessagePackSerializer.DeserializeAsync<List<ProjectEntry>>(stream);

        return (IReadOnlyList<ProjectEntry>?)entries ?? Array.Empty<ProjectEntry>();
    }

    private async Task WriteRegistryAsync(IReadOnlyList<ProjectEntry> entries)
    {
        var tempPath = _registryPath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await MessagePackSerializer.SerializeAsync(stream, entries.ToList());
        }

        File.Move(tempPath, _registryPath, overwrite: true);
    }
}
