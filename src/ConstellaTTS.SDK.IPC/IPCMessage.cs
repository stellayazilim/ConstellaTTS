using MessagePack;

namespace ConstellaTTS.SDK.IPC;

/// <summary>
/// Outgoing request frame — sent from C# to the Python daemon over the
/// control pipe.
///
/// Wire format: length-prefixed MessagePack ([4-byte LE uint32 length][payload]).
/// </summary>
/// <remarks>
/// A unique <see cref="Id"/> is generated automatically; the daemon echoes
/// it back in the matching <see cref="IPCResponse"/> so we can correlate
/// concurrent in-flight requests.
/// </remarks>
[MessagePackObject]
public sealed record IPCRequest
{
    /// <summary>Unique correlation ID, echoed in the response.</summary>
    [Key("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Dotted route, e.g. "echo.ping", "chatterbox_ml.generate".</summary>
    [Key("route")]
    public string Route { get; init; } = string.Empty;

    /// <summary>Arbitrary payload for the handler. Null or empty dict if unused.</summary>
    [Key("data")]
    public object? Data { get; init; }
}

/// <summary>
/// Incoming response frame — sent from the Python daemon to C# over the
/// control pipe.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="Data"/> (on success) or <see cref="Error"/>
/// (on failure) is populated; check <see cref="Ok"/> first.
/// </remarks>
[MessagePackObject]
public sealed record IPCResponse
{
    /// <summary>Correlation ID, matches the <see cref="IPCRequest.Id"/> that produced it.</summary>
    [Key("id")]
    public string? Id { get; init; }

    /// <summary>True if the handler ran without throwing.</summary>
    [Key("ok")]
    public bool Ok { get; init; }

    /// <summary>Handler return value. Null when <see cref="Ok"/> is false.</summary>
    [Key("data")]
    public object? Data { get; init; }

    /// <summary>Error info. Null when <see cref="Ok"/> is true.</summary>
    [Key("error")]
    public IPCError? Error { get; init; }
}

/// <summary>
/// One event from a streaming job, framed over the per-job stream pipe.
///
/// The daemon emits these as length-prefixed MessagePack frames of the
/// form <c>{"type": &lt;event&gt;, "data": &lt;dict&gt;}</c>. Well-known
/// event types include <c>"chunk"</c> (model output), <c>"done"</c>
/// (end of stream), <c>"error"</c> (handler raised), and
/// <c>"cancelled"</c> (admin cancelled via <c>*.cancel</c>).
/// </summary>
[MessagePackObject]
public sealed record IPCStreamEvent
{
    /// <summary>Event label, e.g. <c>"chunk"</c>, <c>"done"</c>, <c>"error"</c>.</summary>
    [Key("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Event payload. Shape depends on <see cref="Type"/>.</summary>
    [Key("data")]
    public object? Data { get; init; }

    /// <summary>True for <c>done</c>, <c>error</c>, <c>cancelled</c> — anything that ends the stream.</summary>
    [IgnoreMember]
    public bool IsTerminal => Type is "done" or "error" or "cancelled";
}

/// <summary>
/// Structured error envelope carried inside a failed <see cref="IPCResponse"/>.
/// </summary>
[MessagePackObject]
public sealed record IPCError
{
    /// <summary>Python exception class name, e.g. "UnknownRouteError".</summary>
    [Key("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    [Key("message")]
    public string Message { get; init; } = string.Empty;
}
