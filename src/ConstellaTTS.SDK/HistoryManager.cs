namespace ConstellaTTS.SDK;

public sealed class HistoryManager : IHistoryManager
{
    private readonly Stack<IHistoryEntry> _stack = new();

    public IReadOnlyList<IHistoryEntry> Entries => _stack.ToList();

    public void Push(IHistoryEntry entry) => _stack.Push(entry);

    public void Rollback(params object[] args)
    {
        if (_stack.TryPop(out var entry))
            entry.Rollback(args);
    }
}