using Microsoft.Extensions.DependencyInjection;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Theme;
using ConstellaTTS.SDK.UI.Windowing;
using ConstellaTTS.SDK.UI.Slots;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.History;

namespace ConstellaTTS.Core.App;

/// <summary>
/// Central application context. Initialized once at startup via ConstellaModuleRegistry.
/// Plugins and modules access services through the IConstellaApp interface via DI.
/// </summary>
public sealed class ConstellaApp(
    IServiceProvider services,
    IWindowManager windowManager,
    IHistoryManager historyManager,
    INavigationManager navigationManager,
    ISlotService slotService,
    IThemeProvider themeProvider) : IConstellaApp
{
    /// <inheritdoc/>
    public IServiceProvider Services { get; } = services;

    /// <inheritdoc/>
    public IWindowManager WindowManager { get; } = windowManager;

    /// <inheritdoc/>
    public IHistoryManager HistoryManager { get; } = historyManager;

    /// <inheritdoc/>
    public INavigationManager NavigationManager { get; } = navigationManager;

    /// <inheritdoc/>
    public ISlotService SlotService { get; } = slotService;

    // ThemeProvider inject edildi — singleton olarak oluşturulmasını guarantee eder.
    // Bu sayede RegisterGlobal çağrıları startup'ta execute edilir.
    public IThemeProvider ThemeProvider { get; } = themeProvider;

    /// <summary>
    /// Builds all registered modules in dependency order and returns the initialized app context.
    /// Call once at startup — typically in App.axaml.cs OnFrameworkInitializationCompleted.
    /// </summary>
    public static IConstellaApp Initialize(Action<ConstellaModuleRegistry> configure)
    {
        var registry = new ConstellaModuleRegistry();
        configure(registry);

        var provider = registry.Initialize();
        return provider.GetRequiredService<IConstellaApp>();
    }
}
