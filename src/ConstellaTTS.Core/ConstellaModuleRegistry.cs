using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConstellaTTS.Core.Misc.Logging;
using ConstellaTTS.SDK.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core;


/// <summary>
/// Collects modules, runs their <see cref="IConstellaModule.Build"/>
/// hooks in dependency order, and drives the full Avalonia startup
/// sequence (bootstrap + main-window hand-off) in a single fluent
/// call. Only used at application startup — plugins have no
/// dependency on this type.
///
/// <para>
/// <b>The full pipeline lives here.</b> Earlier revisions ended at
/// <c>Build()</c> and left bootstrap orchestration in
/// <c>App.axaml.cs</c>; that meant every host (test harness, future
/// CLI, anything else) had to repeat the same five lines of resolve-
/// bootstrap-set-main-window. Folding the lifecycle into the registry
/// keeps the host's <see cref="OnFrameworkInitializationCompleted"/>
/// down to a single chain — the registry is the lifecycle, not just
/// the DI configuration step.
/// </para>
///
/// <para>
/// <b>Configuration vs. terminal calls.</b> <see cref="Register"/> and
/// <see cref="LoadPlugins"/> return the registry so they can chain.
/// <see cref="BuildAsync"/> is terminal — it runs the modules,
/// resolves and disposes the bootstrap, and hands the active window
/// to Avalonia's classic-desktop lifetime. The Task it returns
/// completes when the app is fully up; awaiting it from
/// <see cref="OnFrameworkInitializationCompleted"/> keeps the
/// override's contract intact.
/// </para>
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
    /// Drives the full startup pipeline:
    ///
    /// <list type="number">
    ///   <item>Topologically sorts the registered modules by inter-module
    ///   dependency.</item>
    ///   <item>Registers <paramref name="application"/> as a DI
    ///   singleton so modules that need theming, windowing, or
    ///   top-level resolution can take a constructor dependency on it
    ///   instead of reaching for <c>Application.Current</c>.</item>
    ///   <item>Runs each module's <see cref="IConstellaModule.Build"/>
    ///   in order against a fresh <see cref="ServiceCollection"/>.</item>
    ///   <item>Resolves <see cref="IConstellaBootstrap"/> from the
    ///   built provider, runs <see cref="IConstellaBootstrap.BootstrapAsync"/>,
    ///   and disposes the bootstrap. The bootstrap is the place
    ///   first-window opening lives, so this is what makes the UI
    ///   appear.</item>
    ///   <item>If the host is running under
    ///   <see cref="IClassicDesktopStyleApplicationLifetime"/>, hands
    ///   the active window from <see cref="IConstellaApp.NavigationManager"/>
    ///   to <see cref="IClassicDesktopStyleApplicationLifetime.MainWindow"/>
    ///   so the lifetime knows when to terminate the process.</item>
    /// </list>
    /// </summary>
    public async Task BuildAsync(Application application)
    {
        var sorted   = TopologicalSort(_modules);
        var services = new ServiceCollection();

        services.AddSingleton(application);

        foreach (var module in sorted)
            module.Build(services);

        var provider = services.BuildServiceProvider();

        var logger = provider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LogCategory.WindowProcess);

        logger.LogInformation("ConstellaTTS starting");

        // Post-build wiring: every module that needs a fully-resolved
        // service to do its side-effects (keybind registration,
        // flyout-handler attachment, cache pre-warming, …) runs its
        // OnBuilt hook here, in the same dependency order Build ran
        // in. Skipping this step is silent: missing flyout handlers
        // make the navigation manager's switch fall through to its
        // default arm, history still pushes, but no Show / Hide is
        // called — which is exactly the kind of thing that takes a
        // session of staring at logs to spot.
        foreach (var module in sorted)
            module.OnBuilt(provider);

        await using (var bootstrap = provider.GetRequiredService<IConstellaBootstrap>())
        {
            await bootstrap.BootstrapAsync();
        }

        // Hand the freshly-opened window to the desktop lifetime so
        // closing it terminates the process. Other lifetimes (single-
        // view on mobile, headless test hosts) don't need this step
        // and the cast falls through harmlessly.
        if (application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var app = provider.GetRequiredService<IConstellaApp>();
            desktop.MainWindow = (Window?)app.NavigationManager.Value.ActiveWindow;
            logger.LogInformation("Main window ready");
        }
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
