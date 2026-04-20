namespace ConstellaTTS.SDK;

public interface IHistoryManager
{
    IReadOnlyList<IHistoryEntry> Entries { get; }

    /// <summary>
    /// Pushes a new entry onto the history stack.
    /// </summary>
    void Push(IHistoryEntry entry);

    /// <summary>
    /// Pops the last entry and calls its Rollback.
    /// </summary>
    void Rollback(params object[] args);
}