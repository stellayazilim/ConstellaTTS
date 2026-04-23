using Avalonia;
using System;

namespace ConstellaTTS.Avalonia;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things
    // aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Avalonia configuration — also used by the visual designer.
    //
    // Logging output destinations in Debug builds:
    //   • Rider / Visual Studio "Debug" output window — via Microsoft.Extensions.Logging.Debug
    //     provider (uses Debug.WriteLine; automatically picked up by IDE debugger)
    //   • Console provider — active only when a console is attached (e.g. launched from
    //     a terminal with `dotnet run`). When launched as a detached GUI app from the IDE,
    //     console output is silently dropped which is the desired behavior.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
