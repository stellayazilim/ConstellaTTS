namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Result of an async action invoked through the sync
/// <see cref="System.Windows.Input.ICommand"/> entry point. Carries
/// either the failure reason or a clean signal that the work is
/// done, so subscribers (status bars, toast hosts, view-models that
/// refresh their data) can react without holding the Task that
/// <see cref="AsyncActionBase.Execute"/> intentionally drops.
/// </summary>
public sealed record AsyncActionResult(IAction Action, bool Success, Exception? Error);

/// <summary>
/// Base class for actions whose primary work is asynchronous.
/// Subclasses override <see cref="ExecuteAsync"/>; the sync
/// <see cref="Execute"/> is sealed and routes through the same async
/// path with a continuation that publishes
/// <see cref="AsyncExecutionCompleted"/> when the task settles.
///
/// <para>
/// <b>Sync entry point is fire-and-forget on purpose.</b> ICommand's
/// signature is void — there's no way to make XAML bindings or
/// keybind dispatchers wait. Awaiting on a sync stack from a UI
/// thread (<c>GetAwaiter().GetResult()</c>) is the worse alternative:
/// it deadlocks under Avalonia's dispatcher when the awaited task
/// also wants the UI thread. Fire-and-forget plus a completion
/// event is the canonical bridge — the work runs on the thread pool,
/// the UI gets notified through the event when it's done.
/// </para>
///
/// <para>
/// <b>Subscribe once at construction, not per-Execute.</b> A common
/// trap is wiring up <see cref="AsyncExecutionCompleted"/> inside an
/// <c>Execute</c> override or call site. That attaches a fresh
/// handler every time the action runs; handlers accumulate, the same
/// notification fires N times, and the action's lifetime starts
/// pinning the subscriber for as long as both live. Subscribers
/// (typically a view-model in its constructor) hook the event once
/// against the action instance they hold and unhook on dispose.
/// </para>
///
/// <para>
/// <b>Exceptions are observed, not swallowed.</b> The continuation
/// catches everything the awaited task throws and forwards it on the
/// event as <see cref="AsyncActionResult.Error"/>. Without this,
/// fire-and-forget tasks raise <c>UnobservedTaskException</c> on
/// finalization — late, indirect, easy to miss in logs.
/// </para>
/// </summary>
public abstract class AsyncActionBase : ActionBase, IAsyncAction
{
    public abstract Task ExecuteAsync(object? data = null);

    public sealed override void Execute(object? data = null)
    {
        _ = RunAndNotifyAsync(data);
    }

    private async Task RunAndNotifyAsync(object? data)
    {
        try
        {
            await ExecuteAsync(data).ConfigureAwait(false);
            AsyncExecutionCompleted?.Invoke(
                this, new AsyncActionResult(this, Success: true, Error: null));
        }
        catch (Exception ex)
        {
            AsyncExecutionCompleted?.Invoke(
                this, new AsyncActionResult(this, Success: false, Error: ex));
        }
    }

    /// <summary>
    /// Raised after the async work started by <see cref="Execute"/>
    /// settles, regardless of outcome. Inspect
    /// <see cref="AsyncActionResult.Success"/> /
    /// <see cref="AsyncActionResult.Error"/> on the payload to
    /// branch.
    ///
    /// <para>
    /// Not raised when callers go through <see cref="ExecuteAsync"/>
    /// directly — there the Task itself is the completion signal,
    /// and a duplicate event-based notification would just be noise
    /// for callers who already know how to await.
    /// </para>
    /// </summary>
    public event EventHandler<AsyncActionResult>? AsyncExecutionCompleted;
}
