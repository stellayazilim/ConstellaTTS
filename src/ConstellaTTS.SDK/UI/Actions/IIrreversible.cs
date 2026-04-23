namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Standalone contract for irreversible operations that require user confirmation.
/// Implement alongside <see cref="IAction"/> to show a confirmation dialog before execution.
/// On confirm: action executes and history stack is cleared.
/// On cancel:  action does not execute.
///
/// Example: public sealed class DeleteProjectAction : ActionBase, IIrreversible
/// </summary>
public interface IIrreversible
{
    Func<Task<(bool Confirmed, bool ShowDialogAlways)>> ConfirmationDialog { get; }
}
