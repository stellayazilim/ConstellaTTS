using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.UI.Navigation;

namespace ConstellaTTS.Core;


/// <summary>
/// Active boot path: opens <see cref="LauncherWindow"/> as the
/// application's first surface. The launcher is self-contained — no
/// region mounts, no shared chrome — so this bootstrap only needs to
/// open the window. The DAW (<see cref="MainWindow"/> plus its
/// region mounts) opens later, once the user picks a project from
/// the launcher and the launcher hands control over.
///
/// <para>
/// The previous bootstrap that goes straight to the DAW is preserved
/// in <see cref="DawDirectBootstrap"/>. It isn't registered today,
/// but staying in the codebase makes it a one-line DI swap when we
/// want to skip the launcher (e.g. while iterating on a DAW-only
/// feature and the launcher would just be in the way).
/// </para>
/// </summary>
public sealed class ConstellaBootstrap : IConstellaBootstrap
{
    private INavigationManager? _nav;

    public ConstellaBootstrap(INavigationManager nav) => _nav = nav;

    public Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        // Bootstrap navigation is infrastructure — must NOT enter the history
        // stack. Otherwise Ctrl+Z would rollback the initial window open,
        // closing the launcher and (under default desktop lifetime) the
        // entire app.
        _nav!.ApplyOnly(new NavigationBuilder()
            .OpenWindow<LauncherWindow>()
            .Build());

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _nav = null;
        return ValueTask.CompletedTask;
    }
}
