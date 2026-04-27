using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Projects;
using MessagePack;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Single-document <see cref="IProjectManager"/> implementation.
/// Tracks one <see cref="Active"/> project at a time and owns the
/// on-disk <c>project.ctts</c> manifest read / written through
/// MessagePack.
///
/// <para>
/// <b>The .ctts file is the project.</b> Open / Save target the
/// manifest file directly (Rider / Visual Studio style — pick the
/// solution, not its containing folder); the project root is just
/// the directory that file happens to live in. Create still takes
/// a folder argument because at creation time the .ctts doesn't
/// exist yet for the user to point at.
/// </para>
///
/// <para>
/// <b>Layout produced by Create.</b> A <c>samples/</c> sub-folder is
/// created underneath the project directory if it doesn't already
/// exist, and a fresh <c>project.ctts</c> manifest is written so the
/// project is in a fully valid state from the moment it lands on
/// disk. Creation refuses to overwrite an existing manifest — the
/// user is steered to "Open" instead, which is the correct verb for
/// an already-existing project.
/// </para>
///
/// <para>
/// <b>Atomic writes.</b> <see cref="SaveAsync"/> writes the manifest
/// to a sibling <c>.tmp</c> file and renames it over the live file
/// (atomic on the same volume); a crash mid-write leaves either the
/// previous manifest intact or a stray <c>.tmp</c> file the next
/// boot can ignore. The live file is never observed half-written.
/// </para>
/// </summary>
public sealed class ProjectManager : IProjectManager
{
    /// <summary>
    /// Filename of the on-disk manifest, relative to the project
    /// root. Centralised so all three lifecycle paths (create / open
    /// / save) agree on the same name without duplicating the string
    /// literal.
    /// </summary>
    private const string ManifestFileName = "project.ctts";

    private readonly IProjectsService _projectsService;

    private IConstellaProject? _active;

    public IConstellaProject? Active => _active;

    public event EventHandler<IConstellaProject?>? ActiveChanged;

    public ProjectManager(IProjectsService projectsService)
    {
        _projectsService = projectsService;
    }

    public async Task<IConstellaProject> CreateAsync(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException(
                $"Project directory does not exist: {projectDirectory}");

        var name         = DeriveName(projectDirectory);
        var manifestPath = Path.Combine(projectDirectory, ManifestFileName);

        // Refuse to overwrite an existing project. The user wants
        // "open existing" instead — silently rebuilding the manifest
        // would lose whatever library references and (eventually)
        // user-edited fields the existing project carries.
        if (File.Exists(manifestPath))
            throw new InvalidOperationException(
                $"A project already exists in {projectDirectory}. " +
                "Open it instead of creating a new one.");

        Directory.CreateDirectory(Path.Combine(projectDirectory, "samples"));

        var project = new ConstellaProject { Path = projectDirectory };

        // Materialise the manifest so the project is in a valid
        // state from the moment it lands on disk — no "first save
        // creates the file" surprise.
        await WriteManifestAsync(project);

        // Sweep the tmp/ directory in case anything from a previous
        // crashed session is sitting there. Newly created project,
        // so this is almost always a no-op — but the same call
        // path runs on Open, where it matters.
        SweepTmpDirectory(project.TmpPath);

        // Persist a registry entry pointing at the manifest file.
        // Storing the .ctts path (not the folder) keeps the registry
        // consistent with the open API: launcher rows hand the same
        // string straight to OpenAsync.
        await _projectsService.UpsertAsync(new ProjectEntry(
            Name:       name,
            Path:       manifestPath,
            LastOpened: DateTimeOffset.UtcNow,
            Pinned:     false));

        _active = project;
        ActiveChanged?.Invoke(this, _active);

        return project;
    }

    public async Task<IConstellaProject> OpenAsync(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            throw new FileNotFoundException(
                $"Project file does not exist: {projectFilePath}",
                projectFilePath);

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrEmpty(projectDirectory))
            throw new ArgumentException(
                $"Project file path has no parent directory: {projectFilePath}",
                nameof(projectFilePath));

        var name    = DeriveName(projectDirectory);
        var project = new ConstellaProject { Path = projectDirectory };

