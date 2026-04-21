using ConstellaTTS.Core.Layout;
using ConstellaTTS.Core.Views;
using ConstellaTTS.SDK.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core;

/// <summary>
/// Simulates a plugin mounting views into existing slots.
/// In production each plugin does this inside its own module's Build() method.
/// </summary>
public static class TestPluginSimulator
{
    public static void Mount(IWindowManager windowManager, IServiceProvider services)
    {
        var layout  = services.GetRequiredService<MainLayout>();
        var toolbar = services.GetRequiredService<TestToolbarView>();
        layout.AddToToolbar(toolbar);
    }
}
