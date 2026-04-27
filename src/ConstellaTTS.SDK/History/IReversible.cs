using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.SDK.History;

/// <summary>
/// Standalone contract for reversible operations.
/// Reverse() returns a new IAction representing the inverse operation — caller executes it.
///
/// <para>
/// <b>Payload shape mirrors <see cref="IAction"/>.</b> The same single
/// <c>object?</c> the action contract uses for forward execution flows
/// through here for the inverse path. Callers that need to forward
/// several values bundle them into a record on the way in; the
/// receiving implementation does the unpack. Keeping the two shapes
/// identical means an action and its inverse share the same call
/// convention — no second mental model.
/// </para>
/// </summary>
public interface IReversible
{
    string  Id   { get; }
    string  Name { get; }

    IAction Reverse(IReversible? previous, object? data = null);
}
