using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// View model for the main DAW view toolbar — the strip docked above the
/// track/section workspace. Exposes view-specific actions (sound bank,
/// engine selector, mode switches, …) for XAML command binding.
/// </summary>
public sealed partial class DawToolbarViewModel : ViewModel
{
    /// <summary>Toggles the sound bank flyout. Bound directly to the button's Command.</summary>
    public ToggleSoundBankAction ToggleSoundBank { get; }

    /// <summary>
    /// Mirrors the sound bank window's visibility so the XAML can toggle the
    /// "active" style class on the button without any code-behind.
    /// </summary>
    [ObservableProperty] private bool _isSoundBankVisible;

    public DawToolbarViewModel(
        ToggleSoundBankAction toggleSoundBank,
        SampleLibraryWindow   soundBankWindow)
    {
        ToggleSoundBank     = toggleSoundBank;
        IsSoundBankVisible  = soundBankWindow.IsVisible;
        soundBankWindow.VisibilityChanged += (_, _) =>
            IsSoundBankVisible = soundBankWindow.IsVisible;
    }
}
