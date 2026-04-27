namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Decodes an arbitrary input audio file into a stream of normalized
/// float samples in [-1, 1]. Implementations cover specific input
/// formats (MP3, WAV, …); the orchestrator picks the right one based
/// on file inspection.
///
/// <para>
/// The contract is deliberately pull-based: the caller asks for a
/// chunk, the decoder fills it, the caller writes it onward. This
/// matches how <see cref="Wav.WavStreamWriter"/> wants to be fed and
/// keeps memory bounded — the whole file never needs to live in RAM
/// at once.
/// </para>
///
/// <para>
/// Sample layout is interleaved when there are multiple channels:
/// L, R, L, R, … for stereo. Mono is one sample per frame. The
/// decoder reports its native channel count and sample rate via
/// <see cref="Format"/>; rate or channel conversion is the caller's
/// problem (and out of scope for v1).
/// </para>
/// </summary>
public interface IAudioDecoder : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Native format of the decoded stream. Stable for the lifetime
    /// of the decoder.
    /// </summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length interleaved float
    /// samples into the destination. Returns the number of samples
    /// actually read; 0 means end of stream. Short reads are legal
    /// and don't imply the stream has ended.
    /// </summary>
    int Read(Span<float> buffer);

    /// <summary>
    /// Async counterpart of <see cref="Read"/>. Same semantics.
    /// </summary>
    ValueTask<int> ReadAsync(
        Memory<float>     buffer,
        CancellationToken ct = default);
}
