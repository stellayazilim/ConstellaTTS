using ConstellaTTS.SDK.Exceptions;

namespace ConstellaTTS.SDK.History.Exceptions;

/// <summary>Base for all history-related exceptions.</summary>
public abstract class HistoryException : ConstellaException
{
    protected HistoryException(string message) : base(message) { }
    protected HistoryException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a rollback operation fails.
/// The entry that failed is available via <see cref="Entry"/>.
/// The stack has been restored to its pre-rollback state.
/// </summary>
public sealed class RollbackFailedException : HistoryException
{
    public IReversible Entry { get; }

    public RollbackFailedException(IReversible entry, Exception inner)
        : base($"Rollback failed for [{entry.Id}] '{entry.Name}': {inner.Message}", inner)
    {
        Entry = entry;
    }
}

/// <summary>
/// Thrown when an undo action fails at the UI/action layer.
/// Wraps <see cref="RollbackFailedException"/> with a user-facing message.
/// </summary>
public sealed class UndoFailedException : HistoryException
{
    public IReversible Entry { get; }

    public UndoFailedException(RollbackFailedException inner)
        : base($"'{inner.Entry.Name}' geri alınamadı.", inner)
    {
        Entry = inner.Entry;
    }
}

/// <summary>
/// Thrown when a redo operation fails at the history-manager layer. The
/// entry that failed is available via <see cref="Entry"/>. Both stacks
/// have been restored to their pre-redo state.
/// </summary>
public sealed class RedoFailedException : HistoryException
{
    public IReversible Entry { get; }

    public RedoFailedException(IReversible entry, Exception inner)
        : base($"Redo failed for [{entry.Id}] '{entry.Name}': {inner.Message}", inner)
    {
        Entry = entry;
    }
}

/// <summary>
/// Thrown when a redo action fails at the UI/action layer.
/// Wraps <see cref="RedoFailedException"/> with a user-facing message.
/// </summary>
public sealed class RedoActionFailedException : HistoryException
{
    public IReversible Entry { get; }

    public RedoActionFailedException(RedoFailedException inner)
        : base($"'{inner.Entry.Name}' yeniden uygulanamadı.", inner)
    {
        Entry = inner.Entry;
    }
}
