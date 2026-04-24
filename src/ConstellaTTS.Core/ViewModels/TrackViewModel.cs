using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Domain;
using ConstellaTTS.SDK;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Default track view model. Plugins may replace via a custom
/// <see cref="ITrackViewModel"/> implementation registered in DI.
/// </summary>
public sealed partial class TrackViewModel(Track track) : ObservableObject, ITrackViewModel
{
    public int    Id      { get; } = track.Id;
    public string Name    { get; } = track.Name;
    public string Color   { get; } = track.Color;
    public string BlockBg { get; } = track.BlockBg;

    [ObservableProperty] private byte _order;

    /// <summary>
    /// True while this track is being actively dragged. Set by the
    /// drag controller; XAML binds the class <c>drag-ghost</c> to this
    /// flag so the in-list row fades out, leaving the cursor-attached
    /// preview as the only visible representation.
    /// </summary>
    [ObservableProperty] private bool _isDragging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicatorBorderThickness))]
    private DropIndicator _dropIndicator = DropIndicator.None;

    public Avalonia.Thickness IndicatorBorderThickness => DropIndicator switch
    {
        DropIndicator.Top    => new Avalonia.Thickness(0, 2, 0, 0),
        DropIndicator.Bottom => new Avalonia.Thickness(0, 0, 0, 2),
        _                    => new Avalonia.Thickness(0)
    };

    /// <summary>
    /// Set during a drag to the dragged track's colour; null otherwise.
    /// BorderBrush binding stays transparent when null — no leaked frame.
    /// </summary>
    [ObservableProperty] private IBrush? _indicatorBrush;

    /// <summary>
    /// Timeline blocks for this track. Typed to <see cref="IStageViewModel"/>
    /// so the collection can hold both stages and sections. Plugins can
    /// substitute their own implementations; defaults are
    /// <see cref="StageViewModel"/> and <see cref="SectionViewModel"/>.
    /// </summary>
    public ObservableCollection<IStageViewModel> Sections { get; } = [];
}
