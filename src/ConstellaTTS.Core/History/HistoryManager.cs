using ConstellaTTS.Core.Logging;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.History.Exceptions;
using ConstellaTTS.SDK.UI.Navigation;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.History;

public sealed class HistoryManager(
    ILoggerFactory      loggerFactory,
    Lazy<IConstellaApp> app) : IHistoryManager
{
    private readonly Stack<IReversible> _stack = new();
    private readonly ILogger            _log   = loggerFactory.CreateLogger(LogCategory.WindowProcess);

    private INavigationManager Nav => app.Value.NavigationManager;

    public bool ShowIrreversibleDialog { get; set; } = true;

    public IReadOnlyList<IReversible> Entries => _stack.ToList();

    public void Push(IReversible entry)
    {
        _stack.Push(entry);
        _log.LogInformation("History push: [{Id}] {Name} (depth={Depth})",
            entry.Id, entry.Name, _stack.Count);
    }

    public void Clear()
    {
        _stack.Clear();
        _log.LogInformation("History cleared");
    }

    public void Rollback(params object[] args)
    {
        if (!_stack.TryPop(out var entry))
        {
            _log.LogWarning("Rollback: stack is empty");
            return;
        }

        var previous = _stack.TryPeek(out var prev) ? prev : null;

        try
        {
            _log.LogInformation("Rollback: [{Id}] {Name} (remaining={Depth})",
                entry.Id, entry.Name, _stack.Count);

            var inverse = entry.Reverse(previous, args);

            if (inverse is NavigationRequest navRequest)
                Nav.ApplyOnly(navRequest);
            else
                inverse.Execute();
        }
        catch (Exception ex)
        {
            _stack.Push(entry);
            _log.LogError(ex, "Rollback failed for [{Id}] — re-pushed", entry.Id);
            throw new RollbackFailedException(entry, ex);
        }
    }

    public void Rollback(IReversible rollbackTo, params object[] args)
    {
        if (!_stack.Contains(rollbackTo))
        {
            _log.LogWarning("Rollback: target [{Id}] not found", rollbackTo.Id);
            return;
        }

        var reversed = new Stack<IReversible>();

        while (_stack.TryPop(out var entry))
        {
            var previous = _stack.TryPeek(out var prev) ? prev : null;

            try
            {
                var inverse = entry.Reverse(previous, args);

                if (inverse is NavigationRequest navRequest)
                    Nav.ApplyOnly(navRequest);
                else
                    inverse.Execute();

                reversed.Push(entry);
            }
            catch (Exception ex)
            {
                _stack.Push(entry);
                foreach (var rb in reversed) _stack.Push(rb);
                _log.LogError(ex, "Rollback failed at [{Id}] — stack restored", entry.Id);
                throw new RollbackFailedException(entry, ex);
            }

            if (ReferenceEquals(entry, rollbackTo)) break;
        }
    }
}
