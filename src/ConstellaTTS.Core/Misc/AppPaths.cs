namespace ConstellaTTS.Core.Misc;

/// <summary>
/// Resolves the directories ConstellaTTS reads from and writes to
/// at the application level. Two roles, separated because they
/// have different lifetimes and different writability guarantees:
///
/// <list type="bullet">
///   <item><see cref="InstallRoot"/> — the install directory. Holds
///   the executable, bundled assets, and anything else shipped
///   alongside the app. Read-only in deployed builds (Program
///   Files installs aren't user-writable; even per-user installs
///   shouldn't have the app stomping on its own binary).</item>
///   <item><see cref="UserDataRoot"/> — the user-data directory.
///   Holds the project registry, future global settings, the
///   future cache index. Always writable, and persists across
///   re-installs / upgrades because it lives outside the install
///   folder.</item>
/// </list>
///
/// <para>
/// <b>Why this split.</b> Earlier this was one path — the
/// executable's own directory — which works for portable installs
/// but breaks the moment the app is installed under <c>Program
/// Files</c> (no write permission) or auto-updated (the install
/// folder gets replaced on upgrade and any state inside it is
/// lost). Splitting binary from data is the standard Windows
/// pattern: install once, accumulate user data forever, upgrade
/// without losing recent projects.
/// </para>
///
/// <para>
/// <b>Where the data lives.</b> Deployed builds use
/// <c>%LOCALAPPDATA%\ConstellaTTS\</c> (a per-machine, per-user
/// directory under <c>C:\Users\&lt;name&gt;\AppData\Local\</c>) —
/// the same place Discord, VS Code, GitHub Desktop, and most
/// modern Windows apps put their data. Local rather than Roaming
/// because the registry references absolute paths to projects on
/// this machine; carrying it across to another machine in a
/// domain-roaming profile would just produce broken entries.
/// Project content itself is portable through whatever syncing
/// the user wants (git, Dropbox, manual copy) — only the registry
/// is per-machine.
/// </para>
///
/// <para>
/// <b>Dev mode unification.</b> In a development build both roots
/// resolve to the repo root. Keeping them merged in dev avoids
/// the friction of having to clean two locations on a reset, and
/// the registry under the repo is convenient when working with
/// multiple build configurations side-by-side. Detection is by
/// walking up from <see cref="AppContext.BaseDirectory"/> looking
/// for <c>ConstellaTTS.sln</c> — found within a few hops means
/// dev, otherwise deployed.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Filename to look for when deciding whether we're running
    /// from a dev tree. Anyone running a published build doesn't
    /// have this file, so the search falls through to the deployed
    /// branch the way deployed builds want.
    /// </summary>
    private const string DevSentinel = "ConstellaTTS.sln";

    /// <summary>
    /// Maximum directory hops above <see cref="AppContext.BaseDirectory"/>
    /// to inspect for the dev sentinel. Six covers the worst case
    /// the build layout produces (<c>bin/Debug/net10.0/&lt;rid&gt;/publish/</c>
    /// is five) with one to spare.
    /// </summary>
    private const int MaxWalkUp = 8;

    /// <summary>
    /// Folder name under <c>%LOCALAPPDATA%</c> for the user-data
    /// directory in deployed builds. Capitalised to match the
    /// product name as the user sees it in the UI; the install
    /// folder under <c>Program Files</c> or
    /// <c>%LOCALAPPDATA%\Programs</c> uses the same casing.
    /// </summary>
    private const string AppDataFolderName = "ConstellaTTS";

    private static readonly Lazy<string> _installRoot  = new(ResolveInstallRoot);
    private static readonly Lazy<string> _userDataRoot = new(ResolveUserDataRoot);

    /// <summary>
    /// Absolute path to the install directory — where the
    /// executable and bundled assets live. Treated as read-only
    /// at runtime; nothing the user can mutate gets written here.
    /// In dev, this is the repo root; in deployed builds, the
    /// directory <see cref="AppContext.BaseDirectory"/> resolves
    /// to.
    /// </summary>
    public static string InstallRoot => _installRoot.Value;

    /// <summary>
    /// Absolute path to the user-data directory — where the
    /// project registry and future user-level state live. Always
    /// writable. In dev, this is the repo root (same as
    /// <see cref="InstallRoot"/>); in deployed builds,
    /// <c>%LOCALAPPDATA%\ConstellaTTS\</c>. Created on first
    /// access so callers can hand the path straight to file
    /// operations without preflight checks.
    /// </summary>
    public static string UserDataRoot => _userDataRoot.Value;

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/>
    /// looking for the dev sentinel. Found → repo root; not
    /// found → <see cref="AppContext.BaseDirectory"/> (the
    /// install directory in deployed builds).
    /// </summary>
    private static string ResolveInstallRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (int i = 0; i < MaxWalkUp && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, DevSentinel)))
                return dir.FullName;
            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// In dev, mirrors <see cref="InstallRoot"/> so all state
    /// stays under the repo. In deployed builds, points at
    /// <c>%LOCALAPPDATA%\ConstellaTTS\</c> and ensures the
    /// directory exists so callers can write into it without
    /// a separate <c>CreateDirectory</c> step.
    /// </summary>
    private static string ResolveUserDataRoot()
    {
        // If install resolution found a dev sentinel, we're under
        // the repo — keep user data and binary unified there. The
        // sentinel check duplicates the walk in ResolveInstallRoot
        // rather than depending on its result, because the answers
        // are conceptually different (one is "where am I", the
        // other is "where do I write") and a future change to one
        // shouldn't silently change the other.
        if (IsDevBuild())
            return InstallRoot;

        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var path  = Path.Combine(local, AppDataFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool IsDevBuild()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < MaxWalkUp && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, DevSentinel)))
                return true;
            dir = dir.Parent;
        }
        return false;
    }
}
