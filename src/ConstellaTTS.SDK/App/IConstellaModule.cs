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
}
