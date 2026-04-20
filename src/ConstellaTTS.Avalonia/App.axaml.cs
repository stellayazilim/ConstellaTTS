using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ConstellaTTS.Core;
using ConstellaTTS.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var app = ConstellaApp.Initialize(registry => registry
            .Register(new ConstellaTTSCoreModule())
            .LoadPlugins("./plugins"));

        // Get default window — mounts MainLayout internally
        var mainWindow = app.WindowManager.GetDefaultWindow();

        // Simulate plugin mounting into an existing slot
        TestPluginSimulator.Mount(app.WindowManager, app.Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = mainWindow;

        base.OnFrameworkInitializationCompleted();
    }
}
