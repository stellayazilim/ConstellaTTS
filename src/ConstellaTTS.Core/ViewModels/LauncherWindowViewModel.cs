using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConstellaTTS.Core.Layouts;
using ConstellaTTS.Core.Views;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Backs the launcher window. Three responsibilities:
///
/// <list type="bullet">
///   <item><b>Recent projects</b> — reads <see cref="IProjectsService"/>
///   on construction and exposes a filtered, observable view of the
///   registry. Search text filters by name and path; the remove
///   command drops a row from the registry.</item>
///   <item><b>New project</b> — folder picker resolves the project
///   directory; <see cref="IProjectManager.CreateAsync"/> lays out
///   the project structure and registers the entry.</item>
///   <item><b>Hand-off to the DAW</b> — both new-project and
///   recent-click flows finish by navigating to
///   <see cref="MainWindow"/> (closing the launcher behind them) so
///   the user lands in the editor for the freshly opened project.</item>
/// </list>
///
/// <para>
/// <b>Open existing</b> — uses a file picker filtered to
/// <c>*.ctts</c>. The user picks the project file directly the way
/// Rider / Visual Studio target a solution file rather than its
/// containing folder; the project root is derived from the file's
/// parent directory. New-project still uses a folder picker because
/// at creation time the <c>.ctts</c> doesn't exist yet for the user
/// to point at.
/// </para>
///
/// <para>
/// <b>Why this VM owns navigation.</b> The hand-off lives here rather
/// than in a shared helper because there's currently exactly one call
/// site triggering the launcher → DAW transition, and a second site
/// (a future "open by command-line argument" path) would naturally
/// pull this logic out into a shared <c>DawNavigation</c> helper.
/// Premature abstraction otherwise.
/// </para>
///
/// <para>
/// <b>History bypass.</b> The launcher → DAW transition uses
/// <see cref="INavigationManager.ApplyOnly"/> rather than
/// <see cref="INavigationManager.Navigate"/>. We don't want Ctrl+Z in
/// the DAW to roll back to the launcher — boot navigations are
/// infrastructure, not user actions, and undoing them risks closing
/// the application entirely. Same reasoning <see cref="ConstellaBootstrap"/>
/// applies for opening the launcher itself.
/// </para>
///
/// <para>
/// <b>Project hand-off (deferred).</b> When <c>NavigationRequest.Execute</c>
/// starts threading its <c>data</c> parameter through the navigation
/// pipeline, the active <see cref="IConstellaProject"/> from
/// <see cref="IProjectManager.Active"/> will travel with the open-DAW
/// request so <see cref="MainWindow"/> can populate itself from it.
/// Today the project sits in <see cref="IProjectManager"/> and the DAW
/// pulls it from there if and when it needs it.
/// </para>
/// </summary>
public sealed partial class LauncherWindowViewModel : ViewModel
{
    private readonly IProjectsService    _projectsService;
    private readonly IProjectManager     _projectManager;
    private readonly INavigationManager  _navigationManager;

    /// <summary>
    /// Row in the launcher's recent-projects list. Wraps a
    /// <see cref="ProjectEntry"/> with the precomputed display
    /// strings the XAML template binds to.
    /// </summary>
    public sealed record RecentProjectRow(
        string         Name,
        string         Path,
        string         LastOpened,
        bool           Pinned,
        ProjectEntry   Source);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Chatterbox engine · ready";

    [ObservableProperty]
    private string _versionLabel = "studio · v0.4.2";

    [ObservableProperty]
    private string _versionPill = "EARLY ACCESS";

    public ObservableCollection<RecentProjectRow> RecentProjects  { get; } = new();
    public ObservableCollection<RecentProjectRow> FilteredRecent  { get; } = new();

