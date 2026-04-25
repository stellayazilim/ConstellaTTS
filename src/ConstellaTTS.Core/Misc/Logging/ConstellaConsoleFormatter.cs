using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ConstellaTTS.Core.Misc.Logging;

/// <summary>
/// Custom <see cref="ConsoleFormatter"/> that emits lines in the format:
///   <c>HH:mm:ss.fff LVL [category] message</c>
///
/// Renders ANSI color per log level when <see cref="ConstellaConsoleFormatterOptions.UseColor"/>
/// is true (default). ANSI escape sequences are a no-op on terminals that
/// don't support them, so leaving color on is safe.
/// </summary>
public sealed class ConstellaConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "constella";

    private readonly IDisposable?                    _optionsReloadToken;
    private          ConstellaConsoleFormatterOptions _options;

    public ConstellaConsoleFormatter(IOptionsMonitor<ConstellaConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options            = options.CurrentValue;
        _optionsReloadToken = options.OnChange(o => _options = o);
    }

    public override void Write<TState>(
        in LogEntry<TState>     logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter              textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null) return;

        var useColor      = _options.UseColor;
        var timestamp     = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
        var levelText     = FormatLevel(logEntry.LogLevel);
        var levelColor    = useColor ? LevelColor(logEntry.LogLevel) : null;
        var categoryColor = useColor ? CategoryColor(logEntry.Category) : null;

        // HH:mm:ss.fff  (dim)
        WriteColored(textWriter, timestamp, useColor ? AnsiDim : null);
        textWriter.Write(' ');

        // LVL           (level-colored)
        WriteColored(textWriter, levelText, levelColor);
        textWriter.Write(' ');

        // [category]    (category-colored)
        WriteColored(textWriter, $"[{logEntry.Category}]", categoryColor);
        textWriter.Write(' ');

        // message
        textWriter.Write(message);
        textWriter.WriteLine();

        if (logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception.ToString());
    }

    private static string FormatLevel(LogLevel level) => level switch
    {
        LogLevel.Trace       => "TRC",
        LogLevel.Debug       => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning     => "WRN",
        LogLevel.Error       => "ERR",
        LogLevel.Critical    => "CRT",
        _                    => "   ",
    };

    // ── ANSI helpers ────────────────────────────────────────────────────────

    private const string AnsiReset = "\u001b[0m";
    private const string AnsiDim   = "\u001b[2;37m";

    private static string? LevelColor(LogLevel level) => level switch
    {
        LogLevel.Trace       => "\u001b[90m",       // bright black / grey
        LogLevel.Debug       => "\u001b[36m",       // cyan
        LogLevel.Information => "\u001b[32m",       // green
        LogLevel.Warning     => "\u001b[33m",       // yellow
        LogLevel.Error       => "\u001b[31m",       // red
        LogLevel.Critical    => "\u001b[1;41;97m",  // bold white on red bg
        _                    => null,
    };

    private static string? CategoryColor(string category) => category switch
    {
        LogCategory.WindowProcess => "\u001b[35m",   // magenta
        LogCategory.PythonProcess => "\u001b[94m",   // bright blue
        _                         => "\u001b[90m",   // grey (ipc.client etc.)
    };

    private static void WriteColored(TextWriter writer, string text, string? color)
    {
        if (color is null) { writer.Write(text); return; }
        writer.Write(color);
        writer.Write(text);
        writer.Write(AnsiReset);
    }

    public void Dispose() => _optionsReloadToken?.Dispose();
}

/// <summary>
/// Options for <see cref="ConstellaConsoleFormatter"/>.
/// Extends <see cref="ConsoleFormatterOptions"/> (timestamp/scope fields)
/// with a simple boolean color toggle — avoiding dependency on
/// <c>LoggerColorBehavior</c> which lives on <see cref="SimpleConsoleFormatterOptions"/>.
/// </summary>
public sealed class ConstellaConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>Whether to emit ANSI color escape sequences. Default: true.</summary>
    public bool UseColor { get; set; } = true;
}
