using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Domain;

namespace ConstellaTTS.Core.ViewModels;

public enum DropIndicator { None, Top, Bottom }

public sealed partial class TrackViewModel(Track track) : ObservableObject
{
    public int    Id    { get; } = track.Id;
    public string Name  { get; } = track.Name;
    public string Color { get; } = track.Color;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicatorBorderThickness))]
    private DropIndicator _dropIndicator = DropIndicator.None;

    public Avalonia.Thickness IndicatorBorderThickness => DropIndicator switch
    {
        DropIndicator.Top    => new Avalonia.Thickness(0, 2, 0, 0),
        DropIndicator.Bottom => new Avalonia.Thickness(0, 0, 0, 2),
        _                    => new Avalonia.Thickness(0)
    };

    public IBrush IndicatorBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));

    public ObservableCollection<DummySectionViewModel> Sections { get; } = [];
}

public sealed partial class DummySectionViewModel
{
    public string Label   { get; init; } = string.Empty;
    public string Color   { get; init; } = "#7C6AF7";
    public double LeftPx  { get; init; }
    public double WidthPx { get; init; }
}
