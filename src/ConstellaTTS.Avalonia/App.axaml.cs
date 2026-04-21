using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ConstellaTTS.Core.App;
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

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.MainWindow = app.WindowManager.GetDefaultWindow();
      
        base.OnFrameworkInitializationCompleted();
    }
}
