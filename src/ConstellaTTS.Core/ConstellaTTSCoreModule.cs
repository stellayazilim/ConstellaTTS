using System.Reflection;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.Core.Layouts;
using ConstellaTTS.Core.Managers;
using ConstellaTTS.Core.Misc;
using ConstellaTTS.Core.Misc.Logging;
using ConstellaTTS.Core.Services;
using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.Core.Views;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Engine;
using ConstellaTTS.SDK.Exceptions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.IPC;
using ConstellaTTS.SDK.Theme;
using ConstellaTTS.SDK.Timeline;
using ConstellaTTS.SDK.UI.Keybinds;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.UI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConstellaTTS.Core.Views;
namespace ConstellaTTS.Core;


public sealed class ConstellaTTSCoreModule : IConstellaModule
{
    public string Id   => "Com.ConstellaTTS.Core";
    public string Name => "ConstellaTTS Core";

    public IReadOnlyList<Assembly> Dependencies => [];

    public void Build(IServiceCollection services)
    {
        services.AddConstellaLogging();

        // ── Lazy registrations ────────────────────────────────────────────
        services.AddSingleton<Lazy<IConstellaApp>>(sp =>
            new Lazy<IConstellaApp>(() => sp.GetRequiredService<IConstellaApp>()));
        services.AddSingleton<Lazy<MainWindow>>(sp =>
            new Lazy<MainWindow>(() => sp.GetRequiredService<MainWindow>()));

        // ── Core services ─────────────────────────────────────────────────
        services.AddSingleton<IHistoryManager,    HistoryManager>();
        services.AddSingleton<IRegionManager,     RegionManager>();
        services.AddSingleton<IKeybindManager,    KeybindManager>();
        services.AddSingleton<INavigationManager, NavigationManager>();
        services.AddSingleton<IExceptionHandler,  ExceptionHandler>();
        services.AddSingleton<ExceptionHandler>();

        // Viewport history recorder — single instance shared by every
        // viewport input source (track-canvas wheel, minimap drag, etc.).
        // Coalesces bursts into one undo entry per scroll session
        // regardless of which control originated the gesture.
        services.AddSingleton<IViewportHistoryRecorder, ViewportHistoryRecorder>();

        services.AddSingleton<IThemeProvider>(_ =>
        {
            var provider = new ThemeProvider(Application.Current!);
            provider.RegisterGlobal(new StyleInclude(new Uri("avares://ConstellaTTS.Core/"))
            {
                Source = new Uri("avares://ConstellaTTS.Core/Controls/ControlStyles.axaml")
            });
            return provider;
        });


        services.AddSingleton<IIPCService>(sp =>
        {
            var baseDir       = AppContext.BaseDirectory;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var log           = loggerFactory.CreateLogger(LogCategory.WindowProcess);
            var python        = ResolvePythonExe(baseDir);
            var daemon        = ResolveDaemonScript(baseDir);
            log.LogInformation("IPC: python={Python}", python);
            log.LogInformation("IPC: daemon={Daemon}", daemon);
            return new IPCClient(python, daemon, loggerFactory);
        });

        // ── ViewModels ────────────────────────────────────────────────────
        services.AddSingleton<IToolModeService,   ToolModeService>();
        services.AddSingleton<ISelectionService,  SelectionService>();
        services.AddSingleton<IEngineCatalog,     StaticEngineCatalog>();
        // services.AddSingleton<ISampleProvider,    InMemorySampleProvider>();
        services.AddSingleton<ITimelineViewport>(_ => TimelineViewport.Current);
        services.AddSingleton<TrackListViewModel>();
        services.AddSingleton<ContextBarViewModel>();
        services.AddSingleton<SampleLibraryViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DawToolbarViewModel>();

        // ── Views ─────────────────────────────────────────────────────────
        services.AddSingleton<SampleLibraryView>();
        services.AddSingleton<StatusBarView>();
        services.AddSingleton<ContextBarView>(sp =>
        {
            var vm   = sp.GetRequiredService<ContextBarViewModel>();
            var view = new ContextBarView { DataContext = vm };
            return view;
        });
        services.AddSingleton<TrackListView>(sp =>
        {
            var vm               = sp.GetRequiredService<TrackListViewModel>();
            var toolMode         = sp.GetRequiredService<IToolModeService>();
            var viewport         = sp.GetRequiredService<ITimelineViewport>();
            var history          = sp.GetRequiredService<IHistoryManager>();
            var selection        = sp.GetRequiredService<ISelectionService>();
            var engineCatalog    = sp.GetRequiredService<IEngineCatalog>();
            var viewportRecorder = sp.GetRequiredService<IViewportHistoryRecorder>();
            var loggerFactory    = sp.GetRequiredService<ILoggerFactory>();
            var view             = new TrackListView(
                toolMode, viewport, history, selection,
                engineCatalog,  viewportRecorder,
                loggerFactory)
            { DataContext = vm };
            return view;
        });
        services.AddSingleton<DawToolbarView>(sp =>
        {
            var vm   = sp.GetRequiredService<DawToolbarViewModel>();
            var view = new DawToolbarView { DataContext = vm };
            return view;
        });

        // ── Windows ───────────────────────────────────────────────────────
        services.AddSingleton<SampleLibraryWindow>(sp =>
            new SampleLibraryWindow(
                sp.GetRequiredService<Lazy<MainWindow>>(),
                sp.GetRequiredService<SampleLibraryView>(),
                sp.GetRequiredService<SampleLibraryViewModel>()));
        services.AddSingleton<MainLayout>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ShowcaseWindow>();

        // ── Actions ───────────────────────────────────────────────────────
        services.AddSingleton<ToggleSoundBankAction>();
        services.AddSingleton<UndoLastAction>();
        services.AddSingleton<RedoLastAction>();

        // ── Bootstrap ─────────────────────────────────────────────────────
        services.AddTransient<IConstellaBootstrap, ConstellaBootstrap>();

        // ── ConstellaApp ──────────────────────────────────────────────────
        services.AddSingleton<ConstellaApp>(sp =>
        {
            var kb = sp.GetRequiredService<IKeybindManager>();
            kb.Register(sp.GetRequiredService<ToggleSoundBankAction>());
            kb.Register(sp.GetRequiredService<UndoLastAction>());
            kb.Register(sp.GetRequiredService<RedoLastAction>());

            // Track flyout window for keybinds
            kb.TrackWindow(sp.GetRequiredService<SampleLibraryWindow>());

            var nav    = sp.GetRequiredService<INavigationManager>();
            var flyout = sp.GetRequiredService<SampleLibraryWindow>();
            nav.RegisterFlyout(
                typeof(SampleLibraryWindow),
                show:      flyout.FlyoutShow,
                hide:      flyout.FlyoutHide,
                isVisible: () => flyout.IsVisible);

            return new ConstellaApp(sp);
        });
        services.AddSingleton<IConstellaApp>(sp => sp.GetRequiredService<ConstellaApp>());
    }

    private static string ResolvePythonExe(string baseDir)
    {
        var dist = Path.Combine(baseDir, "python", "python.exe");
        if (File.Exists(dist)) return dist;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var dev = Path.Combine(dir.FullName, "infra", "python", "python.exe");
            if (File.Exists(dev)) return dev;
            dir = dir.Parent;
        }
        return dist;
    }

    private static string ResolveDaemonScript(string baseDir)
    {
        var dist = Path.Combine(baseDir, "daemon", "main.py");
        if (File.Exists(dist)) return dist;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var dev = Path.Combine(dir.FullName, "src", "ConstellaTTS.Daemon", "main.py");
            if (File.Exists(dev)) return dev;
            dir = dir.Parent;
        }
        return dist;
    }
}
