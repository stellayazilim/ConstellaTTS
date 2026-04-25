using Avalonia;
using Avalonia.Controls;

namespace ConstellaTTS.Core.Controls;

/// <summary>
/// A named UI region that can be targeted by NavigationManager.
/// Set RegionId in XAML to match a Regions.* constant.
///
/// Example:
///   &lt;regions:RegionControl RegionId="MainWindow.Layout.Content" /&gt;
///
/// NavigationManager scans the visual tree on window open,
/// registers all RegionControl instances, and mounts views by RegionId.
/// Only the targeted region updates — nothing else re-renders.
/// </summary>
public sealed class RegionControl : ContentControl
{
    public static readonly StyledProperty<string?> RegionIdProperty =
        AvaloniaProperty.Register<RegionControl, string?>(nameof(RegionId));

    public string? RegionId
    {
        get => GetValue(RegionIdProperty);
        set => SetValue(RegionIdProperty, value);
    }
}
