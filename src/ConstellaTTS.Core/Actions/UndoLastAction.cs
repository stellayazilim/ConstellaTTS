using ConstellaTTS.SDK.Exceptions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.History.Exceptions;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Keybinds;

namespace ConstellaTTS.Core.Actions;

public sealed class UndoLastAction : ActionBase, IBindable
{
    private readonly IHistoryManager   _history;
    private readonly IExceptionHandler _exceptions;

    public override string    Id          => "UndoLastAction";
    public override string    Name        => "Geri Al";
    public override string?   Description => "Son işlemi geri alır.";
    public          KeyCombo[] Bindings   { get; set; } = [KeyMap.Ctrl | KeyMap.Z];

    public UndoLastAction(IHistoryManager history, IExceptionHandler exceptions)
    {
        _history    = history;
        _exceptions = exceptions;
    }

    public override bool CanExecute(object? parameter) =>
        _history.Entries.Count > 0;

    public override void Execute(object? data = null)
    {
        try
        {
            _history.Rollback();
        }
        catch (RollbackFailedException ex)
        {
            _exceptions.Handle(new UndoFailedException(ex));
        }
        finally
        {
            RaiseCanExecuteChanged();
        }
    }
}
