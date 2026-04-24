using ConstellaTTS.Core.Logging;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.History.Exceptions;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Navigation;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.History;

/// <summary>
/// Default <see cref="IHistoryManager"/> — maintains symmetric undo and
/// redo stacks. The symmetry relies on reversible actions whose
/// <c>Reverse()</c> itself returns a reversible action (e.g.
/// <c>CreateBlockAction</c> ⇄ <c>RemoveBlockAction</c>); such pairs
/// round-trip indefinitely in both directions.
///
/// Invariants:
///  · <see cref="Push"/> always clears the redo stack (branches are
///    intentionally not supported — standard editor UX).
///  · <see cref="Rollback"/> pushes the inverse action onto the redo
///    stack only if that action is <see cref="IReversible"/>. Otherwise
///    the redo chain breaks for that entry.
///  · <see cref="Redo"/> mirrors the above: it pops from the redo stack,
///    calls <c>Reverse()</c> on the entry to obtain the forward action,
///    executes it, and pushes that forward action back onto the undo
///    stack when reversible.
///  · On exception either stack is restored to its pre-call state.
/// </summary>
public sealed class HistoryManager(
    ILoggerFactory      loggerFactory,
    Lazy<IConstellaApp> app) : IHistoryManager
{
    private readonly Stack<IReversible> _undoStack = new();
    private readonly Stack<IReversible> _redoStack = new();
    private readonly ILogger            _log       = loggerFactory.CreateLogger(LogCategory.WindowProcess);

    private INavigationManager Nav => app.Value.NavigationManager;

    public bool ShowIrreversibleDialog { get; set; } = true;

    public IReadOnlyList<IReversible> Entries     => _undoStack.ToList();
    public IReadOnlyList<IReversible> RedoEntries => _redoStack.ToList();

    public void Push(IReversible entry)
    {
        _undoStack.Push(entry);

        // A new action invalidates any pending redo path. Without this
        // a user could create an unreachable "timeline" by undoing past
        // something then doing something new while stale redo entries
        // point into the abandoned branch.
        var discarded = _redoStack.Count;
        _redoStack.Clear();

        _log.LogInformation(
            "History push: [{Id}] {Name} (undo={UndoDepth}, redoDiscarded={Discarded})",
            entry.Id, entry.Name, _undoStack.Count, discarded);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _log.LogInformation("History cleared (both stacks)");
    }

    // ── Undo ────────────────────────────────────────────────────────────

    public void Rollback(params object[] args)
    {
        if (!_undoStack.TryPop(out var entry))
        {
            _log.LogWarning("Rollback: undo stack is empty");
            return;
        }

        var previous = _undoStack.TryPeek(out var prev) ? prev : null;

        try
        {
            _log.LogInformation("Rollback: [{Id}] {Name} (remaining undo={Depth})",
                entry.Id, entry.Name, _undoStack.Count);

            var inverse = entry.Reverse(previous, args);
            ExecuteInverse(inverse);

            PushToRedoIfReversible(inverse, origin: entry);
        }
        catch (Exception ex)
        {
            _undoStack.Push(entry);
            _log.LogError(ex, "Rollback failed for [{Id}] — undo stack restored", entry.Id);
            throw new RollbackFailedException(entry, ex);
        }
    }

    public void Rollback(IReversible rollbackTo, params object[] args)
    {
        if (!_undoStack.Contains(rollbackTo))
        {
            _log.LogWarning("Rollback: target [{Id}] not found", rollbackTo.Id);
            return;
        }

        // Keep a local trail of reversed entries so we can restore the
        // undo stack if a later step throws mid-iteration.
        var reversedTrail = new Stack<IReversible>();

        // Track redo pushes to roll back on failure too.
        var redoPushCount = 0;

        while (_undoStack.TryPop(out var entry))
        {
            var previous = _undoStack.TryPeek(out var prev) ? prev : null;

            try
            {
                var inverse = entry.Reverse(previous, args);
                ExecuteInverse(inverse);
                reversedTrail.Push(entry);

                if (PushToRedoIfReversible(inverse, origin: entry))
                    redoPushCount++;
            }
            catch (Exception ex)
            {
                // Restore both stacks to a consistent pre-call state.
                _undoStack.Push(entry);
                foreach (var rb in reversedTrail) _undoStack.Push(rb);
                for (int i = 0; i < redoPushCount; i++) _redoStack.Pop();

                _log.LogError(ex, "Rollback failed at [{Id}] — stacks restored", entry.Id);
                throw new RollbackFailedException(entry, ex);
            }

            if (ReferenceEquals(entry, rollbackTo)) break;
        }
    }

    // ── Redo ────────────────────────────────────────────────────────────

    public void Redo(params object[] args)
    {
        if (!_redoStack.TryPop(out var entry))
        {
            _log.LogWarning("Redo: redo stack is empty");
            return;
        }

        // For the `previous` arg we pass the current top of the UNDO
        // stack — that's the most recent history fact, semantically the
        // same position the rollback path passes.
        var previous = _undoStack.TryPeek(out var prev) ? prev : null;

        try
        {
            _log.LogInformation("Redo: [{Id}] {Name} (remaining redo={Depth})",
                entry.Id, entry.Name, _redoStack.Count);

            // The redo entry IS an inverse-of-an-undone-action. Calling
            // Reverse() on it produces the forward action — the thing
            // that restores the state we left when the user hit Ctrl+Z.
            var forward = entry.Reverse(previous, args);
            ExecuteInverse(forward);

            // Extend the undo chain so the same action can be undone again.
            if (forward is IReversible reversibleForward)
            {
                _undoStack.Push(reversibleForward);
            }
            else
            {
                _log.LogWarning(
                    "Redo: forward action [{Id}] is not IReversible — undo chain broken",
                    entry.Id);
            }
        }
        catch (Exception ex)
        {
            _redoStack.Push(entry);
            _log.LogError(ex, "Redo failed for [{Id}] — redo stack restored", entry.Id);
            throw new RedoFailedException(entry, ex);
        }
    }

    // ── Shared helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Dispatch helper: NavigationRequests need to go through the nav
    /// manager's ApplyOnly path (no history recording), everything else
    /// runs through plain IAction.Execute.
    /// </summary>
    private void ExecuteInverse(IAction action)
    {
        if (action is NavigationRequest navRequest)
            Nav.ApplyOnly(navRequest);
        else
            action.Execute();
    }

    /// <summary>
    /// Push <paramref name="inverse"/> onto the redo stack if it's reversible.
    /// Returns whether a push happened, so batch operations can count and
    /// unwind on failure.
    /// </summary>
    private bool PushToRedoIfReversible(IAction inverse, IReversible origin)
    {
        if (inverse is IReversible reversibleInverse)
        {
            _redoStack.Push(reversibleInverse);
            return true;
        }

        _log.LogWarning(
            "Rollback: inverse of [{Id}] is not IReversible — redo chain broken",
            origin.Id);
        return false;
    }
}
