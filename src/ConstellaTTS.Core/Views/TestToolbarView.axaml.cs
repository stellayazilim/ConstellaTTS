using Avalonia.Controls;
using ConstellaTTS.Core.Actions;

namespace ConstellaTTS.Core.Views;

public partial class TestToolbarView : UserControl
{
    private readonly ToggleSoundBankAction _toggleAction;

    public TestToolbarView(ToggleSoundBankAction toggleAction)
    {
        InitializeComponent();
        _toggleAction = toggleAction;
        AttachedToVisualTree += (_, _) => WireButton();
        _toggleAction.CanExecuteChanged += (_, _) => UpdateButtonState();
    }

    private void WireButton()
    {
        if (this.FindControl<Button>("SampleLibraryButton") is not { } btn) return;
        btn.Click += (_, _) => _toggleAction.Execute();
        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        if (this.FindControl<Button>("SampleLibraryButton") is not { } btn) return;
        if (_toggleAction.IsWindowVisible) btn.Classes.Add("active");
        else                               btn.Classes.Remove("active");
    }
}
