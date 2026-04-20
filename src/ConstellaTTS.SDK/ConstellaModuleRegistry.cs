using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.SDK;

/// <summary>
/// Collects and initializes all modules in dependency order.
/// </summary>
public sealed class ConstellaModuleRegistry
{
    private readonly List<IConstellaModule> _modules = [];

    /// <summary>
    /// Registers a module manually — for built-in/hardcoded modules.
    /// </summary>
    public ConstellaModuleRegistry Register(IConstellaModule module)
    {
        _modules.Add(module);
        return this;
    }

    /// <summary>
    /// Scans a directory for assemblies, discovers IConstellaModule implementations
    /// and registers them automatically.
    /// </summary>
    public ConstellaModuleRegistry LoadPlugins(string directory)
    {
        if (!Directory.Exists(directory)) return this;

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            var assembly = Assembly.LoadFrom(dll);
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(IConstellaModule).IsAssignableFrom(t)
                         && !t.IsInterface
                         && !t.IsAbstract);

            foreach (var type in moduleTypes)
            {
                var module = (IConstellaModule)Activator.CreateInstance(type)!;
                Register(module);
            }
        }

        return this;
    }

    /// <summary>
    /// Topologically sorts modules by assembly dependencies,
    /// calls Build() on each, and returns the built IServiceProvider.
    /// </summary>
    public IServiceProvider Initialize()
    {
        var sorted   = TopologicalSort(_modules);
        var services = new ServiceCollection();

        foreach (var module in sorted)
            module.Build(services);

        return services.BuildServiceProvider();
    }

    private static IEnumerable<IConstellaModule> TopologicalSort(
        IEnumerable<IConstellaModule> modules)
    {
        var list       = modules.ToList();
        var assemblyMap = list.ToDictionary(m => m.GetType().Assembly);
        var visited    = new HashSet<Assembly>();
        var result     = new List<IConstellaModule>();

        void Visit(IConstellaModule module)
        {
            var assembly = module.GetType().Assembly;
            if (!visited.Add(assembly)) return;

            foreach (var dep in module.Dependencies)
            {
                if (assemblyMap.TryGetValue(dep, out var depModule))
                    Visit(depModule);
            }

            result.Add(module);
        }

        foreach (var module in list)
            Visit(module);

        return result;
    }
}
