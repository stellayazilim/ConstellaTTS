namespace ConstellaTTS.SDK.History;

/// <summary>
/// Manages a stack of reversible operations.
/// Push entries after performing an action; call Rollback to undo the most recent one.
/// </summary>
public interface IHistoryManager
{
    /// <summary>All entries currently on the history stack, most recent last.</summary>
    IReadOnlyList<IHistoryEntry> Entries { get; }

    /// <summary>Pushes a new entry onto the history stack.</summary>
    void Push(IHistoryEntry entry);

    /// <summary>Pops the most recent entry and calls its Rollback.</summary>
    void Rollback(params object[] args);
}
