using System.ComponentModel;

namespace ConstellaTTS.SDK.Timeline;

/// <summary>
/// Defines the timeline's view onto an unbounded time vector (0 → ∞
/// seconds). Blocks (sections, stages) store their position in the time
/// domain; pixel projection is a pure function of this viewport's
/// <see cref="PxPerSec"/> (zoom) and <see cref="ScrollOffsetSec"/>
/// (horizontal scroll). The viewport itself owns nothing except these
/// two numbers and a change notification so bindings re-project when
/// the user zooms or scrolls.
///
/// Helper methods convert between pixel and time. Both are linear —
/// log/exponential zoom curves, if ever added, belong in the zoom
/// control's input handling, not here.
/// </summary>
public interface ITimelineViewport : INotifyPropertyChanged
{
    /// <summary>Horizontal zoom: how many pixels represent one second.</summary>
    double PxPerSec { get; set; }

    /// <summary>
    /// The time (seconds) at the viewport's left edge. Increasing this
    /// scrolls the timeline to the right (later content comes into view).
    /// </summary>
    double ScrollOffsetSec { get; set; }

    /// <summary>Convert a time (seconds) to a canvas-local pixel offset.</summary>
    double TimeToPx(double timeSec) => (timeSec - ScrollOffsetSec) * PxPerSec;

    /// <summary>Convert a canvas-local pixel offset to a time (seconds).</summary>
    double PxToTime(double px) => (px / PxPerSec) + ScrollOffsetSec;

    /// <summary>Convert a pixel span (width) to a duration (seconds).</summary>
    double PxToDuration(double px) => px / PxPerSec;

    /// <summary>Convert a duration (seconds) to a pixel span (width).</summary>
    double DurationToPx(double sec) => sec * PxPerSec;
}
