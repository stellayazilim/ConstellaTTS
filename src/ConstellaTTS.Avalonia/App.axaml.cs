using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ConstellaTTS.Core.App;
using ConstellaTTS.Core.Logging;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        var app = new ConstellaModuleRegistry()
            .Register(new ConstellaTTSCoreModule())
            .LoadPlugins("./plugins")
            .Build();

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LogCategory.WindowProcess);

        logger.LogInformation("ConstellaTTS starting");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await using (var bootstrap = app.Services.GetRequiredService<IConstellaBootstrap>())
            {
                await bootstrap.BootstrapAsync();
            } // nav referansı bırakıldı

            desktop.MainWindow = (Window?)app.NavigationManager.ActiveWindow;
            logger.LogInformation("Main window ready");
        }

#if DEBUG
        var history = app.HistoryManager;
        history.Push(new DummyEntry("test-1", "Test action 1"));
        history.Push(new DummyEntry("test-2", "Test action 2"));
        history.Push(new DummyEntry("test-3", "Test action 3"));
        logger.LogInformation("Test: pushed 3 dummy history entries — press Ctrl+Z to roll back");
#endif

        base.OnFrameworkInitializationCompleted();
    }

#if DEBUG
    private sealed class DummyEntry(string id, string name) : IReversible
    {
        public string Id   { get; } = id;
        public string Name { get; } = name;
        public IAction Reverse(IReversible? previous, params object[] args) => NoOpAction.Instance;
    }

    private sealed class NoOpAction : ActionBase
    {
        public static readonly NoOpAction Instance = new();
        public override string Id   => "NoOp";
        public override string Name => "No-op";
        public override void Execute(object? data = null) { }
    }
#endif
}
