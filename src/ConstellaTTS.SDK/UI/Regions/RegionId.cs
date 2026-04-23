using Avalonia;
using Avalonia.Controls;

namespace ConstellaTTS.SDK.UI.Regions;

/// <summary>
/// Attached property that marks a Control as a named region.
/// NavigationManager scans the visual tree for these and builds a registry.
///
/// Usage in XAML:
///   &lt;ContentControl regions:RegionId.Value="MainWindow.Layout.Content" /&gt;
///   &lt;StackPanel     regions:RegionId.Value="MainWindow.Layout.Toolbar" /&gt;
///
/// Path convention: WindowName.LayoutName.SlotName
///   e.g. MainWindow.Layout.Content
///        MainWindow.Layout.Toolbar
///        MainWindow.Layout.ViewTools
/// </summary>
public static class RegionId
{
    public static readonly AttachedProperty<string?> ValueProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, string?>("Value", inherits: false);

    public static string? GetValue(Control control) =>
        control.GetValue(ValueProperty);

    public static void SetValue(Control control, string? value) =>
        control.SetValue(ValueProperty, value);
}
