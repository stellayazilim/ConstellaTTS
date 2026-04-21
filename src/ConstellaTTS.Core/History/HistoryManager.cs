using ConstellaTTS.SDK.History;

namespace ConstellaTTS.Core.History;

public sealed class HistoryManager : IHistoryManager
{
    private readonly Stack<IHistoryEntry> _stack = new();

    /// <inheritdoc/>
    public IReadOnlyList<IHistoryEntry> Entries => _stack.ToList();

    /// <inheritdoc/>
    public void Push(IHistoryEntry entry) => _stack.Push(entry);

    /// <inheritdoc/>
    public void Rollback(params object[] args)
    {
        if (_stack.TryPop(out var entry))
            entry.Rollback(args);
    }
}
