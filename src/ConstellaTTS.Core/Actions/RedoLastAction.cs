using ConstellaTTS.SDK.Exceptions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.History.Exceptions;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Keybinds;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Keybind-bound redo action — the forward counterpart to
/// <see cref="UndoLastAction"/>. Ctrl+Y pops the most recent redo entry,
/// executes the forward action it produces, and pushes that forward
/// action back onto the undo stack.
///
/// Disabled when the redo stack is empty, so keybind-bound buttons can
/// bind <see cref="System.Windows.Input.ICommand.CanExecute"/> to grey
/// themselves out.
/// </summary>
public sealed class RedoLastAction : ActionBase, IBindable
{
    private readonly IHistoryManager   _history;
    private readonly IExceptionHandler _exceptions;

    public override string    Id          => "RedoLastAction";
    public override string    Name        => "Yinele";
    public override string?   Description => "Son geri alınan işlemi yeniden uygular.";
    public          KeyCombo[] Bindings   { get; set; } = [KeyMap.Ctrl | KeyMap.Y];

    public RedoLastAction(IHistoryManager history, IExceptionHandler exceptions)
    {
        _history    = history;
        _exceptions = exceptions;
    }

    public override bool CanExecute(object? parameter) =>
        _history.RedoEntries.Count > 0;

    public override void Execute(object? data = null)
    {
        try
        {
            _history.Redo();
        }
        catch (RedoFailedException ex)
        {
            _exceptions.Handle(new RedoActionFailedException(ex));
        }
        finally
        {
            RaiseCanExecuteChanged();
        }
    }
}
