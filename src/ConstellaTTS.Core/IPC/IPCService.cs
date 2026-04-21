using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ConstellaTTS.Core.Exceptions;
using ConstellaTTS.Core.Sound;
using ConstellaTTS.SDK.IPC;
using MessagePack;

namespace ConstellaTTS.Core.IPC;

/// <summary>
/// Manages the Python daemon process over stdin/stdout binary pipes.
///
/// Architecture:
///   - Dedicated blocking thread reads raw frames from stdout pipe.
///   - Frames are written into a Channel[IPCMessage] (lock-free handoff).
///   - Async dispatch loop reads from the channel and routes messages:
///       tts.start  → ISoundService.StartJob
///       tts.chunk  → ISoundService.Append
///       other      → pending correlation + event/method subscriber dispatch
///   - Watchdog timer fires DaemonNotRespondingException via IExceptionHandler
///     if no chunk arrives within ChunkTimeout during an active generation.
///
/// Protocol: [4-byte LE uint32 = payload length][msgpack bytes]
/// </summary>
public sealed class IPCService : IIPCService, IAsyncDisposable
{
    private readonly string                         _pythonExe;
    private readonly string                         _daemonScript;
    private readonly ISoundService                  _soundService;
    private readonly SDK.Exceptions.IExceptionHandler _exceptionHandler;

    private Process? _process;
    private Stream?  _stdin;
    private Stream?  _stdout;

    private readonly Channel<IPCMessage> _channel =
        Channel.CreateUnbounded<IPCMessage>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private Thread? _readerThread;
    private Task?   _dispatchTask;
    private CancellationTokenSource _cts = new();

    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPCMessage>>
        _pending = new();

    private readonly ConcurrentDictionary<string, List<Func<IPCMessage, Task>>>
        _handlers = new();

    private Timer?   _watchdog;
    private DateTime _lastChunkAt = DateTime.MinValue;
    private bool     _generationActive;

    private static readonly MessagePackSerializerOptions MsgPackOpts =
        MessagePackSerializerOptions.Standard;

    /// <summary>
    /// How long to wait without a tts.chunk before the watchdog fires.
    /// Only active during an ongoing generation job. Default: 30 seconds.
    /// </summary>
    public TimeSpan ChunkTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsConnected =>
        _process is { HasExited: false } && _stdin is not null;

    public event Func<IPCMessage, Task>? MessageReceived;

    public IPCService(
        string                            pythonExe,
        string                            daemonScript,
        ISoundService                     soundService,
        SDK.Exceptions.IExceptionHandler  exceptionHandler)
    {
        _pythonExe        = pythonExe;
        _daemonScript     = daemonScript;
        _soundService     = soundService;
        _exceptionHandler = exceptionHandler;
    }

    // Lifecycle

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo(_pythonExe, $"\"{_daemonScript}\"")
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        _process = Process.Start(psi)
            ?? throw new DaemonStartFailedException(_pythonExe, _daemonScript);

        _stdin  = _process.StandardInput.BaseStream;
        _stdout = _process.StandardOutput.BaseStream;

        _readerThread = new Thread(() => PipeReaderLoop(_stdout, _cts.Token))
        {
            IsBackground = true,
            Name         = "IPC.PipeReader",
        };
        _readerThread.Start();

        _dispatchTask = Task.Run(() => DispatchLoopAsync(_cts.Token), _cts.Token);

        _ = Task.Run(() => PipeStderrAsync(_process, _cts.Token), _cts.Token);

        _watchdog = new Timer(OnWatchdogTick, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _watchdog?.Dispose();
        _watchdog = null;

        _cts.Cancel();
        _channel.Writer.TryComplete();

        try { _stdin?.Close(); } catch { /* ignore */ }

        if (_process is { HasExited: false })
        {
            _process.Kill();
            await _process.WaitForExitAsync();
        }

        if (_dispatchTask is not null)
            await _dispatchTask.ConfigureAwait(false);

        _readerThread?.Join(TimeSpan.FromSeconds(2));

        _process?.Dispose();
        _process = null;
        _stdin   = null;
        _stdout  = null;
    }

    // Send

