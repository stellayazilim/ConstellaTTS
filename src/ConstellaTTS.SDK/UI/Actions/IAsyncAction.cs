namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Action whose primary work is asynchronous. Adds an
/// <see cref="ExecuteAsync"/> overload to the base contract so
/// callers that hold a typed reference can <c>await</c> the work
/// directly, while the inherited sync <see cref="IAction.Execute"/>
/// stays compatible with <see cref="System.Windows.Input.ICommand"/>
/// for XAML binding.
///
/// <para>
/// <b>Why both shapes.</b> ICommand only exposes a void
/// <c>Execute</c>. If async actions implemented only that, the work
/// would be fire-and-forget with no way for callers to await
/// completion or observe failures — fine for a button click that
/// just kicks something off, useless for a view-model that needs to
/// update its state once the operation finishes. The async overload
/// is the explicit "I want to wait" path; the sync overload is the
/// "command-binding pipe" path.
/// </para>
/// </summary>
public interface IAsyncAction : IAction
{
    /// <summary>
    /// Asynchronous counterpart of <see cref="IAction.Execute"/>.
    /// Same payload semantics. The Task completes when the work is
    /// done; failures surface as faulted tasks (and, when entered
    /// through <see cref="IAction.Execute"/>, through the
    /// <see cref="AsyncActionBase.AsyncExecutionCompleted"/> event).
    /// </summary>
    Task ExecuteAsync(object? data = null);
}
