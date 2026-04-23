
namespace ConstellaTTS.SDK.History;

/// <summary>
/// Manages the executed action history stack.
/// Only <see cref="IReversible"/> entries are accepted.
/// Irreversible actions are handled by <see cref="ConstellaTTS.SDK.UI.Actions.IActionManager"/>.
/// </summary>
public interface IHistoryManager
{
    IReadOnlyList<IReversible> Entries { get; }

    /// <summary>
    /// When true, ActionManager shows a confirmation dialog before executing
    /// irreversible actions. Set to false in tests or headless environments.
    /// Default: true.
    /// </summary>
    bool ShowIrreversibleDialog { get; set; }

    /// <summary>Push a reversible entry onto the history stack.</summary>
    void Push(IReversible entry);

    /// <summary>Pop the most recent entry and call Reverse.</summary>
    void Rollback(params object[] args);

    /// <summary>
    /// Pop and reverse all entries down to and including <paramref name="rollbackTo"/>.
    /// No-op if not found.
    /// </summary>
    void Rollback(IReversible rollbackTo, params object[] args);

    /// <summary>Clear all history entries.</summary>
    void Clear();
}
