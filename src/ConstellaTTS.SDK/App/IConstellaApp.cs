namespace ConstellaTTS.SDK.App;

/// <summary>
/// The running application — the host produced by
/// <c>ConstellaModuleRegistry.Build()</c>. Holds the service provider
/// the modules wired up and exposes the most-touched cross-cutting
/// managers as <see cref="Lazy{T}"/> handles so dependents can take
/// an <see cref="IConstellaApp"/> reference and reach the rest of
/// the graph without forcing eager construction.
///
/// <para>
/// <b>Why <see cref="Lazy{T}"/> on the interface.</b> Several core
/// managers cross-reference each other — the history manager calls
/// into the navigation manager when reversing a navigation request,
/// the navigation manager pushes onto the history manager when
/// applying a recorded transition. Resolving everything eagerly
/// during construction closes a cycle the container can't satisfy.
/// Threading <see cref="Lazy{T}"/> through here defers the
/// resolution to first use, by which point all registrations have
/// finished. Surfacing the laziness in the contract (rather than
/// hiding it behind plain getters) makes the deferred resolution
/// part of the type — anyone reading <see cref="IConstellaApp"/>
/// sees, in the signature, that reaching a manager is a
/// <c>.Value</c> away.
/// </para>
///
/// <para>
/// <b>Why <see cref="Services"/> is here.</b> The host is the
/// authoritative handle on the running app, and bootstrap code
/// occasionally needs to resolve services the facade doesn't bother
/// surfacing on its own (one-off types like <c>IConstellaBootstrap</c>,
/// logger factories, plugin-supplied singletons). Exposing
/// <see cref="IServiceProvider"/> here keeps that escape hatch on
/// the host instead of leaking it onto the builder; the builder
/// shouldn't outlive <see cref="ConstellaModuleRegistry.Build"/>,
/// the host should.
/// </para>
/// </summary>
public interface IConstellaApp
{

    Lazy<History.IHistoryManager>          HistoryManager    { get; }
    Lazy<UI.Navigation.INavigationManager> NavigationManager { get; }
    Lazy<Theme.IThemeProvider>             ThemeProvider     { get; }
}