        // Hydrate from manifest. Tolerate read failures so a corrupt
        // or unreadable file doesn't block the open — the user lands
        // in the DAW with an empty library list and can rebuild from
        // the UI. Logging will surface the underlying cause once
        // ILogger is wired into this layer.
        try
        {
            await using var stream = File.OpenRead(projectFilePath);
            var manifest = await MessagePackSerializer.DeserializeAsync<ProjectManifest>(stream);
            if (manifest is not null)
            {
                project.ReplaceSampleLibraries(manifest.SampleLibraries);
                project.ReplaceTracks(manifest.Tracks);
            }
        }
        catch
        {
            // Swallow — see comment above.
        }

        // Clear out leftover tmp/ entries from a previous session
        // that crashed before its name-prompt modal resolved. The
        // user never confirmed those recordings, so they have no
        // place in the live catalogue or on disk.
        SweepTmpDirectory(project.TmpPath);

        // Refresh the registry's LastOpened. UpsertAsync's path-based
        // identity means an existing entry is updated in place rather
        // than duplicated; if the entry doesn't exist (e.g. the user
        // is opening a project that wasn't created via the launcher),
        // it gets added so it shows up in the recent list next time.
        await _projectsService.UpsertAsync(new ProjectEntry(
            Name:       name,
            Path:       projectFilePath,
            LastOpened: DateTimeOffset.UtcNow,
            Pinned:     false));

        _active = project;
        ActiveChanged?.Invoke(this, _active);

        return project;
    }

    public async Task SaveAsync()
    {
        if (_active is null) return;
        await WriteManifestAsync(_active);
    }

    /// <summary>
    /// Project name = folder leaf. Trim trailing separators so
    /// GetFileName gives back the actual folder name even if the
    /// caller passed a trailing slash.
    /// </summary>
    private static string DeriveName(string projectDirectory)
    {
        var name = Path.GetFileName(projectDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(name))
            throw new ArgumentException(
                $"Cannot derive a project name from the directory path: {projectDirectory}",
                nameof(projectDirectory));

        return name;
    }

    /// <summary>
    /// Best-effort cleanup of the project's <c>tmp/</c> directory.
    /// Anything in here from a previous session is, by definition,
    /// scratch the user never confirmed (an in-progress recording
    /// when the app crashed, an aborted upload). Re-creates the
    /// directory empty rather than deleting it outright so callers
    /// that immediately want to write into it don't trip on a
    /// missing folder. Failures are swallowed — if the directory
    /// is locked or unwritable, the next session will try again.
    /// </summary>
    private static void SweepTmpDirectory(string tmpPath)
    {
        try
        {
            if (Directory.Exists(tmpPath))
            {
                foreach (var file in Directory.EnumerateFiles(tmpPath))
                {
                    try { File.Delete(file); }
                    catch { /* one stuck file shouldn't block the rest */ }
                }
                foreach (var dir in Directory.EnumerateDirectories(tmpPath))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch { /* same logic for stray subdirectories */ }
                }
            }
        }
        catch
        {
            // Top-level enumeration failure is rare — tolerate it,
            // tmp/ stays as-is and a future session will retry.
        }
    }

    /// <summary>
    /// Serializes the current state of <paramref name="project"/> to
    /// <c>project.ctts</c> via the temp-file-and-rename atomicity
    /// pattern. The temp file lives next to the live file so the
    /// rename is on the same volume (atomic on Windows / Unix); a
    /// crash between the write and the rename leaves the previous
    /// manifest intact.
    /// </summary>
    private static async Task WriteManifestAsync(IConstellaProject project)
    {
        var manifest = new ProjectManifest
        {
            Version         = 1,
            Name            = project.Name,
            SampleLibraries = project.SampleLibraries.ToList(),
            Tracks          = project.Tracks.ToList(),
        };

        var manifestPath = Path.Combine(project.Path, ManifestFileName);
        var tempPath     = manifestPath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await MessagePackSerializer.SerializeAsync(stream, manifest);
        }

        File.Move(tempPath, manifestPath, overwrite: true);
    }
}
