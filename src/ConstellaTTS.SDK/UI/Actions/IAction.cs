namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Base contract for all actions. MVVM-compatible via ICommand.
/// Optionally implement IBindable for keyboard shortcuts.
/// Optionally implement IReversible for undo support.
/// Optionally implement IIrreversible for confirmation dialog.
/// </summary>
public interface IAction : System.Windows.Input.ICommand
{
    string  Id          { get; }
    string  Name        { get; }
    string? Description { get; }

    new void Execute(object? data = null);
}
