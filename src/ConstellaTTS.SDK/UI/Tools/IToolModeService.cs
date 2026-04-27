using System.ComponentModel;

namespace ConstellaTTS.SDK.UI.Tools;

/// <summary>Top-level pointer tool. Mutually exclusive with each other.</summary>
public enum ToolMode
{
    /// <summary>Pick / edit existing blocks on the timeline.</summary>
    Select,

    /// <summary>
    /// Draw new blocks on the timeline. The sub-type created is
    /// determined by <see cref="IToolModeService.CreateType"/>.
    /// </summary>
    Create,

    /// <summary>
    /// Sample-assignment mode. The sample library window is open
    /// (toggled in lockstep with this tool); the timeline canvas
    /// is suspended for normal interaction — click-to-select,
    /// move/resize, and create are all disabled. The only active
    /// gesture is dragging a sample from the library onto a
    /// section, which fires an <c>AssignSampleAction</c> and
    /// leaves the tool active so the user can chain assignments.
    ///
    /// <para>
    /// <b>Why a tool, not just a window state.</b> Modeling this
    /// as a third tool (alongside Select and Create) gives the
    /// canvas a single switch to gate every pointer behaviour
    /// against, and makes the "sample library is open" condition
    /// observable through the same 
    /// <see cref="IToolModeService.PropertyChanged"/> stream the
    /// rest of the UI already listens to. The sample library
    /// window's visibility piggybacks on the tool transition
    /// instead of carrying its own independent state machine.
    /// </para>
    /// </summary>
    Sample,
}

/// <summary>
/// Create-mode sub-selection — which kind of block the drag gesture
/// produces. Always one of Section or Stage; there is no empty state.
/// The user picks via the context bar's sub-radio; keyboard overrides
/// (Ctrl / Ctrl+Shift) can temporarily force one or the other during
/// the gesture regardless of this value.
/// </summary>
public enum CreateType
{
    /// <summary>Draw TTS sections (bound to generation pipeline).</summary>
    Section,

    /// <summary>Draw stage directions (visual annotations, no generation).</summary>
    Stage,
}

/// <summary>
/// Single source of truth for the current pointer tool and create sub-type.
/// Singleton — ContextBar writes, TrackListView reads, neither knows about
/// the other. State change is observed via <see cref="INotifyPropertyChanged"/>.
///
/// Preview layer: <see cref="PreviewTool"/> and <see cref="PreviewCreateType"/>
/// are transient "what the user is hovering toward" overrides. The timeline
/// view sets them while the pointer is over the canvas and a Ctrl / Ctrl+Shift
/// modifier is held; it clears them when the modifier is released or the
/// pointer leaves the canvas. The context bar binds to <see cref="EffectiveTool"/>
/// and <see cref="EffectiveCreateType"/> so its button highlight tracks the
/// preview while it's active, then falls back to the committed state.
///
/// Write Tool/CreateType from explicit user actions (button clicks, committed
/// gestures). Write Preview* only from hover+modifier detection.
/// </summary>
public interface IToolModeService : INotifyPropertyChanged
{
    ToolMode   Tool       { get; set; }
    CreateType CreateType { get; set; }

    /// <summary>
    /// Transient preview override. Non-null while the user's hover+modifier
    /// combination would force a create gesture from Select mode. Clearing
    /// to null reverts UI highlight to <see cref="Tool"/>.
    /// </summary>
    ToolMode?   PreviewTool       { get; set; }

    /// <summary>
    /// Transient preview override for the create sub-type. See
    /// <see cref="PreviewTool"/>.
    /// </summary>
    CreateType? PreviewCreateType { get; set; }

    /// <summary>What the UI should display as active — preview wins over committed.</summary>
    ToolMode   EffectiveTool       { get; }

    /// <summary>What the UI should display as the active sub-type — preview wins.</summary>
    CreateType EffectiveCreateType { get; }
}
