using CommunityToolkit.Mvvm.ComponentModel;

namespace ConstellaTTS.SDK.Timeline;

/// <summary>
/// Default <see cref="ITimelineViewport"/> — a process-wide singleton.
/// The static <see cref="Current"/> field is the canonical instance;
/// DI registers the same instance so both XAML (via
/// <c>{x:Static timeline:TimelineViewport.Current}</c>) and code
/// (via constructor injection) see one and the same object.
///
/// Default zoom is 30 px/sec; at this zoom a 30-second stretch of
/// timeline fills ~900 px which matches the current static ruler's
/// visual density (0–28 s across the canvas).
/// </summary>
public sealed partial class TimelineViewport : ObservableObject, ITimelineViewport
{
    /// <summary>The shared viewport singleton. Eagerly initialized on first access.</summary>
    public static TimelineViewport Current { get; } = new();

    [ObservableProperty] private double _pxPerSec         = 30;
    [ObservableProperty] private double _scrollOffsetSec  = 0;

    private TimelineViewport() { }
}
