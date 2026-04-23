namespace ConstellaTTS.SDK.App;

/// <summary>
/// Provides access to the core application services after initialization.
/// All properties resolve lazily from the DI container.
/// </summary>
public interface IConstellaApp
{
    IServiceProvider                 Services          { get; }
    History.IHistoryManager          HistoryManager    { get; }
    UI.Navigation.INavigationManager NavigationManager { get; }
    Theme.IThemeProvider             ThemeProvider     { get; }
}
