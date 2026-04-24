using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.SDK.Primitives;

namespace ConstellaTTS.SDK;

/// <summary>
/// Default ViewModel for a stage — a timeline annotation with no TTS
/// pipeline behaviour. Visually distinguished from sections by a
/// dashed outline (see TrackListView.axaml stage DataTemplate).
///
/// SectionViewModel derives from this and adds engine-specific state.
///
/// Geometry is (<see cref="StartSec"/>, <see cref="DurationSec"/>) —
/// time domain. Pixel rendering is a XAML concern, driven by the
/// timeline viewport.
/// </summary>
public partial class StageViewModel : ViewModel, IStageViewModel
{
    /// <summary>Display label on the block.</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>Block background (hex).</summary>
    [ObservableProperty] private string _bg = "#2A2560";

    /// <summary>Label foreground — track accent (hex).</summary>
    [ObservableProperty] private string _accentColor = "#7C6AF7";

    /// <summary>Start time on the timeline, in seconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndSec))]
    private double _startSec;

    /// <summary>Duration of the block, in seconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndSec))]
    private double _durationSec;

    /// <inheritdoc />
    public double EndSec => StartSec + DurationSec;
}
