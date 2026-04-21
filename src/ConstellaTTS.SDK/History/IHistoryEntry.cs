namespace ConstellaTTS.SDK.History;

/// <summary>
/// A reversible operation stored in the history stack.
/// Each entry is responsible for restoring its own previous state on rollback.
/// </summary>
public interface IHistoryEntry
{
    /// <summary>Unique identifier for this entry.</summary>
    string Id { get; }

    /// <summary>Human-readable description of the operation.</summary>
    string Name { get; }

    /// <summary>Reverts the operation. The entry is responsible for restoring previous state.</summary>
    void Rollback(params object[] args);
}
