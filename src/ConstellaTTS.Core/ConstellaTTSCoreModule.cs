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
using ConstellaTTS.SDK.Audio;
using ConstellaTTS.SDK.Engine;
using ConstellaTTS.SDK.Exceptions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.IO;
using ConstellaTTS.SDK.IPC;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.Theme;
using ConstellaTTS.SDK.Timeline;
using ConstellaTTS.SDK.UI.Keybinds;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.UI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace ConstellaTTS.Core;


public sealed class ConstellaTTSCoreModule : IConstellaModule
{
    public string Id   => "Com.ConstellaTTS.Core";
    public string Name => "ConstellaTTS Core";

    public IReadOnlyList<Assembly> Dependencies => [];

    public void Build(IServiceCollection services)
    {
        services.AddConstellaLogging();

        // ── Core services ─────────────────────────────────────────────────
        services.AddSingleton<IHistoryManager,    HistoryManager>();
        services.AddSingleton<IRegionManager,     RegionManager>();
        services.AddSingleton<IKeybindManager,    KeybindManager>();
        services.AddSingleton<INavigationManager, NavigationManager>();
        services.AddSingleton<IExceptionHandler,  ExceptionHandler>();
        services.AddSingleton<ExceptionHandler>();
        services.AddSingleton<IFileWriter,        LocalFileWriter>();

        // ── Project layer ────────────────────────────────────────────────
        // ProjectsService owns the on-disk registry (root/projects),
        // ProjectManager owns the single active IConstellaProject and
        // delegates registry mutations to ProjectsService on create.
        services.AddSingleton<IProjectsService,   ProjectsService>();
        services.AddSingleton<IProjectManager,    ProjectManager>();

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
            var projectManager   = sp.GetRequiredService<IProjectManager>();
            var loggerFactory    = sp.GetRequiredService<ILoggerFactory>();
            var view             = new TrackListView(
                toolMode, viewport, history, selection,
                engineCatalog,  viewportRecorder, projectManager,
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
                sp.GetRequiredService<INavigationManager>(),
                sp.GetRequiredService<SampleLibraryView>(),
                sp.GetRequiredService<SampleLibraryViewModel>()));
        services.AddSingleton<MainLayout>();
        services.AddSingleton<MainWindow>();

        // Launcher is transient — every open builds a fresh window
        // and view-model, every close (via NavigationManager) lets the
        // GC reclaim them. The launcher is the user's entry point and
        // re-entry point if they want to switch projects; carrying
        // stale state (search text, scroll position, in-flight tasks)
        // across runs is more confusing than helpful, and the
        // construction cost is negligible compared to the on-disk
        // registry read it does on each open. Singleton would also
        // pin the VM's IProjectsService / IProjectManager subscriptions
        // for the application's lifetime, which is the kind of leak
        // that only shows up under instrumentation.
        services.AddTransient<LauncherWindowViewModel>();
        services.AddTransient<LauncherWindow>(sp =>
            new LauncherWindow(sp.GetRequiredService<LauncherWindowViewModel>()));

        services.AddSingleton<ShowcaseWindow>();

        // ── Audio ─────────────────────────────────────────────────────────
        // Sample processor is stateless — every call carries its own
        // source / output paths — so a single shared instance serves
        // every importer (the upload action today, drag-drop and
        // clipboard paths once those wire in). Singleton keeps the
        // event subscriptions stable across imports.
        services.AddSingleton<ISampleProcessor, WavSampleProcessor>();

        // Sample service owns the live catalogue for the active
        // project. Singleton so the same in-memory list survives
        // every panel open / close and every project switch handles
        // its own internal refresh.
        services.AddSingleton<ISampleService, WavSampleService>();

        // Microphone recorder — Windows-only at v1 (NAudio.WaveIn).
        // The interface lives in SDK.Audio so cross-platform builds
        // can plug in a different backend later without touching
        // the view-model. Singleton because the recorder owns its
        // own start/stop state and a second instance would race on
        // the same default capture device.
#if WINDOWS
        services.AddSingleton<IMicrophoneRecorder, ConstellaTTS.SDK.Audio.Recording.NAudioMicrophoneRecorder>();
#endif

        // ── Actions ───────────────────────────────────────────────────────
        services.AddSingleton<ToggleSoundBankAction>();
        services.AddSingleton<SampleUploadAction>();
        services.AddSingleton<UndoLastAction>();
        services.AddSingleton<RedoLastAction>();

        // ── Bootstrap ─────────────────────────────────────────────────────
        services.AddTransient<IConstellaBootstrap, ConstellaBootstrap>();

        // ── Lazy<T> generic wrapper ─────────────────────────────────────────
        // Microsoft.Extensions.DependencyInjection does NOT synthesise
        // Lazy<T> automatically the way Autofac does — every concrete T
        // we want to inject as Lazy<T> needs an explicit registration.
        // Three lines, one per cross-cutting service ConstellaApp
        // exposes; the factory captures `sp` and defers the lookup
        // until the lazy is unwrapped, which is what breaks the cycle
        // between history, navigation, and theme.
        services.AddSingleton(sp => new Lazy<IHistoryManager>(
            sp.GetRequiredService<IHistoryManager>));
        services.AddSingleton(sp => new Lazy<INavigationManager>(
            sp.GetRequiredService<INavigationManager>));
        services.AddSingleton(sp => new Lazy<IThemeProvider>(
            sp.GetRequiredService<IThemeProvider>));

        // ── ConstellaApp ──────────────────────────────────────────────────
        // The keybind / flyout side-effects this factory runs are
        // here because they have to fire exactly once at app
        // wire-up. ConstellaApp itself takes plain Lazy<T>
        // dependencies (registered above) — the factory just
        // forwards them.
        services.AddSingleton<ConstellaApp>();
        services.AddSingleton<IConstellaApp>(sp => sp.GetRequiredService<ConstellaApp>());
    }

    /// <summary>
    /// Post-build wiring. The container is available by the time
    /// this runs, so we can resolve the keybind manager and the
    /// navigation manager and hook things up against real instances.
    /// Each step runs exactly once at startup.
    /// </summary>
    public void OnBuilt(IServiceProvider sp)
    {
        var kb = sp.GetRequiredService<IKeybindManager>();
        kb.Register(sp.GetRequiredService<ToggleSoundBankAction>());
        kb.Register(sp.GetRequiredService<UndoLastAction>());
        kb.Register(sp.GetRequiredService<RedoLastAction>());

        // Track the flyout window for keybind dispatch — keybinds
        // routed at the application level need to know which windows
        // can receive them.
        kb.TrackWindow(sp.GetRequiredService<SampleLibraryWindow>());

        // Register the sample library as a flyout the navigation
        // manager can show / hide / query, so ToggleSoundBankAction
        // (and any future caller) can navigate to it by type without
        // talking to the window directly.
        var nav    = sp.GetRequiredService<INavigationManager>();
        var flyout = sp.GetRequiredService<SampleLibraryWindow>();
        nav.RegisterFlyout(
            typeof(SampleLibraryWindow),
            show:      flyout.FlyoutShow,
            hide:      flyout.FlyoutHide,
            isVisible: () => flyout.IsVisible);
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
