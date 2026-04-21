using ConstellaTTS.SDK.Exceptions;

namespace ConstellaTTS.Core.Exceptions;

/// <summary>Base class for IPC and daemon-related exceptions.</summary>
public abstract class IPCException : ConstellaException
{
    protected IPCException(string message) : base(message) { }
    protected IPCException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when the daemon process fails to start.</summary>
public sealed class DaemonStartFailedException : IPCException
{
    public string PythonExe    { get; }
    public string DaemonScript { get; }

    public DaemonStartFailedException(string pythonExe, string daemonScript, Exception? inner = null)
        : base($"Failed to start daemon. python='{pythonExe}' script='{daemonScript}'",
               inner ?? new Exception("unknown"))
    {
        PythonExe    = pythonExe;
        DaemonScript = daemonScript;
    }
}

/// <summary>
/// Thrown when no tts.chunk arrives within the configured timeout during an active generation.
/// Indicates the daemon process has likely crashed or become unresponsive.
/// </summary>
public sealed class DaemonNotRespondingException : IPCException
{
    public TimeSpan Elapsed { get; }
    public TimeSpan Timeout { get; }

    public DaemonNotRespondingException(TimeSpan elapsed, TimeSpan timeout)
        : base($"Daemon not responding — no chunk received for {elapsed.TotalSeconds:F0}s " +
               $"(timeout: {timeout.TotalSeconds:F0}s). The daemon process may have crashed.")
    {
        Elapsed = elapsed;
        Timeout = timeout;
    }
}

/// <summary>Thrown when a request/response IPC call exceeds its timeout.</summary>
public sealed class IPCTimeoutException : IPCException
{
    public string Event     { get; }
    public string MessageId { get; }

    public IPCTimeoutException(string @event, string messageId, TimeSpan timeout)
        : base($"IPC timeout waiting for response to '{@event}' " +
               $"(id={messageId}, timeout={timeout.TotalSeconds:F0}s)")
    {
        Event     = @event;
        MessageId = messageId;
    }
}
