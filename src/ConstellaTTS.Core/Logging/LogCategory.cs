namespace ConstellaTTS.Core.Logging;

/// <summary>
/// Canonical logger category names. Every logger in the application is
/// created under one of these — either <see cref="WindowProcess"/> for
/// all C# app code, or <see cref="PythonProcess"/> for forwarded daemon
/// stdout/stderr.
/// </summary>
/// <remarks>
/// Electron analogy: the Avalonia UI is the "window process" (main),
/// the Python daemon is the "python process" (renderer/worker).
/// </remarks>
public static class LogCategory
{
    /// <summary>C# application code — UI, controllers, services, everything except forwarded daemon output.</summary>
    public const string WindowProcess = "window_process";

    /// <summary>Forwarded Python daemon stdout/stderr (set by IPCClient).</summary>
    public const string PythonProcess = "python_process";
}
