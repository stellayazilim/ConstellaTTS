using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using ConstellaTTS.Core.Exceptions;
using ConstellaTTS.Core.History;
using ConstellaTTS.Core.IPC;
using ConstellaTTS.Core.Layout;
using ConstellaTTS.Core.Sound;
using ConstellaTTS.Core.Theme;
using ConstellaTTS.Core.UI.Infrastructure;
using ConstellaTTS.Core.Views;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Exceptions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.IPC;
using ConstellaTTS.SDK.Theme;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Slots;
using ConstellaTTS.SDK.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core.App;

public sealed class ConstellaTTSCoreModule : IConstellaModule
{
    public string Id   => "Com.ConstellaTTS.Core";
    public string Name => "ConstellaTTS Core";

    public IReadOnlyList<Assembly> Dependencies => [];

    public void Build(IServiceCollection services)
    {
        // Window factory
        services.AddSingleton<IWindowFactory>(sp =>
        {
            var factory = new AvaloniaWindowFactory(
                sp,
                sp.GetRequiredService<ISlotService>());
            factory.SetDefaultWindow(typeof(MainWindow));
            return factory;
        });

        // Window manager
        services.AddSingleton<WindowManager>(sp =>
        {
            var manager = new WindowManager(sp.GetRequiredService<IWindowFactory>());
            manager.RegisterWindow(typeof(MainWindow));

            manager.DeferMount(window =>
            {
                if (window.FindControl<ContentControl>("LayoutSlot") is { } slot)
                    slot.Content = sp.GetRequiredService<MainLayout>();
            });

            return manager;
        });
        services.AddSingleton<IWindowManager>(sp => sp.GetRequiredService<WindowManager>());

        // Theme provider
        services.AddSingleton<IThemeProvider>(_ =>
        {
            var provider = new ThemeProvider(Application.Current!);

            provider.RegisterGlobal(new StyleInclude(
                new Uri("avares://ConstellaTTS.Core/"))
            {
                Source = new Uri("avares://ConstellaTTS.Core/Controls/ControlStyles.axaml")
            });

            return provider;
        });

        // Exception handler — UI subscribes to ExceptionHandler.ExceptionHandled
        services.AddSingleton<ExceptionHandler>();
        services.AddSingleton<IExceptionHandler>(sp =>
            sp.GetRequiredService<ExceptionHandler>());

        // Sound service
        services.AddSingleton<ISoundService>(_ =>
        {
            var baseDir   = AppContext.BaseDirectory;
            var tempDir   = Path.Combine(baseDir, "cache", "pcm");
            var outputDir = Path.Combine(baseDir, "cache", "opus");
            return new SoundService(tempDir, outputDir);
        });

        // IPC
        // Dev:  infra/python/python.exe  (setup by infra/setup.csx)
        // Dist: python/python.exe        (installed by the app installer)
        services.AddSingleton<IIPCService>(sp =>
        {
            var baseDir = AppContext.BaseDirectory;
            var python  = ResolvePythonExe(baseDir);
            var daemon  = Path.Combine(baseDir, "daemon", "main.py");
            return new IPCService(
                python, daemon,
                sp.GetRequiredService<ISoundService>(),
                sp.GetRequiredService<IExceptionHandler>());
        });

        // Core services
        services.AddSingleton<IHistoryManager, HistoryManager>();
        services.AddSingleton<ISlotService>(_ =>
        {
            var slotService = new SlotService();
            slotService.RegisterWindow(new WindowDescriptor(
                windowType: typeof(MainWindow),
                slotMap: new SlotMap()
                    .Add(Slots.Toolbar,   SlotType.Control)
                    .Add(Slots.ViewTools, SlotType.Control)
                    .Add(Slots.Content,   SlotType.Page)));
            return slotService;
        });
        services.AddSingleton<INavigationManager, NavigationManager>();

        // ConstellaApp
        services.AddSingleton<ConstellaApp>(sp => new ConstellaApp(
            sp,
            sp.GetRequiredService<IWindowManager>(),
            sp.GetRequiredService<IHistoryManager>(),
            sp.GetRequiredService<INavigationManager>(),
            sp.GetRequiredService<ISlotService>(),
            sp.GetRequiredService<IThemeProvider>()));
        services.AddSingleton<IConstellaApp>(sp => sp.GetRequiredService<ConstellaApp>());

        // Windows & Layouts
        services.AddSingleton<MainLayout>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ShowcaseWindow>();

        // Test views
        services.AddSingleton<TestToolbarView>();
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
}
