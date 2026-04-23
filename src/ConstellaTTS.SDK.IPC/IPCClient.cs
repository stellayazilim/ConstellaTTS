using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConstellaTTS.SDK.IPC;

/// <summary>
/// Concrete <see cref="IIPCService"/> that manages the Python daemon
/// process and talks to it over a Windows named pipe.
///
/// Wire format on the control pipe:
///   [4-byte little-endian uint32 length][MessagePack body]
/// </summary>
/// <remarks>
/// The control pipe path is deterministic: <c>\\.\pipe\constella_&lt;pid&gt;_control</c>
/// where <c>&lt;pid&gt;</c> is the daemon's PID (known to us via
/// <see cref="Process.Id"/>). Both sides derive the path from the PID
/// independently; no stdout/stderr sideband is needed for the host to
/// find the daemon's listener — <see cref="NamedPipeClientStream.ConnectAsync(TimeSpan)"/>
/// polls the pipe namespace until the daemon's server comes up.
/// </remarks>
public sealed class IPCClient : IIPCService
{
    private readonly string   _pythonExePath;
    private readonly string   _daemonScriptPath;
    private readonly ILogger  _ipcLogger;       // IPCClient's own diagnostics
    private readonly ILogger  _daemonLogger;    // forwards daemon stdout/stderr

    private Process? _daemonProcess;
    private NamedPipeClientStream? _pipe;

    // Correlation table: request ID → TaskCompletionSource awaited by the caller.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPCResponse>>
        _pending = new();

    // Serializes writes to the pipe. Reads are single-threaded on a dedicated task.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Cancels the background reader loop on shutdown.
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;

    private volatile bool _connected;

    public bool IsConnected => _connected;

    /// <summary>
    /// Create a new client pointing at a specific Python interpreter and
    /// daemon entry script.
    /// </summary>
    /// <param name="pythonExePath">Absolute path to <c>python.exe</c>.</param>
    /// <param name="daemonScriptPath">Absolute path to the daemon's <c>main.py</c>.</param>
    /// <param name="loggerFactory">
    /// Optional logger factory. When supplied, two categories are created:
    ///   <c>ipc.client</c> — IPCClient's own diagnostics.
    ///   <c>python_process</c> — forwards daemon stdout (INF) / stderr (WRN).
    /// When null, logging is silenced (NullLoggerFactory is used).
    /// </param>
    public IPCClient(
        string          pythonExePath,
        string          daemonScriptPath,
        ILoggerFactory? loggerFactory = null)
    {
        _pythonExePath    = pythonExePath;
        _daemonScriptPath = daemonScriptPath;

        loggerFactory  ??= NullLoggerFactory.Instance;
        _ipcLogger      = loggerFactory.CreateLogger("ipc.client");
        _daemonLogger   = loggerFactory.CreateLogger("python_process");
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        // 1. Spawn the daemon.
        var psi = new ProcessStartInfo
        {
            FileName               = _pythonExePath,
            Arguments              = $"\"{_daemonScriptPath}\"",
            WorkingDirectory       = Path.GetDirectoryName(_daemonScriptPath)!,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        _daemonProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start daemon process");

        _ipcLogger.LogInformation("Daemon spawned, pid={Pid}", _daemonProcess.Id);

        // 2. Connect to the deterministic control pipe path derived from
        //    the daemon's PID. ConnectAsync(timeout) internally polls the
        //    named pipe namespace via WaitNamedPipe, so there's no race
        //    with the daemon not being ready yet — it just blocks until
        //    the daemon's listener comes up (or the timeout fires).
        var pipePath = $@"\\.\pipe\constella_{_daemonProcess.Id}_control";
        _ipcLogger.LogDebug("Connecting to control pipe: {PipePath}", pipePath);
        await ConnectToNamedPipeAsync(pipePath, ct, TimeSpan.FromSeconds(15));
        _ipcLogger.LogInformation("Control pipe connected");

        // 3. Now that we're connected, start forwarding daemon logs.
        //    The daemon splits by severity: INFO/DEBUG go to stdout,
        //    WARNING+ to stderr. We preserve that split by logging at
        //    matching levels on the "python_process" category.
        _ = Task.Run(() => PumpStreamAsync(
            _daemonProcess.StandardOutput, _daemonLogger, LogLevel.Information));
        _ = Task.Run(() => PumpStreamAsync(
            _daemonProcess.StandardError,  _daemonLogger, LogLevel.Warning));

        // 4. Start the background reader.
        _readerCts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token));

