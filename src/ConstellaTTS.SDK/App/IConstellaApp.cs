namespace ConstellaTTS.SDK.App;

/// <summary>
/// Provides access to the core application services after initialization.
/// Inject this interface into modules and plugins instead of depending on the concrete type.
/// </summary>
public interface IConstellaApp
{
    /// <summary>The DI service provider. Use this to resolve views and application services.</summary>
    IServiceProvider Services { get; }

    /// <summary>Opens, closes, and tracks application windows.</summary>
    UI.Windowing.IWindowManager WindowManager { get; }

    /// <summary>Global undo/redo history for all reversible operations.</summary>
    History.IHistoryManager HistoryManager { get; }

    /// <summary>Orchestrates navigation across windows, layouts, and slots.</summary>
    UI.Navigation.INavigationManager NavigationManager { get; }

    /// <summary>Manages slot registration and view mounting across all windows.</summary>
    UI.Slots.ISlotService SlotService { get; }
}
