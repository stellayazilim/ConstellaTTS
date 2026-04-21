namespace ConstellaTTS.SDK.IPC;

/// <summary>
/// Manages the Python daemon process and provides send/subscribe primitives.
///
/// Two subscription styles:
///   Event-style:  ipc.MessageReceived += async msg => { ... }
///   Method-style: ipc.On("ping",       async msg => { ... })
/// </summary>
public interface IIPCService
{
    /// <summary>True when the daemon process is running and the pipe is open.</summary>
    bool IsConnected { get; }

    // Lifecycle

    /// <summary>
    /// Starts the daemon if it is not already running.
    /// Safe to call multiple times — no-op if already connected.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Gracefully shuts down the daemon process.</summary>
    Task StopAsync();

    // Send

    /// <summary>Sends an event to the daemon. Fire and forget.</summary>
    Task SendAsync(string @event, object? data = null);

    /// <summary>Sends an event and waits for a response with the same message ID.</summary>
    Task<IPCMessage> SendAsync(string @event, object? data, TimeSpan timeout);

    // Subscribe — event style

    /// <summary>
    /// Raised for every message received from the daemon.
    /// Use for broad or global subscriptions.
    /// </summary>
    event Func<IPCMessage, Task> MessageReceived;

    // Subscribe — method style

    /// <summary>
    /// Subscribes a handler to a specific event name.
    /// Pass "*" to receive all events regardless of name.
    /// </summary>
    void On(string @event, Func<IPCMessage, Task> handler);

    /// <summary>Removes a previously registered method-style handler.</summary>
    void Off(string @event, Func<IPCMessage, Task> handler);
}
