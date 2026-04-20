namespace ConstellaTTS.SDK;

/// <summary>
/// Represents a reversible operation in the history stack.
/// Each entry is responsible for its own cleanup via <see cref="Rollback"/>.
/// </summary>
public interface IHistoryEntry
{
    string Id   { get; }
    string Name { get; }

    /// <summary>
    /// Rolls back this operation. The entry is responsible for restoring previous state.
    /// </summary>
    void Rollback(params object[] args);
}
