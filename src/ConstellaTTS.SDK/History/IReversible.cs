using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.SDK.History;

/// <summary>
/// Standalone contract for reversible operations.
/// Reverse() returns a new IAction representing the inverse operation — caller executes it.
/// </summary>
public interface IReversible
{
    string  Id   { get; }
    string  Name { get; }

    IAction Reverse(IReversible? previous, params object[] args);
}
