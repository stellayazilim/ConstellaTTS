using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.SDK;

/// <summary>
/// Central application context. Initialized once at startup via the module registry.
/// Accessible globally through <see cref="Instance"/> after initialization.
/// </summary>
public sealed class ConstellaApp(
    IServiceProvider services,
    IWindowManager windowManager,
    IHistoryManager historyManager,
    INavigationManager navigationManager,
    ISlotService slotService)
{
    private static ConstellaApp? _instance;

    /// <summary>
    /// Global singleton accessor. Available after <see cref="Initialize"/> is called.
    /// </summary>
    public static ConstellaApp Instance =>
        _instance ?? throw new InvalidOperationException("ConstellaApp is not initialized.");

    /// <summary>DI service provider — use for resolving views and other services.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Manages opening, closing, and tracking application windows.</summary>
    public IWindowManager WindowManager { get; } = windowManager;

    /// <summary>Global undo/redo history for all reversible operations.</summary>
    public IHistoryManager HistoryManager { get; } = historyManager;

    /// <summary>Orchestrates navigation across windows, layouts and slots.</summary>
    public INavigationManager NavigationManager { get; } = navigationManager;

    /// <summary>Manages slot registration and view mounting.</summary>
    public ISlotService SlotService { get; } = slotService;

    /// <summary>
    /// Builds all registered modules in dependency order and initializes the application.
    /// Call once at startup — typically in App.axaml.cs OnFrameworkInitializationCompleted.
    /// </summary>
    public static ConstellaApp Initialize(Action<ConstellaModuleRegistry> configure)
    {
        if (_instance is not null)
            throw new InvalidOperationException("ConstellaApp is already initialized.");

        var registry = new ConstellaModuleRegistry();
        configure(registry);

        var provider = registry.Initialize();
        _instance = provider.GetRequiredService<ConstellaApp>();
        return _instance;
    }
}
