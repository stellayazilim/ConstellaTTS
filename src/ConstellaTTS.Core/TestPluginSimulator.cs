using Avalonia.Controls;
using ConstellaTTS.Core.Layout;
using ConstellaTTS.Core.Views;
using ConstellaTTS.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core;

/// <summary>
/// Simulates a plugin mounting its views into existing slots.
/// In production, each plugin would do this in their own module.
/// </summary>
public static class TestPluginSimulator
{
    public static void Mount(IWindowManager windowManager, IServiceProvider services)
    {
        // MainLayout is singleton — resolve directly, no visual tree needed
        var layout = services.GetRequiredService<MainLayout>();

        if (layout.FindControl<StackPanel>("ToolbarSlot") is { } toolbarSlot)
            toolbarSlot.Children.Add(services.GetRequiredService<TestToolbarView>());
    }
}
