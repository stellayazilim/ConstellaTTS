using System.Reflection;
using ConstellaTTS.SDK.App;
using Microsoft.Extensions.DependencyInjection;
namespace ConstellaTTS.Core;


/// <summary>
/// Collects modules and initializes them in topological dependency order.
/// Only used at application startup — plugins have no dependency on this type.
/// </summary>
public sealed class ConstellaModuleRegistry
{
    private readonly List<IConstellaModule> _modules = [];

    public ConstellaModuleRegistry Register(IConstellaModule module)
    {
        _modules.Add(module);
        return this;
    }

    public ConstellaModuleRegistry LoadPlugins(string directory)
    {
        if (!Directory.Exists(directory)) return this;

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            var assembly    = Assembly.LoadFrom(dll);
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
    /// Builds all registered modules in dependency order and returns the initialized app.
    /// Entry point for application startup.
    /// </summary>
    public IConstellaApp Build()
    {
        var sorted   = TopologicalSort(_modules);
        var services = new ServiceCollection();

        foreach (var module in sorted)
            module.Build(services);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IConstellaApp>();
    }

    private static IEnumerable<IConstellaModule> TopologicalSort(IEnumerable<IConstellaModule> modules)
    {
        var list        = modules.ToList();
        var assemblyMap = list.ToDictionary(m => m.GetType().Assembly);
        var visited     = new HashSet<Assembly>();
        var result      = new List<IConstellaModule>();

        void Visit(IConstellaModule module)
        {
            var assembly = module.GetType().Assembly;
            if (!visited.Add(assembly)) return;

            foreach (var dep in module.Dependencies)
                if (assemblyMap.TryGetValue(dep, out var depModule))
                    Visit(depModule);

            result.Add(module);
        }

        foreach (var module in list)
            Visit(module);

        return result;
    }
}
