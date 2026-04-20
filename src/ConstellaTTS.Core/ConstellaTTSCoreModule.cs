using System.Reflection;
using ConstellaTTS.Core.Layout;
using ConstellaTTS.Core.Services;
using ConstellaTTS.Core.Views;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core;

/// <summary>
/// Core module — registers all platform services and the main window.
/// Must be loaded before any other module.
/// </summary>
public sealed class ConstellaTTSCoreModule : IConstellaModule
{
    public string Id   => "Com.ConstellaTTS.Core";
    public string Name => "ConstellaTTS Core";

    public IReadOnlyList<Assembly> Dependencies => [];

    public void Build(IServiceCollection services)
    {
        // Window factory — Avalonia specific, mounts default slot map
        services.AddSingleton<IWindowFactory>(sp =>
        {
            var factory = new AvaloniaWindowFactory(
                sp,
                sp.GetRequiredService<ISlotService>());
            factory.SetDefaultWindow(typeof(MainWindow));
            return factory;
        });

        // Window manager — SDK, uses factory
        services.AddSingleton<WindowManager>(sp =>
        {
            var manager = new WindowManager(sp.GetRequiredService<IWindowFactory>());
            manager.RegisterWindow(typeof(MainWindow));
            return manager;
        });
        services.AddSingleton<IWindowManager>(sp => sp.GetRequiredService<WindowManager>());

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
            sp.GetRequiredService<ISlotService>()));

        // Windows & Layouts
        services.AddSingleton<MainLayout>();
        services.AddSingleton<MainWindow>();

        // Test views
        services.AddSingleton<TestToolbarView>();
    }
}
