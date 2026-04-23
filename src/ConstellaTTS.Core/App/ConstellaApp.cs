using Microsoft.Extensions.DependencyInjection;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Theme;
using ConstellaTTS.SDK.UI.Slots;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.History;

namespace ConstellaTTS.Core.App;

/// <summary>
/// Central application context — pure service accessor.
/// All properties resolve lazily from the DI container.
/// </summary>
public sealed class ConstellaApp(IServiceProvider services) : IConstellaApp
{
    public IServiceProvider          Services          { get; } = services;
    public IHistoryManager           HistoryManager    => services.GetRequiredService<IHistoryManager>();
    public INavigationManager        NavigationManager => services.GetRequiredService<INavigationManager>();
    public IThemeProvider            ThemeProvider     => services.GetRequiredService<IThemeProvider>();
}
