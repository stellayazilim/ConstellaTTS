using System.ComponentModel;

namespace ConstellaTTS.SDK.ViewModelContracts;

/// <summary>
/// Base contract for a block on the track timeline — any visual element
/// placed on a track. A stage is a pure timeline annotation: it has
/// geometry and a label but no TTS pipeline wiring. Typical use: mark a
/// silent beat that the narrative context explains (an on-scene explosion
/// cuts the dialogue, actor steps offstage, etc.).
///
/// <see cref="ISectionViewModel"/> derives from this and adds engine
/// state (emotion, dirty flag, model binding). A track's block list is
/// typed to <c>IStageViewModel</c> so it can hold both stages and
/// sections polymorphically.
///
/// Geometry is stored in the TIME domain — (<see cref="StartSec"/>,
/// <see cref="DurationSec"/>). Pixel projection is the view's job:
/// XAML multi-bindings take these plus the timeline viewport's
/// PxPerSec / ScrollOffsetSec and compute Canvas.Left / Width at
/// render time. Zooming and scrolling therefore don't touch block
/// data — the viewport alone changes and every binding re-projects.
/// </summary>
public interface IStageViewModel : INotifyPropertyChanged
{
    /// <summary>Display label on the block (e.g. "Patlama — diyalog kesik").</summary>
    string Label       { get; set; }

    /// <summary>Block background — track-specific dark tinted color (hex).</summary>
    string Bg          { get; set; }

    /// <summary>Label foreground — track accent (bright) color (hex).</summary>
    string AccentColor { get; set; }

    /// <summary>Start time on the timeline, in seconds.</summary>
    double StartSec    { get; set; }

    /// <summary>Length of the block, in seconds.</summary>
    double DurationSec { get; set; }

    /// <summary>
    /// Computed end time (<see cref="StartSec"/> + <see cref="DurationSec"/>).
    /// Read-only convenience for collision/snap call sites. Not stored;
    /// callers that need a mutable end should adjust DurationSec.
    /// </summary>
    double EndSec      { get; }

    /// <summary>
    /// Runtime hover flag set by the timeline view while a sample is
    /// being dragged over this block from the Sample Library. The
    /// section DataTemplate watches this and lights up a glow / accent
    /// border so the user can see exactly which target their drop
    /// will land on. Stages also expose the property for type-system
    /// uniformity, but their template ignores it — stages aren't
    /// valid drop targets and the drag handlers won't ever set it
    /// true on a stage.
    ///
    /// <para>
    /// <b>Not persisted.</b> This is purely transient view state, set
    /// during DragOver and cleared on DragLeave / Drop. The on-disk
    /// <c>BlockData</c> shape doesn't carry it, hydration always
    /// starts at false.
    /// </para>
    /// </summary>
    bool IsDropTarget { get; set; }
}
