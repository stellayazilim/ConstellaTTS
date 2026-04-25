namespace ConstellaTTS.SDK.Timeline;

/// <summary>
/// Records viewport state changes onto the history stack as discrete
/// scroll/zoom sessions, regardless of which input source produced them.
/// Mouse wheel in the track canvas, drag on the minimap, future
/// keyboard pan shortcuts — they all funnel through here, so a single
/// Ctrl+Z always restores the user's previous "where I was looking"
/// state irrespective of how they navigated away.
///
/// <para>
/// <b>Why this isn't built into ITimelineViewport.</b> The viewport
/// itself is a dumb pair of numbers (PxPerSec, ScrollOffsetSec) —
/// promoting it to "the thing that tracks user navigation" would
/// pollute it with debounce timers, history-manager dependencies, and
/// session-state machinery that have nothing to do with projecting
/// time onto pixels. Splitting the recorder out keeps the viewport
/// pure and lets multiple input sources share one debounce policy.
/// </para>
///
/// <para>
/// <b>Session model.</b> A "session" is a contiguous burst of viewport
/// mutations — wheel ticks, drag-frame samples, anything live. Callers
/// don't manually open or close sessions; they call <see cref="Touch"/>
/// each time they're about to mutate the viewport, and the recorder
/// implicitly opens a session on the first Touch and closes it after
/// a quiet period (configurable via the implementation; the default
/// is around 250 ms — short enough not to feel laggy, long enough to
/// merge a normal scroll burst). The closed session pushes a single
/// reversible entry whose FROM is the viewport state at the first
/// Touch and TO is whatever the viewport reads when the timer fires.
/// </para>
///
/// <para>
/// <b>Caller contract.</b> Touch BEFORE mutating the viewport, not
/// after. The recorder snapshots the FROM state on Touch; if the
/// caller mutated first, the FROM would already be the new state and
/// undo would be a no-op. Touching unconditionally on every input
/// event is fine — Touch is cheap and the no-op case (FROM == TO at
/// settle time) is filtered before pushing.
/// </para>
///
/// <para>
/// <b>Flush semantics.</b> <see cref="Flush"/> commits any pending
/// session synchronously. Used at view tear-down so a burst that
/// hasn't reached its quiet threshold yet still produces a history
/// entry rather than getting silently dropped. Idempotent if no
/// session is active.
/// </para>
/// </summary>
public interface IViewportHistoryRecorder
{
    /// <summary>
    /// Signal that a viewport mutation is about to happen. Opens a new
    /// session (capturing the current viewport state as the FROM
    /// anchor) on the first call of a burst; subsequent calls within
    /// the same burst restart the settle timer without disturbing the
    /// anchor. Cheap to call on every input event.
    /// </summary>
    void Touch();

    /// <summary>
    /// Commit any active session immediately, pushing its history
    /// entry. No-op if no session is active. Call from input-source
    /// tear-down paths so a mid-burst detach doesn't drop the entry.
    /// </summary>
    void Flush();
}
