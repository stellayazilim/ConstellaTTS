using Avalonia;
using Avalonia.Markup.Xaml;
using ConstellaTTS.Core;

namespace ConstellaTTS.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        await new ConstellaModuleRegistry()
            .Register(new ConstellaTTSCoreModule())
            .LoadPlugins("./plugins")
            .BuildAsync(this);

        base.OnFrameworkInitializationCompleted();
    }
}
