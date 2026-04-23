using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Keybinds;
using ConstellaTTS.SDK.UI.Navigation;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Toggles the Sample Library flyout.
/// ActionBase + IBindable — Ctrl+L hotkey.
/// </summary>
public sealed class ToggleSoundBankAction : ActionBase, IBindable
{
    private readonly SampleLibraryWindow _window;
    private readonly INavigationManager  _navigation;

    public override string  Id          => "ToggleSoundBank";
    public override string  Name        => "Ses Bankası";
    public override string? Description => "Ses bankası panelini açar veya kapatır.";
    public KeyCombo[]       Bindings    { get; set; } = [KeyMap.Ctrl | KeyMap.L];

    public bool IsWindowVisible => _window.IsVisible;

    public ToggleSoundBankAction(SampleLibraryWindow window, INavigationManager navigation)
    {
        _window     = window;
        _navigation = navigation;
        _window.VisibilityChanged += (_, _) => RaiseCanExecuteChanged();
    }

    public override void Execute(object? data = null)
    {
        _navigation.Navigate(
            _window.IsVisible
                ? new HideFlyoutRequest(typeof(SampleLibraryWindow))
                : new ShowFlyoutRequest(typeof(SampleLibraryWindow)));
    }
}