    public async Task SendAsync(string @event, object? data = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Daemon is not connected.");

        var msg = new IPCMessage { Event = @event, Data = data };
        await WriteFrameAsync(_stdin!, msg, _cts.Token);
    }

    public async Task<IPCMessage> SendAsync(string @event, object? data, TimeSpan timeout)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Daemon is not connected.");

        var msg = new IPCMessage { Event = @event, Data = data };
        var tcs = new TaskCompletionSource<IPCMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[msg.Id] = tcs;
        await WriteFrameAsync(_stdin!, msg, _cts.Token);

        using var timeoutCts = new CancellationTokenSource(timeout);
        await using var reg  = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(msg.Id, out var pending))
                pending.TrySetException(
                    new IPCTimeoutException(@event, msg.Id, timeout));
        });

        return await tcs.Task;
    }

    // Method-style subscription

    public void On(string @event, Func<IPCMessage, Task> handler) =>
        _handlers.GetOrAdd(@event, _ => []).Add(handler);

    public void Off(string @event, Func<IPCMessage, Task> handler)
    {
        if (_handlers.TryGetValue(@event, out var list))
            list.Remove(handler);
    }

    // Watchdog

    private void OnWatchdogTick(object? state)
    {
        if (!_generationActive) return;

        var elapsed = DateTime.UtcNow - _lastChunkAt;
        if (elapsed < ChunkTimeout) return;

        _generationActive = false;
        _exceptionHandler.Handle(
            new DaemonNotRespondingException(elapsed, ChunkTimeout));
    }

    // Pipe reader — dedicated blocking thread

    private void PipeReaderLoop(Stream stdout, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = ReadFrame(stdout);
                if (msg is null) break;
                _channel.Writer.TryWrite(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPC.PipeReader] {ex.Message}");
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    // Dispatch loop — async, thread pool

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        await foreach (var msg in _channel.Reader.ReadAllAsync(ct))
        {
            if (msg.Event == "tts.start")
            {
                _lastChunkAt      = DateTime.UtcNow;
                _generationActive = true;
                _soundService.StartJob(msg.Id);
                continue;
            }

            if (msg.Event == "tts.chunk" && msg.Data is IDictionary<string, object> d)
            {
                _lastChunkAt      = DateTime.UtcNow;
                _generationActive = true;

                var bytes = d.TryGetValue("bytes", out var b) ? (byte[])b : [];
                var final = d.TryGetValue("final", out var f) && (bool)f;

                _soundService.Append(msg.Id, bytes, final);

                if (final) _generationActive = false;
                continue;
            }

            if (_pending.TryRemove(msg.Id, out var tcs))
                tcs.TrySetResult(msg);

            if (MessageReceived is { } ev)
                await SafeInvokeAsync(() => ev(msg));

            await DispatchHandlersAsync(msg);
        }
    }

    private async Task DispatchHandlersAsync(IPCMessage msg)
    {
        if (_handlers.TryGetValue(msg.Event, out var specific))
            foreach (var h in specific.ToList())
                await SafeInvokeAsync(() => h(msg));

        if (_handlers.TryGetValue("*", out var wildcards))
            foreach (var h in wildcards.ToList())
                await SafeInvokeAsync(() => h(msg));
    }

    // Framing

    private static IPCMessage? ReadFrame(Stream stream)
    {
        var header = new byte[4];
        if (!ReadExact(stream, header)) return null;

        var length  = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var payload = new byte[length];
        if (!ReadExact(stream, payload)) return null;

        return MessagePackSerializer.Deserialize<IPCMessage>(payload, MsgPackOpts);
    }

    private static bool ReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private static async Task WriteFrameAsync(Stream stream, IPCMessage msg, CancellationToken ct)
    {
        var payload = MessagePackSerializer.Serialize(msg, MsgPackOpts);
        var header  = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task SafeInvokeAsync(Func<Task> fn)
    {
        try   { await fn(); }
        catch (Exception ex) { Debug.WriteLine($"[IPC] handler error: {ex.Message}"); }
    }

    private static async Task PipeStderrAsync(Process process, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !process.HasExited)
        {
            var line = await process.StandardError.ReadLineAsync(ct);
            if (line is null) break;
            Debug.WriteLine($"[daemon] {line}");
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
