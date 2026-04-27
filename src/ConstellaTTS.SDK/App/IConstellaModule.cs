using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.SDK.App;

/// <summary>
/// Implemented by every pluggable module. Each module registers its own services
/// and declares assembly dependencies for topological load ordering at startup.
/// </summary>
public interface IConstellaModule
{
    /// <summary>Unique module identifier (e.g. "Com.ConstellaTTS.Core").</summary>
    string Id { get; }

    /// <summary>Human-readable module name.</summary>
    string Name { get; }

    /// <summary>
    /// Assemblies this module depends on.
    /// The registry uses these to determine load order — list any assembly whose
    /// types this module references so it is initialized first.
    /// </summary>
    IReadOnlyList<Assembly> Dependencies { get; }

    /// <summary>Registers this module's services into the DI container.</summary>
    void Build(IServiceCollection services);

    /// <summary>
    /// Hook for module wire-up that needs a fully built service
    /// provider. Runs once per module after every <see cref="Build"/>
    /// has finished and the container is available, so a module can
    /// resolve its own services and run side-effects against them —
    /// registering keybinds, attaching flyouts to navigation,
    /// pre-warming caches, that kind of thing.
    ///
    /// <para>
    /// <b>Why not in <see cref="Build"/>.</b> The <see cref="Build"/>
    /// hook only sees an <see cref="IServiceCollection"/>; nothing is
    /// resolvable yet, so any side-effect that needs an actual
    /// service instance has to be smuggled in through a factory
    /// closure ("register a singleton whose factory does the work as
    /// a side-effect of being called"). That pattern works but it
    /// hides the lifecycle in the DI graph: the work runs whenever
    /// the singleton happens to be resolved for the first time,
    /// which can be much later than module startup. Splitting
    /// post-build wiring into its own hook makes the lifecycle
    /// explicit — module Build phase, then post-Build phase, in
    /// dependency order — and keeps factories doing nothing but
    /// constructing their service.
    /// </para>
    ///
    /// <para>
    /// Default implementation is a no-op so existing modules don't
    /// have to opt in.
    /// </para>
    /// </summary>
    void OnBuilt(IServiceProvider services) { }
}
