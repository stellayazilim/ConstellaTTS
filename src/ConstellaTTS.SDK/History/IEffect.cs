namespace ConstellaTTS.SDK.History;

/// <summary>
/// Optional contract for actions that produce side effects — extra
/// <see cref="UI.Actions.IAction"/> instances that run as part of the
/// same logical operation. The carrier is the "outer" action: it
/// performs the primary work, and the items in <see cref="SideEffects"/>
/// are run alongside (typically by the action's own Execute) so the
/// caller doesn't have to know about them.
///
/// <para>
/// <b>Why this is on the action, not on the history manager.</b>
/// "What happens together" is part of the action's semantics — only
/// the author of <c>ScrollAction</c> knows that scrolling far enough
/// might collapse an open editor. The history manager has no business
/// inspecting one entry's relationship to another; it just executes,
/// records, and reverses individual entries. Pushing this concern down
/// onto the action keeps the history layer dumb and composable.
/// </para>
///
/// <para>
/// <b>Reversibility is the carrier's job.</b> Whether the side effects
/// can be undone is a question only the carrier can answer — it knows
/// what it dispatched and how to invert each piece. A carrier that
/// implements <see cref="IReversible"/> takes responsibility for
/// undoing its own side effects in <see cref="IReversible.Reverse"/>;
/// a non-reversible carrier just lets them happen. Side effects are
/// not separately pushed onto the history stack — the carrier is the
/// single recorded entry, and one Ctrl+Z restores everything that
/// went out together.
/// </para>
///
/// <para>
/// <b>Typical shape.</b> The carrier's <see cref="UI.Actions.IAction.Execute"/>
/// runs its own work, then iterates <see cref="SideEffects"/> and
/// invokes each one. <see cref="IReversible.Reverse"/> returns a new
/// reversible whose Execute performs the inverse primary work AND the
/// inverse of each side effect, so a single round-trip restores the
/// composite state.
/// </para>
/// </summary>
public interface IEffect
{
    /// <summary>
    /// Auxiliary actions that run as part of this action's execution.
    /// Exposed so the carrier and tests can inspect what was dispatched;
    /// the history manager itself doesn't read this property — it only
    /// cares about <see cref="IReversible"/>. Implementations may return
    /// an empty array when the action ran in isolation; callers should
    /// not pre-suppose nullability beyond an empty collection.
    /// </summary>
    UI.Actions.IAction[] SideEffects { get; }
}
