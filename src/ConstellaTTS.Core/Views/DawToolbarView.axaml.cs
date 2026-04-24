using Avalonia.Controls;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Toolbar for the DAW view (track + section workspace).
/// All behavior flows through <see cref="ViewModels.DawToolbarViewModel"/>
/// and XAML command bindings — no manual wiring in code-behind.
/// </summary>
public partial class DawToolbarView : UserControl
{
    public DawToolbarView()
    {
        InitializeComponent();
    }
}