    public LauncherWindowViewModel(
        IProjectsService    projectsService,
        IProjectManager     projectManager,
        INavigationManager  navigationManager)
    {
        _projectsService    = projectsService;
        _projectManager     = projectManager;
        _navigationManager  = navigationManager;

        // Best-effort initial load — failures fall back to an empty
        // list so the launcher still opens. A future iteration can
        // surface load errors via the status footer.
        _ = LoadRecentAsync();
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            var entries = await _projectsService.GetAllAsync();
            RecentProjects.Clear();

            // Pinned-first, then most-recent-first within each group —
            // matches what the user expects from a "recent" list when
            // they've explicitly anchored a few projects to the top.
            var ordered = entries
                .OrderByDescending(e => e.Pinned)
                .ThenByDescending(e => e.LastOpened);

            foreach (var entry in ordered)
                RecentProjects.Add(ToRow(entry));

            RebuildFilter();
        }
        catch
        {
            // Swallow — empty registry is a valid state at first
            // launch, and a corrupted registry shouldn't block the
            // launcher from opening. Logging will be added once the
            // ILogger is wired into this layer.
        }
    }

    partial void OnSearchTextChanged(string value) => RebuildFilter();

    private void RebuildFilter()
    {
        FilteredRecent.Clear();

        var query = SearchText?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            foreach (var row in RecentProjects)
                FilteredRecent.Add(row);
            return;
        }

        foreach (var row in RecentProjects)
        {
            if (row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
             || row.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                FilteredRecent.Add(row);
        }
    }

    private static RecentProjectRow ToRow(ProjectEntry entry) =>
        new(
            Name:       entry.Name,
            Path:       entry.Path,
            LastOpened: FormatLastOpened(entry.LastOpened),
            Pinned:     entry.Pinned,
            Source:     entry);

    /// <summary>
    /// Renders a relative-time string for the row's "last opened"
    /// subtitle. Resolution is intentionally coarse — users care
    /// about ordering, not precise timestamps — and the strings
    /// stay in Turkish to match the rest of the launcher chrome
    /// until the i18n layer lands.
    /// </summary>
    private static string FormatLastOpened(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when;
        if (delta.TotalMinutes < 1)  return "az önce";
        if (delta.TotalHours   < 1)  return $"{(int)delta.TotalMinutes} dk önce";
        if (delta.TotalHours   < 24) return $"{(int)delta.TotalHours} saat önce";
        if (delta.TotalDays    < 2)  return "Dün";
        if (delta.TotalDays    < 7)  return $"{(int)delta.TotalDays} gün önce";
        if (delta.TotalDays    < 30) return $"{(int)(delta.TotalDays / 7)} hafta önce";
        if (delta.TotalDays    < 365) return $"{(int)(delta.TotalDays / 30)} ay önce";
        return $"{(int)(delta.TotalDays / 365)} yıl önce";
    }

    /// <summary>
    /// "Proje Aç" — file picker filtered to <c>*.ctts</c>. The user
    /// targets the project file directly; <see cref="IProjectManager.OpenAsync"/>
    /// hydrates the project from it and we hand off to the DAW.
    /// </summary>
    [RelayCommand]
    private async Task OpenProjectAsync(object? sender)
    {
        if (sender is not Visual visual) return;

        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Proje dosyası seç",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ConstellaTTS Projesi")
                {
                    Patterns                    = new[] { "*.ctts" },
                    AppleUniformTypeIdentifiers = new[] { "public.data" },
                    MimeTypes                   = new[] { "application/octet-stream" },
                }
            },
        });

        if (picked.Count == 0) return;

        var path = picked[0].TryGetLocalPath();
        if (path is null) return;

        try
        {
            await _projectManager.OpenAsync(path);
            OpenDawWindow();
        }
        catch (Exception ex)
        {
            StatusText = $"Açılamadı: {ex.Message}";
        }
    }

    /// <summary>
    /// "Yeni Proje" — folder picker resolves the directory the user
    /// wants to use as the project root. The directory's name becomes
    /// the project name; <see cref="IProjectManager.CreateAsync"/>
    /// lays out the project structure (samples folder + a fresh
    /// <c>project.ctts</c> manifest), then we hand off to the DAW.
    /// Refuses to overwrite an existing project — the user is steered
    /// to "Proje Aç" by the manager's exception, which we surface as
    /// a status message.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectAsync(object? sender)
    {
        if (sender is not Visual visual) return;

        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        var picked = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = "Yeni proje klasörü seç",
            AllowMultiple = false,
        });

        if (picked.Count == 0) return;

        var projectDir = picked[0].TryGetLocalPath();
        if (projectDir is null) return;

        try
        {
            await _projectManager.CreateAsync(projectDir);
            StatusText = $"Oluşturuldu: {Path.GetFileName(projectDir)}";

            OpenDawWindow();
        }
        catch (Exception ex)
        {
            StatusText = $"Oluşturulamadı: {ex.Message}";
        }
    }

    /// <summary>
    /// Activates a recent entry. The row's path is the absolute
    /// <c>.ctts</c> path stored in the registry; hands it straight
    /// to <see cref="IProjectManager.OpenAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task OpenRecentAsync(RecentProjectRow? row)
    {
        if (row is null) return;

        try
        {
            await _projectManager.OpenAsync(row.Path);
            OpenDawWindow();
        }
        catch (Exception ex)
        {
            StatusText = $"Açılamadı: {ex.Message}";
        }
    }

    /// <summary>
    /// Performs the launcher → DAW transition. Mirrors the layout
    /// produced by <see cref="DawDirectBootstrap"/>: open the main
    /// window, mount the standard region chain, close the launcher
    /// — all in one navigation request. Goes through
    /// <see cref="INavigationManager.ApplyOnly"/> so the transition
    /// stays out of the history stack.
    ///
    /// <para>
    /// The active <see cref="IConstellaProject"/> from
    /// <see cref="IProjectManager.Active"/> rides along on the
    /// open-window request as the navigation payload, so
    /// <see cref="MainWindowViewModel.GetParams"/> can populate the
    /// title-bar badge (and, eventually, the rest of the editor) the
    /// moment the window is constructed.
    /// </para>
    ///
    /// <para>
    /// <b>Ordering inside the queue.</b> NavigationManager's
    /// <c>QueueNavigationRequest</c> handler pulls the
    /// <c>OpenWindowRequest</c> to the front and runs the rest after,
    /// so the open / mount / close ordering here is correct: the
    /// DAW window opens first, the regions mount onto it, and only
    /// then does the launcher close. The launcher being transient
    /// means "close" is a real <c>Window.Close()</c> — the instance
    /// is gone, GC reclaims its VM and any subscriptions.
    /// </para>
    /// </summary>
    private void OpenDawWindow()
    {
        _navigationManager.ApplyOnly(new NavigationBuilder()
            .OpenWindow<MainWindow>(_projectManager.Active)
            .Mount(Regions.Layout,    typeof(MainLayout))
            .Mount(Regions.Toolbar,   typeof(DawToolbarView))
            .Mount(Regions.ViewTools, typeof(ContextBarView))
            .Mount(Regions.Content,   typeof(TrackListView))
            .Mount(Regions.StatusBar, typeof(StatusBarView))
            .CloseWindow<LauncherWindow>()
            .Build());
    }

    /// <summary>
    /// Drops a row from both the registry and the in-memory list.
    /// </summary>
    [RelayCommand]
    private async Task RemoveRecentAsync(RecentProjectRow? row)
    {
        if (row is null) return;

        await _projectsService.RemoveAsync(row.Source.Path);

        RecentProjects.Remove(row);
        FilteredRecent.Remove(row);

        StatusText = "Listeden kaldırıldı";
    }

    /// <summary>
    /// Raised when the user activates a row's "reveal in folder"
    /// action. The path is the absolute <c>.ctts</c> file path;
    /// subscribers (the view code-behind, in practice) handle the
    /// platform shell-out and decide whether to reveal the file or
    /// its parent folder.
    /// </summary>
    public event EventHandler<string>? RevealInFolderRequested;

    /// <summary>
    /// "Konumda göster" — the actual platform shell-out lives in
    /// the view code-behind; this command raises
    /// <see cref="RevealInFolderRequested"/> with the row's path so
    /// the code-behind can act on it.
    /// </summary>
    [RelayCommand]
    private void ShowInFolder(RecentProjectRow? row)
    {
        if (row is null) return;

        StatusText = $"Konumda: {row.Path}";
        RevealInFolderRequested?.Invoke(this, row.Path);
    }
}
