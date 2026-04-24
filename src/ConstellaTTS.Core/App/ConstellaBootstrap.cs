using ConstellaTTS.Core.Layout;
using ConstellaTTS.Core.Views;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;

namespace ConstellaTTS.Core.App;

public sealed class ConstellaBootstrap : IConstellaBootstrap
{
    private INavigationManager? _nav;

    public ConstellaBootstrap(INavigationManager nav) => _nav = nav;

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
