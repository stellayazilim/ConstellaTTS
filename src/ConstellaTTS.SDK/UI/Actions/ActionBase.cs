namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Base class for all actions. Handles ICommand boilerplate.
/// Add IBindable to support keyboard shortcuts.
/// Add IReversible to support undo.
/// Add IIrreversible to require confirmation before execute.
/// </summary>
public abstract class ActionBase : IAction
{
    public abstract string  Id          { get; }
    public abstract string  Name        { get; }
    public virtual  string? Description => null;

    public abstract void Execute(object? data = null);

    public virtual bool CanExecute(object? parameter) => true;
    void System.Windows.Input.ICommand.Execute(object? parameter) => Execute(parameter);
    public event EventHandler? CanExecuteChanged;
    protected void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
