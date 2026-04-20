using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.SDK;

/// <summary>
/// Represents a pluggable module. Each module registers its own services
/// and declares its assembly dependencies for topological load ordering.
/// </summary>
public interface IConstellaModule
{
    string Id   { get; }
    string Name { get; }

    /// <summary>
    /// Assemblies this module depends on. Used for topological sort during initialization.
    /// If your module depends on another module's types, you already reference its assembly —
    /// declare it here so the registry loads it first.
    /// </summary>
    IReadOnlyList<Assembly> Dependencies { get; }

    /// <summary>
    /// Register services into the DI container.
    /// </summary>
    void Build(IServiceCollection services);
}
