using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConstellaTTS.Core.Controls;

public partial class TrackHeader : UserControl
{
    public static readonly StyledProperty<string> TrackNameProperty =
        AvaloniaProperty.Register<TrackHeader, string>(nameof(TrackName), "Track");

    public static readonly StyledProperty<string> TrackMetaProperty =
        AvaloniaProperty.Register<TrackHeader, string>(nameof(TrackMeta), "");

    public static readonly StyledProperty<IBrush?> AccentColorProperty =
        AvaloniaProperty.Register<TrackHeader, IBrush?>(nameof(AccentColor));

    public string TrackName
    {
        get => GetValue(TrackNameProperty);
        set => SetValue(TrackNameProperty, value);
    }

    public string TrackMeta
    {
        get => GetValue(TrackMetaProperty);
        set => SetValue(TrackMetaProperty, value);
    }

    public IBrush? AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public TrackHeader()
    {
        InitializeComponent();
    }
}
