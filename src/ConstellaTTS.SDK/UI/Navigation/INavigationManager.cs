using Avalonia.Controls;

namespace ConstellaTTS.SDK.UI.Navigation;

public interface INavigationManager
{
    /// <summary>The currently active window, or null if none is open.</summary>
    Window? ActiveWindow { get; }

    /// <summary>Apply request and push onto history stack.</summary>
    void Navigate(NavigationRequest request);

    void Navigate(Action<NavigationBuilder> configure);

    /// <summary>Apply request WITHOUT pushing to history stack.</summary>
    void ApplyOnly(NavigationRequest request);

    void RegisterFlyout(Type flyoutType, Action show, Action hide, Func<bool> isVisible);
}
