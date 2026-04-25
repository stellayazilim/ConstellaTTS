using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Domain;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Default track view model. Plugins may replace via a custom
/// <see cref="ITrackViewModel"/> implementation registered in DI.
///
/// <c>Id</c>, <c>Color</c>, <c>BlockBg</c> are captured from the domain
/// <see cref="Track"/> at construction and stay fixed for the VM's
/// lifetime — identity and accent are stable. <c>Name</c> is mutable
/// (user can rename inline) and <c>IsEditing</c> is pure view state
/// driving the rename label↔TextBox swap.
/// </summary>
public sealed partial class TrackViewModel : ObservableObject, ITrackViewModel
{
    public TrackViewModel(Track track)
    {
        Id      = track.Id;
        Color   = track.Color;
        BlockBg = track.BlockBg;
        _name   = track.Name;
    }

    public int    Id      { get; }
    public string Color   { get; }
    public string BlockBg { get; }

    [ObservableProperty] private string _name;

    [ObservableProperty] private byte _order;

    /// <summary>
    /// True while this track is being actively dragged. Set by the
    /// drag controller; XAML binds the class <c>drag-ghost</c> to this
    /// flag so the in-list row fades out, leaving the cursor-attached
    /// preview as the only visible representation.
    /// </summary>
    [ObservableProperty] private bool _isDragging;

    /// <summary>
    /// True while the user is inline-renaming this track via a right-click
    /// on the header. XAML swaps the Name TextBlock for a TextBox while
    /// this is set.
    /// </summary>
    [ObservableProperty] private bool _isEditing;

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
