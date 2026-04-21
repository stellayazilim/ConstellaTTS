using MessagePack;

namespace ConstellaTTS.SDK.IPC;

/// <summary>
/// Binary message exchanged with the Python daemon over stdin/stdout.
/// Serialized with MessagePack. Framing: [4-byte LE uint32 length][msgpack bytes].
/// </summary>
[MessagePackObject]
public sealed record IPCMessage
{
    /// <summary>Unique message ID. Used to correlate requests with responses.</summary>
    [Key("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Event name, e.g. "ping", "tts.generate".</summary>
    [Key("event")]
    public string Event { get; init; } = string.Empty;

    /// <summary>Arbitrary payload. Null for events with no data.</summary>
    [Key("data")]
    public object? Data { get; init; }
}
