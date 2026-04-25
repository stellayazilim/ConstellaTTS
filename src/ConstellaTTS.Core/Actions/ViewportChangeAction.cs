using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Timeline;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Records a viewport state transition (zoom + scroll) so the user can
/// step backwards through their navigation history with Ctrl+Z. One
/// commit per scroll session, not one per wheel notch — see the
/// debounce in TrackListView for the timing logic.
///
/// <para>
/// <b>Why navigation belongs on the undo stack.</b> Without this, a
/// user who scrolls past their selected section to glance at something
/// upstream loses the editor overlay (it auto-hides when the block
/// scrolls out of the viewport — see <c>PositionBlockEditor</c>). The
/// section's selection is preserved in the SelectionService, but
/// reopening the editor requires manually scrolling back to find the
/// block. Putting viewport state on the same Ctrl+Z stack as everything
/// else gives the user one keystroke to "go back to where I was
/// looking", which transparently restores the overlay because
/// PositionBlockEditor reopens it as soon as the selected block lands
/// back in view.
/// </para>
///
/// <para>
/// <b>Snapshot shape.</b> Both PxPerSec and ScrollOffsetSec are stored
/// because Ctrl+wheel zoom-at-cursor mutates both at once (zooming
/// pivots around the pointer, which means recomputing the scroll offset
/// to keep the cursor anchored). Restoring a zoom but not its matching
/// scroll would warp the timeline to a state the user never saw.
/// </para>
///
/// <para>
/// <b>One scroll burst, one entry.</b> The view-side debounce in
/// TrackListView captures the viewport state at the first wheel tick
/// of a burst, lets subsequent ticks mutate the live viewport directly
/// for responsiveness, and pushes a single ViewportChangeAction once
/// the user pauses (<see cref="TimeSpan"/> threshold lives in the
/// view). Each new burst becomes its own undo entry — no merging.
/// One Ctrl+Z steps back exactly one scroll session, matching the
/// behaviour of Spine2D's transform tool and how users mentally chunk
/// rapid input into discrete acts.
/// </para>
///
/// <para>
/// <b>Side effects.</b> Implements <see cref="IEffect"/> as an
/// extension hook for things that should happen as a consequence of
/// the viewport moving — for example, "collapse the open block editor
/// when the selected block scrolls off-screen as part of this
/// transition". The current build doesn't dispatch any effects (the
/// editor's visibility is reactive to viewport changes via
/// PositionBlockEditor and doesn't need an action to toggle), so
/// <see cref="SideEffects"/> is empty. The interface is here so adding
/// effects later doesn't change the action's shape.
/// </para>
/// </summary>
public sealed class ViewportChangeAction : ActionBase, IReversible, IEffect
{
    private readonly ITimelineViewport _viewport;

    private readonly double _fromPxPerSec;
    private readonly double _fromScrollOffsetSec;

    private readonly double _toPxPerSec;
    private readonly double _toScrollOffsetSec;

    public override string Id   => "ViewportChangeAction";
    public override string Name => "Görünüm";

    /// <inheritdoc />
    /// <remarks>
    /// Empty for now — see the type-level remarks. When a side effect
    /// is added (e.g. a CollapseEditorAction triggered when the
    /// selected block scrolls out of view), this property starts
    /// returning the dispatched action(s). Reverse() must then mirror
    /// the effect chain so undo restores the composite state in one
    /// step.
    /// </remarks>
    public IAction[] SideEffects => System.Array.Empty<IAction>();

    public ViewportChangeAction(
        ITimelineViewport viewport,
        double            fromPxPerSec,
        double            fromScrollOffsetSec,
        double            toPxPerSec,
        double            toScrollOffsetSec)
    {
        _viewport            = viewport;
        _fromPxPerSec        = fromPxPerSec;
        _fromScrollOffsetSec = fromScrollOffsetSec;
        _toPxPerSec          = toPxPerSec;
        _toScrollOffsetSec   = toScrollOffsetSec;
    }

    /// <summary>
    /// Apply the "to" viewport snapshot. Used by the history manager
    /// during redo; the live scroll handler in TrackListView mutates
    /// the viewport directly for responsiveness and only pushes the
    /// action after the burst settles, so the action's Execute is
    /// effectively a no-op on the initial push (the viewport is
    /// already at "to") but matters on every subsequent redo. When
    /// side effects are added, this method is also where they fire —
    /// dispatch each entry from <see cref="SideEffects"/> after the
    /// primary state mutation, so the order of operations is
    /// deterministic at undo time.
    /// </summary>
    public override void Execute(object? data = null)
    {
        // Order doesn't matter for these two — they don't interact in
        // the viewport's setters, and TimelineItemsPanel reflows after
        // both writes settle via PropertyChanged.
        _viewport.PxPerSec        = _toPxPerSec;
        _viewport.ScrollOffsetSec = _toScrollOffsetSec;

        // Side effects, when populated, run here. Today the array is
        // empty so the loop is a no-op — kept in place so the contract
        // doesn't change shape when the first effect is wired up.
        foreach (var effect in SideEffects)
            effect.Execute();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a ViewportChangeAction with from/to swapped. The history
    /// manager executes it (viewport reverts), then pushes it onto the
    /// redo stack. The same instance, reversed again, produces a
    /// forward action with the original direction — round-trippable.
    ///
    /// When side effects are added, the inverse needs to know how to
    /// undo each effect alongside the viewport revert. The simplest
    /// pattern is: each effect that's also IReversible contributes its
    /// Reverse() to the inverse's SideEffects, so undo applies them in
    /// the same execution path. Effects that aren't reversible become
    /// fire-and-forget: the carrier did them on the way out, undo
    /// doesn't try to invent inverses.
    /// </remarks>
    public IAction Reverse(IReversible? previous, params object[] args) =>
        new ViewportChangeAction(_viewport,
            _toPxPerSec, _toScrollOffsetSec,
            _fromPxPerSec, _fromScrollOffsetSec);
}
