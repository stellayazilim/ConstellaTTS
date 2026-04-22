using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using MessagePack;

namespace ConstellaTTS.SDK.IPC;

/// <summary>
/// Active subscription to a streaming job's per-job pipe.
///
/// Returned by <see cref="IIPCService.StartStreamAsync"/>. Consume events
/// with <see cref="ReadEventsAsync"/>, then either let them run to the
/// terminal <c>done</c> event or call <see cref="CancelAsync"/> to ask
/// the daemon to abort early.
///
/// The stream always owns its own pipe; disposing it closes that pipe.
/// Disposal without an explicit <see cref="CancelAsync"/> closes the pipe
/// locally but does NOT formally tell the daemon to cancel — the daemon
/// will notice the client is gone and tear down on its own, but
/// <see cref="CancelAsync"/> is the clean way to stop early.
/// </summary>
public sealed class IPCStream : IAsyncDisposable
{
    private readonly IIPCService _client;
    private readonly NamedPipeClientStream _pipe;
    private readonly string _route;      // originating route, e.g. "fake"
    private readonly string _jobId;
    private bool _disposed;

    internal IPCStream(
        IIPCService client,
        NamedPipeClientStream pipe,
        string route,
        string jobId)
    {
        _client = client;
        _pipe = pipe;
        _route = route;
        _jobId = jobId;
    }

    /// <summary>The job id returned by the daemon when the stream was started.</summary>
    public string JobId => _jobId;

    /// <summary>The route prefix this stream belongs to (e.g. <c>"fake"</c>).</summary>
    public string Route => _route;

    /// <summary>
    /// Enumerate events as the daemon emits them.
    ///
    /// Terminates naturally when the daemon emits a terminal event
    /// (<c>done</c>, <c>error</c>, <c>cancelled</c>) or when the pipe
    /// closes. Cancellation via <paramref name="ct"/> throws
    /// <see cref="OperationCanceledException"/> without cancelling on
    /// the daemon side — pair with <see cref="CancelAsync"/> if you
    /// want the server work stopped too.
    /// </summary>
    public async IAsyncEnumerable<IPCStreamEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var headerBuf = new byte[4];

        while (!ct.IsCancellationRequested)
        {
            // Read 4-byte length header.
            if (!await ReadExactlyAsync(headerBuf, ct))
                yield break;   // clean EOF

            var length = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf);
            if (length == 0 || length > 16 * 1024 * 1024)
                throw new InvalidDataException($"Bad stream frame length: {length}");

            var body = new byte[length];
            if (!await ReadExactlyAsync(body, ct))
                throw new EndOfStreamException("Stream pipe closed mid-frame");

            var evt = MessagePackSerializer.Deserialize<IPCStreamEvent>(body);
            yield return evt;

            if (evt.IsTerminal)
                yield break;
        }
    }

    /// <summary>
    /// Ask the daemon to cancel this job, then close the local pipe.
    ///
    /// Sends <c>{route}.cancel</c> over the control pipe with
    /// <c>{"job_id": ...}</c>, then disposes. Safe to call after the
    /// stream has already finished — the cancel request simply returns
    /// a <c>JobNotFoundError</c> which is ignored.
    /// </summary>
    public async Task CancelAsync(CancellationToken ct = default)
    {
        if (_disposed) return;

        try
        {
            await _client.RequestAsync(
                $"{_route}.cancel",
                new Dictionary<string, object> { ["job_id"] = _jobId },
                timeout: TimeSpan.FromSeconds(5),
                ct: ct);
        }
        catch
        {
            // Best-effort — the job may have already completed or the
            // daemon may be unreachable. Either way we still close our
            // side below.
        }

        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _pipe.Close(); } catch { /* ignore */ }
        try { await _pipe.DisposeAsync(); } catch { /* ignore */ }
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private async Task<bool> ReadExactlyAsync(byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await _pipe.ReadAsync(buffer.AsMemory(offset), ct);
            if (n == 0) return offset == 0;   // clean EOF at frame boundary
            offset += n;
        }
        return true;
    }
}
