namespace ConstellaTTS.SDK.IPC;

/// <summary>
/// High-level client for the ConstellaTTS Python daemon.
///
/// Handles daemon lifecycle (spawn/shutdown) and request/response
/// correlation over the control pipe. Job-streaming subscription will be
/// added in a later iteration.
/// </summary>
public interface IIPCService : IAsyncDisposable
{
    /// <summary>True once the daemon has announced its endpoint and the control pipe is connected.</summary>
    bool IsConnected { get; }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Start the daemon process (if not already running) and connect to its
    /// control pipe. Safe to call multiple times — becomes a no-op once
    /// connected.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Gracefully shut the daemon down and close the pipe.</summary>
    Task StopAsync(CancellationToken ct = default);

    // ── Request/response ────────────────────────────────────────────────────

    /// <summary>
    /// Send a request over the control pipe and await the matching response.
    /// </summary>
    /// <param name="route">Dotted route, e.g. <c>"echo.ping"</c>.</param>
    /// <param name="data">Optional payload. Pass an anonymous object or dict.</param>
    /// <param name="timeout">Max time to wait for a response. Defaults to 30s.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response envelope — inspect <c>Ok</c> before reading <c>Data</c>.</returns>
    /// <exception cref="TimeoutException">Response did not arrive within <paramref name="timeout"/>.</exception>
    /// <exception cref="InvalidOperationException">Client is not connected.</exception>
    Task<IPCResponse> RequestAsync(
        string route,
        object? data = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    // ── Streaming ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start a streaming job and subscribe to its per-job pipe.
    ///
    /// Calls <paramref name="startRoute"/> (typically <c>"{model}.generate"</c>)
    /// with <paramref name="data"/>, reads <c>stream_pipe</c> and
    /// <c>job_id</c> from the response, then opens the stream pipe and
    /// returns an <see cref="IPCStream"/> you can iterate.
    /// </summary>
    /// <param name="startRoute">
    /// The <c>generate</c>-style route to call. Must return a response
    /// with <c>job_id</c> and <c>stream_pipe</c> in its <c>data</c>.
    /// </param>
    /// <param name="data">Optional payload forwarded to the route.</param>
    /// <param name="startTimeout">Max time to wait for the start response.</param>
    /// <param name="ct">Cancellation token for the start request.</param>
    /// <returns>An open <see cref="IPCStream"/>. Dispose it when you're done.</returns>
    /// <exception cref="InvalidOperationException">
    /// Client is not connected, or the start route returned a malformed
    /// response (missing <c>job_id</c> / <c>stream_pipe</c>), or the
    /// handler itself failed.
    /// </exception>
    Task<IPCStream> StartStreamAsync(
        string startRoute,
        object? data = null,
        TimeSpan? startTimeout = null,
        CancellationToken ct = default);
}
