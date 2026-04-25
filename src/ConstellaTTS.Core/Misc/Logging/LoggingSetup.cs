using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace ConstellaTTS.Core.Misc.Logging;

/// <summary>
/// Registers ILoggerFactory / ILogger with DI and wires the ConstellaTTS
/// console formatter (colored, prefixed with [category]) plus a Debug
/// output sink for Visual Studio's Output window.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Add logging to a <see cref="IServiceCollection"/>. Safe to call
    /// from inside a module's <c>Build</c>.
    /// </summary>
    public static IServiceCollection AddConstellaLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();

            // Console — colored, prefixed with [category], ANSI where supported.
            builder.AddConsole(o =>
            {
                o.FormatterName = ConstellaConsoleFormatter.FormatterName;
            });
            builder.AddConsoleFormatter<
                ConstellaConsoleFormatter,
                ConstellaConsoleFormatterOptions>();

            // Debug output — shows up in Visual Studio's Output window.
            builder.AddDebug();

            // Default minimum level. Tune per-category below if needed.
            builder.SetMinimumLevel(LogLevel.Debug);

            // Keep IPCClient's own chatter quieter than the daemon forwards —
            // its diagnostics are useful but verbose at Debug.
            builder.AddFilter("ipc.client", LogLevel.Information);
        });

        return services;
    }
}
