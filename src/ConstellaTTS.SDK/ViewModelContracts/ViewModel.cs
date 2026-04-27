using CommunityToolkit.Mvvm.ComponentModel;

namespace ConstellaTTS.SDK.ViewModelContracts;

/// <summary>
/// Common base for application view-models. Inherits the source-
/// generated change-notification plumbing from
/// <see cref="ObservableObject"/> and adds the navigation parameter
/// hook every window-bound VM can opt into.
///
/// <para>
/// <b>The parameter hook.</b> When a navigation request opens a
/// window, the navigation manager resolves the window's DataContext
/// and calls <see cref="GetParams"/> with whatever payload the
/// caller attached to the request. The base implementation raises a
/// blanket <see cref="INotifyPropertyChanged.PropertyChanged"/> with
/// a null property name — the WPF/Avalonia convention for "every
/// property may have changed, re-evaluate all bindings". VMs that
/// receive a payload override <see cref="GetParams"/>, set their
/// (often non-observable) seed properties first, then call
/// <c>base.GetParams(data)</c> at the end of the override to flush
/// every dependent binding in one go.
/// </para>
///
/// <para>
/// <b>Why blanket-notify.</b> The properties the navigation payload
/// fills in are usually plain getters (<c>IConstellaProject</c>
/// references, configuration records) the VM owner never plans to
/// mutate after the initial hand-off. Promoting each of them to
/// <see cref="ObservableProperty"/> just to fire one notification at
/// open time is overkill — observable infrastructure is meant for
/// fields that genuinely change throughout a window's lifetime.
/// Blanket-notify gives the same first-render correctness without
/// the per-property attribute noise; cost is one extra pass over
/// already-bound elements during a transition the user is waiting
/// on anyway.
/// </para>
///
/// <para>
/// <b>Why a base class instead of an interface.</b> An interface
/// would let any VM opt in without a hierarchy change, but it also
/// means the navigation manager has to type-test every DataContext
/// before calling the hook. A virtual on the shared base puts the
/// dispatch in one place: every window-bound VM derives from
/// <see cref="ViewModel"/>, the manager calls <see cref="GetParams"/>
/// unconditionally, and VMs that haven't overridden it pay the cost
/// of one no-op virtual call plus the blanket notify. Cheaper to
/// read at the call site than the type-test alternative.
/// </para>
/// </summary>
public partial class ViewModel : ObservableObject
{
    /// <summary>
    /// Hook for navigation-supplied parameters. Called by the
    /// navigation manager after the window is constructed and the
    /// DataContext is bound, but before <c>Show()</c>, so the first
    /// pass of binding evaluation already sees the populated state.
    ///
    /// <para>
    /// <b>Override pattern.</b> Derived VMs that care about the
    /// payload override this method, unpack <paramref name="data"/>
    /// into their fields, and call <c>base.GetParams(data)</c> at the
    /// end. The base call raises a blanket PropertyChanged with a
    /// null property name, which Avalonia's binding system treats as
    /// "re-evaluate everything bound to this DataContext" — so even
    /// non-observable seed properties pick up their new values on
    /// first paint. VMs that don't care about the payload simply
    /// don't override; the blanket notify still fires (cheap) but
    /// reads as a no-op since nothing changed.
    /// </para>
    ///
    /// <para>
    /// <b>Payload shape.</b> The argument is whatever the caller
    /// attached to the navigation request — a single domain object
    /// most of the time (an <c>IConstellaProject</c>, an entity id,
    /// a configuration record). Callers that need to forward several
    /// values bundle them into a record and the override does the
    /// unpack. Same convention <see cref="UI.Actions.IAction.Execute"/>
    /// follows.
    /// </para>
    /// </summary>
    /// <param name="data">
    /// Payload supplied by the navigation request. May be <c>null</c>
    /// when the request didn't attach one.
    /// </param>
    public virtual void GetParams(object? data)
    {
        // Blanket PropertyChanged — null/empty propertyName tells
        // Avalonia "every binding on this DataContext may be stale,
        // re-evaluate them all". Inherited so every override can
        // call base.GetParams(data) at the end and have its seed
        // properties pulled into the live binding tree without
        // promoting them to [ObservableProperty].
        OnPropertyChanged(string.Empty);
    }
}