        _connected = true;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_connected && _daemonProcess is null) return;

        _connected = false;
        _ipcLogger.LogInformation("Stopping daemon");

        // 1. Cancel reader loop.
        _readerCts?.Cancel();

        // 2. Close pipe — also unblocks the reader if it's mid-read.
        try { _pipe?.Close(); } catch { /* ignored */ }

        if (_readerTask is not null)
        {
            try { await _readerTask.WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch { /* ignored */ }
        }

        // 3. Ask daemon to exit by closing its stdin (triggers its stdin watcher).
        try { _daemonProcess?.StandardInput.Close(); } catch { /* ignored */ }

        // 4. Give it a moment, then kill if still alive.
        if (_daemonProcess is not null)
        {
            try
            {
                await _daemonProcess.WaitForExitAsync(
                    new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            }
            catch (OperationCanceledException)
            {
                _ipcLogger.LogWarning(
                    "Daemon did not exit gracefully within 3s, killing");
                try { _daemonProcess.Kill(entireProcessTree: true); } catch { /* ignored */ }
            }
        }

        // 5. Fail any still-pending requests.
        foreach (var kvp in _pending)
            kvp.Value.TrySetException(new InvalidOperationException("IPC client stopped"));
        _pending.Clear();

        _pipe?.Dispose();
        _daemonProcess?.Dispose();
        _pipe = null;
        _daemonProcess = null;

        _ipcLogger.LogInformation("Daemon stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _writeLock.Dispose();
    }

    // ── Request/response ────────────────────────────────────────────────────

    public async Task<IPCResponse> RequestAsync(
        string            route,
        object?           data   = null,
        TimeSpan?         timeout = null,
        CancellationToken ct     = default)
    {
        if (!_connected || _pipe is null)
            throw new InvalidOperationException("Client is not connected");

        var req = new IPCRequest { Route = route, Data = data };
        var tcs = new TaskCompletionSource<IPCResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(req.Id, tcs))
            throw new InvalidOperationException($"Duplicate request ID {req.Id}");

        _ipcLogger.LogDebug("→ {Route} (id={Id})", route, req.Id);

        try
        {
            await WriteFrameAsync(req, ct);
        }
        catch
        {
            _pending.TryRemove(req.Id, out _);
            throw;
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        await using var registration = linked.Token.Register(() =>
        {
            if (_pending.TryRemove(req.Id, out var existing))
            {
                if (timeoutCts.IsCancellationRequested)
                    existing.TrySetException(new TimeoutException(
                        $"No response for {route} (id={req.Id}) within {effectiveTimeout}"));
                else
                    existing.TrySetCanceled(ct);
            }
        });

        return await tcs.Task;
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    public async Task<IPCStream> StartStreamAsync(
        string            startRoute,
        object?           data         = null,
        TimeSpan?         startTimeout = null,
        CancellationToken ct           = default)
    {
        // 1. Call the generate-style route to start the job.
        var resp = await RequestAsync(startRoute, data, startTimeout, ct);
        if (!resp.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to start stream on {startRoute}: " +
                $"{resp.Error?.Type}: {resp.Error?.Message}");
        }

        // 2. Extract job_id and stream_pipe from the response.
        var (jobId, streamPipePath) = ExtractStreamInfo(resp.Data, startRoute);

        // 3. Derive the route prefix for the matching cancel action.
        //    "fake.generate" → "fake"; "chatterbox_ml.generate" → "chatterbox_ml".
        var dotIdx = startRoute.IndexOf('.');
        if (dotIdx <= 0)
            throw new InvalidOperationException(
                $"startRoute '{startRoute}' must be in 'model.action' format");
        var routePrefix = startRoute[..dotIdx];

        // 4. Connect to the stream pipe.
        var streamPipe = await ConnectToStreamPipeAsync(streamPipePath, ct);

        _ipcLogger.LogInformation(
            "Stream started: route={Route}, job_id={JobId}", startRoute, jobId);

        return new IPCStream(this, streamPipe, routePrefix, jobId);
    }

    private static (string jobId, string streamPipe) ExtractStreamInfo(
        object? data, string startRoute)
    {
        // MessagePack deserializes an untyped map as Dictionary<object,object>.
        if (data is not IDictionary<object, object> dict)
            throw new InvalidOperationException(
                $"{startRoute} response data is not a dict (got {data?.GetType().Name ?? "null"})");

        string? jobId      = null;
        string? streamPipe = null;
        foreach (var kvp in dict)
        {
            var key = kvp.Key as string;
            if      (key == "job_id")      jobId      = kvp.Value as string;
            else if (key == "stream_pipe") streamPipe = kvp.Value as string;
        }

        if (string.IsNullOrEmpty(jobId))
            throw new InvalidOperationException(
                $"{startRoute} response missing 'job_id'");
        if (string.IsNullOrEmpty(streamPipe))
            throw new InvalidOperationException(
                $"{startRoute} response missing 'stream_pipe'");

        return (jobId, streamPipe);
    }

    private static async Task<NamedPipeClientStream> ConnectToStreamPipeAsync(
        string pipePath, CancellationToken ct)
    {
        const string prefix = @"\\.\pipe\";
        if (!pipePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected pipe path: {pipePath}");

        var pipeName = pipePath[prefix.Length..];
        var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName:   pipeName,
            direction:  PipeDirection.InOut,
            options:    PipeOptions.Asynchronous);

        await pipe.ConnectAsync(TimeSpan.FromSeconds(5), ct);
        return pipe;
    }

    // ── Framing ─────────────────────────────────────────────────────────────

    private async Task WriteFrameAsync(IPCRequest req, CancellationToken ct)
    {
        var body   = MessagePackSerializer.Serialize(req);
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);

        await _writeLock.WaitAsync(ct);
        try
        {
            // Concat header+body into a single write. One syscall is both
            // faster and avoids a peer ever seeing a half-framed header.
            var frame = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            Buffer.BlockCopy(body,   0, frame, header.Length, body.Length);

            await _pipe!.WriteAsync(frame, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var headerBuf = new byte[4];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Read 4-byte length header.
                if (!await ReadExactlyAsync(headerBuf, ct))
                    break;  // clean EOF

                var length = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf);
                if (length == 0 || length > 16 * 1024 * 1024)
                    throw new InvalidDataException($"Bad frame length: {length}");

                // Read body.
                var body = new byte[length];
                if (!await ReadExactlyAsync(body, ct))
                    throw new EndOfStreamException("Pipe closed mid-frame");

                var response = MessagePackSerializer.Deserialize<IPCResponse>(body);
                DispatchResponse(response);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _ipcLogger.LogError(ex, "Read loop failed");
            foreach (var kvp in _pending)
                kvp.Value.TrySetException(ex);
            _pending.Clear();
        }
    }

    private async Task<bool> ReadExactlyAsync(byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await _pipe!.ReadAsync(buffer.AsMemory(offset), ct);
            if (n == 0) return offset == 0;  // clean EOF at boundary vs. mid-frame
            offset += n;
        }
        return true;
    }

    private void DispatchResponse(IPCResponse response)
    {
        if (response.Id is null)
        {
            _ipcLogger.LogWarning("Received response without ID, dropping");
            return;
        }

        _ipcLogger.LogDebug(
            "← response id={Id} ok={Ok}", response.Id, response.Ok);

        if (_pending.TryRemove(response.Id, out var tcs))
            tcs.TrySetResult(response);
        else
            _ipcLogger.LogWarning(
                "Received response for unknown request id {Id}", response.Id);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task ConnectToNamedPipeAsync(
        string pipePath, CancellationToken ct, TimeSpan? timeout = null)
    {
        // Pipe path arrives as "\\.\pipe\NAME" — NamedPipeClientStream wants
        // the server and name separately.
        const string prefix = @"\\.\pipe\";
        if (!pipePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected pipe path: {pipePath}");

        var pipeName = pipePath[prefix.Length..];
        _pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName:   pipeName,
            direction:  PipeDirection.InOut,
            options:    PipeOptions.Asynchronous);

        await _pipe.ConnectAsync(timeout ?? TimeSpan.FromSeconds(5), ct);
    }

    /// <summary>
    /// Pump lines from a daemon stdout/stderr stream into the provided logger.
    /// </summary>
    private static async Task PumpStreamAsync(
        StreamReader source, ILogger logger, LogLevel level)
    {
        try
        {
            string? line;
            while ((line = await source.ReadLineAsync()) is not null)
                logger.Log(level, "{Line}", line);
        }
        catch
        {
            /* stream closed, end of job */
        }
    }
}
