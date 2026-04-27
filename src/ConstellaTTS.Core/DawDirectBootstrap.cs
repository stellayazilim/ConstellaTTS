using ConstellaTTS.Core.Layouts;
using ConstellaTTS.Core.Views;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;

namespace ConstellaTTS.Core;


/// <summary>
/// Original bootstrap that opens <see cref="MainWindow"/> and mounts
/// the full DAW chrome (layout, toolbar, context bar, content,
/// status bar). Kept around as a reference and as a fast path back
/// to "skip the launcher and land directly in the DAW" while we
/// iterate on the launcher flow — but no longer registered as the
/// active <see cref="IConstellaBootstrap"/>; see
/// <see cref="ConstellaBootstrap"/> for the launcher-first version
/// the application currently uses.
/// </summary>
public sealed class DawDirectBootstrap : IConstellaBootstrap
{
    private INavigationManager? _nav;

    public DawDirectBootstrap(INavigationManager nav) => _nav = nav;

    public Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        // Bootstrap navigation is infrastructure — must NOT enter the history
        // stack. Otherwise Ctrl+Z would rollback the initial window+mounts,
        // closing MainWindow and (under default desktop lifetime) the entire app.
        _nav!.ApplyOnly(new NavigationBuilder()
            .OpenWindow<MainWindow>()
            .Mount(Regions.Layout,    typeof(MainLayout))
            .Mount(Regions.Toolbar,   typeof(DawToolbarView))
            .Mount(Regions.ViewTools, typeof(ContextBarView))
            .Mount(Regions.Content,   typeof(TrackListView))
            .Mount(Regions.StatusBar, typeof(StatusBarView))
            .Build());

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _nav = null;
        return ValueTask.CompletedTask;
    }
}
