
namespace ConstellaTTS.SDK.History;

/// <summary>
/// Manages the executed action history stack and its redo counterpart.
/// Only <see cref="IReversible"/> entries are accepted on the undo stack.
/// Irreversible actions are handled by <see cref="ConstellaTTS.SDK.UI.Actions.IActionManager"/>.
///
/// <para>
/// <b>Undo/redo invariant.</b> Entries stored on either stack must be
/// reversible so the round-trip can continue in both directions. When an
/// undo operation's returned inverse action is itself <see cref="IReversible"/>
/// (the common case — see <c>CreateBlockAction</c> ⇄ <c>RemoveBlockAction</c>),
/// the manager automatically pushes it onto the redo stack. If the inverse
/// isn't reversible, the redo chain is broken for that entry.
/// </para>
///
/// <para>
/// <b>Redo-chain invalidation.</b> Every successful <see cref="Push"/>
/// clears the redo stack — a new action taken after undos would create
/// a branch, and branches aren't supported. This mirrors the behaviour
/// of virtually every editor and IDE.
/// </para>
/// </summary>
public interface IHistoryManager
{
    /// <summary>Entries currently on the undo stack, most-recent first.</summary>
    IReadOnlyList<IReversible> Entries { get; }

    /// <summary>Entries currently on the redo stack, most-recent first.</summary>
    IReadOnlyList<IReversible> RedoEntries { get; }

    /// <summary>
    /// When true, ActionManager shows a confirmation dialog before executing
    /// irreversible actions. Set to false in tests or headless environments.
    /// Default: true.
    /// </summary>
    bool ShowIrreversibleDialog { get; set; }

    /// <summary>
    /// Push a reversible entry onto the undo stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Redo-stack invalidation.</b> Every successful Push clears the redo
    /// stack entirely. After a sequence of Ctrl+Z's, any new action taken by
    /// the user is treated as a deliberate branch — the previously-undone
    /// operations are no longer reachable via Ctrl+Y and their entries are
    /// discarded. This is the standard editor behaviour (VS Code, Photoshop,
    /// Word, IntelliJ) because the alternative — preserving redo entries
    /// whose VM references now point into an abandoned branch — produces
    /// state corruption, not a useful "non-linear undo" feature.
    /// </para>
    /// <para>
    /// Discarded redo entries are NOT treated as <see cref="ConstellaTTS.SDK.UI.Actions.IIrreversible"/>.
    /// No confirmation dialog fires. The distinction matters:
    /// <see cref="ConstellaTTS.SDK.UI.Actions.IIrreversible"/> is for operations
    /// whose EFFECT cannot be undone (deleting a file from disk, dropping a
    /// database table — the real world has changed). Redo discard only loses
    /// <i>potential</i> state the user could still reach by re-doing the
    /// operation manually; nothing irrecoverable happens. Treating this as
    /// irreversible would produce a dialog on every post-undo action, which
    /// desensitises users to real irreversibility warnings.
    /// </para>
    /// <para>
    /// The number of discarded entries is surfaced via the structured
    /// <c>redoDiscarded</c> log field on the push-log line, so a user asking
    /// "why didn't Ctrl+Y work?" can be answered from the log without guessing.
    /// </para>
    /// </remarks>
    void Push(IReversible entry);

    /// <summary>
    /// Pop the most recent undo entry, call <see cref="IReversible.Reverse"/>,
    /// execute the returned action, and push that action onto the redo stack
    /// when it is itself <see cref="IReversible"/>.
    /// </summary>
    void Rollback(params object[] args);

    /// <summary>
    /// Pop and reverse all entries down to and including <paramref name="rollbackTo"/>.
    /// Each reversed entry's inverse is pushed onto the redo stack if reversible,
    /// so an unbounded Ctrl+Y chain can walk forward through the same path.
    /// No-op if <paramref name="rollbackTo"/> is not present.
    /// </summary>
    void Rollback(IReversible rollbackTo, params object[] args);

    /// <summary>
    /// Pop the most recent redo entry, call <see cref="IReversible.Reverse"/>
    /// to obtain the forward action, execute it, and push that forward action
    /// back onto the undo stack when it is itself reversible.
    /// </summary>
    void Redo(params object[] args);

    /// <summary>Clear both undo and redo stacks.</summary>
    void Clear();
}
