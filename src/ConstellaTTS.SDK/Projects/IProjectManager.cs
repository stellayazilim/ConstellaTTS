using ConstellaTTS.SDK.App;

namespace ConstellaTTS.SDK.Projects;

/// <summary>
/// Owns the lifecycle of the single active <see cref="IConstellaProject"/>.
/// The application is single-document — at most one project is open at
/// any given moment; switching projects unloads the current one before
/// loading the next.
///
/// <para>
/// <b>Manifest layer.</b> Each project's <c>project.ctts</c> file is
/// the persisted form of <see cref="IConstellaProject"/>;
/// <see cref="OpenAsync"/> hydrates a project from it on open,
/// <see cref="SaveAsync"/> writes the live state back, and
/// <see cref="CreateAsync"/> materialises a fresh manifest as part
/// of laying out a new project on disk. The format is MessagePack —
/// project-wide convention for binary artefacts.
/// </para>
///
/// <para>
/// <b>Registry coupling.</b> A successful <see cref="CreateAsync"/>
/// or <see cref="OpenAsync"/> also touches
/// <see cref="IProjectsService"/> so the project appears (or moves
/// to the top) of the launcher's recent list on next boot. The two
/// services are paired via DI; callers don't manage the registry
/// directly.
/// </para>
/// </summary>
public interface IProjectManager
{
    /// <summary>
    /// The project currently loaded in memory, or <c>null</c> if none.
    /// Mutated by <see cref="CreateAsync"/> and <see cref="OpenAsync"/>;
    /// subscribers can observe transitions via <see cref="ActiveChanged"/>.
    /// </summary>
    IConstellaProject? Active { get; }

    /// <summary>
    /// Raised after <see cref="Active"/> changes — both when a project
    /// is loaded and when one is unloaded (in which case the argument
    /// is <c>null</c>). Always fires on the thread that completed the
    /// transition; UI subscribers should marshal as needed.
    /// </summary>
    event EventHandler<IConstellaProject?>? ActiveChanged;

    /// <summary>
    /// Lays out a new project at <paramref name="projectDirectory"/>
    /// (the directory itself becomes the project root — no
    /// sub-folder is created), writes a fresh <c>project.ctts</c>
    /// manifest, registers an entry with <see cref="IProjectsService"/>,
    /// and sets the result as <see cref="Active"/>. The project name
    /// is taken from the directory's leaf name so the on-disk
    /// identity and the registry display name stay aligned.
    /// </summary>
    /// <param name="projectDirectory">
    /// Absolute path to the folder that will become the project
    /// root. Must exist; the launcher's folder picker is what
    /// produces this argument, so existence is guaranteed in the
    /// normal flow.
    /// </param>
    Task<IConstellaProject> CreateAsync(string projectDirectory);

    /// <summary>
    /// Loads an existing project from <paramref name="projectFilePath"/>
    /// (an absolute path to a <c>project.ctts</c> file) and sets it
    /// as <see cref="Active"/>. The project root is the directory
    /// containing the manifest. Reads the manifest and hydrates the
    /// returned <see cref="IConstellaProject"/>'s library list from
    /// it; a missing or unreadable manifest yields an empty library
    /// list rather than failing the open, so projects copied without
    /// their <c>.ctts</c> file still come up. Refreshes the
    /// registry's <c>LastOpened</c> so the launcher's recent list
    /// reorders accordingly on next boot.
    /// </summary>
    /// <param name="projectFilePath">
    /// Absolute path to an existing project's <c>project.ctts</c> file.
    /// </param>
    Task<IConstellaProject> OpenAsync(string projectFilePath);

    /// <summary>
    /// Persists the active project's manifest to its on-disk
    /// <c>project.ctts</c> file. Written atomically through a
    /// sibling <c>.tmp</c> file and a final rename so a crash
    /// can't leave a half-written manifest.
    ///
    /// <para>
    /// <b>Explicit, not automatic.</b> Callers ask for a save when
    /// they've made a change worth persisting (a library reference
    /// added or removed; future Ctrl+S on user-edited fields).
    /// Manifests are small, so saving on every relevant mutation
    /// is a fine default until there's a measurable reason to
    /// coalesce. Sample imports do <i>not</i> trigger a save —
    /// the manifest doesn't track individual samples.
    /// </para>
    ///
    /// <para>
    /// No-op if no project is active.
    /// </para>
    /// </summary>
    Task SaveAsync();
}
