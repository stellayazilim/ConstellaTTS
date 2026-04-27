namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Base contract for all actions. MVVM-compatible via ICommand.
/// Optionally implement IBindable for keyboard shortcuts.
/// Optionally implement IReversible for undo support.
/// Optionally implement IIrreversible for confirmation dialog.
///
/// <para>
/// <b>Payload shape.</b> The runtime entry point is the
/// <c>object?</c> overload — that's what <see cref="System.Windows.Input.ICommand"/>
/// can call into and what every existing implementation already
/// overrides. Callers that want to pass several values bundle them
/// into a record and hand the record over as the single payload;
/// the receiving action does the unpack. This keeps the contract
/// narrow (one shape, one cast site per handler) without giving up
/// the freedom to forward arbitrary state.
/// </para>
///
/// <para>
/// <b>Generic convenience overload.</b> The <see cref="Execute{TParam}(TParam)"/>
/// default interface method is sugar — it boxes the typed argument
/// and forwards to <see cref="Execute(object?)"/>, so call sites that
/// happen to have a strongly-typed payload don't have to write
/// <c>(object?)</c> casts at every invocation. It is not an
/// override point; concrete actions implement <see cref="Execute(object?)"/>
/// only.
/// </para>
/// </summary>
public interface IAction : System.Windows.Input.ICommand
{
    string  Id          { get; }
    string  Name        { get; }
    string? Description { get; }

    new void Execute(object? data = null);

    /// <summary>
    /// Typed convenience wrapper around <see cref="Execute(object?)"/>.
    /// Forwards the argument as-is; the boxing is the same one the
    /// runtime would do anyway when binding to the <c>object?</c>
    /// overload, so there's no extra cost beyond the method dispatch.
    /// Provided as a default interface method so every implementation
    /// inherits it without writing the forwarder by hand.
    /// </summary>
    void Execute<TParam>(TParam param) => Execute((object?)param);
}
