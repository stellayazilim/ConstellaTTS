using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Theme;
using ConstellaTTS.SDK.UI.Navigation;

namespace ConstellaTTS.Core;

/// <summary>
/// Default <see cref="IConstellaApp"/>. Holds the
/// <see cref="Lazy{T}"/> handles supplied by the container; every
/// access goes through the same handle, so first-use does the
/// container lookup and every subsequent read is a cached field
/// access.
///
/// <para>
/// <b>No <see cref="IServiceProvider"/> dependency.</b> Earlier
/// revisions held an <see cref="IServiceProvider"/> reference so the
/// properties could resolve services on demand — convenient, but it
/// re-introduced the service-locator pattern this layer was built
/// to avoid. With <see cref="Lazy{T}"/> threaded through the
/// constructor, the container is the one wiring up the deferred
/// lookups; <see cref="ConstellaApp"/> never needs to hold the
/// container.
/// </para>
/// </summary>
public sealed class ConstellaApp(
    Lazy<IHistoryManager>    history,
    Lazy<INavigationManager> navigation,
    Lazy<IThemeProvider>     theme) : IConstellaApp
{
    public Lazy<IHistoryManager>    HistoryManager    => history;
    public Lazy<INavigationManager> NavigationManager => navigation;
    public Lazy<IThemeProvider>     ThemeProvider     => theme;
}
