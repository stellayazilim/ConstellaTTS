#if WINDOWS
using System.Buffers;
using System.Buffers.Binary;
using NAudio.Wave;

namespace ConstellaTTS.SDK.Audio.Mp3;

/// <summary>
/// MP3 decoder backed by NAudio's <c>Mp3FileReader</c>. Wraps it in
/// the project's <see cref="IAudioDecoder"/> shape so the rest of the
/// pipeline doesn't see the NAudio types.
///
/// <para>
/// <b>Windows-only.</b> NAudio's MP3 reader leans on the platform's
/// MP3 codec (ACM on Windows). This decoder is gated behind
/// <c>#if WINDOWS</c> in the project; a Linux/macOS counterpart
/// would use a pure-managed library like NLayer and live in a
/// parallel <c>#else</c> branch (or a sibling type), wiring into the
/// same interface.
/// </para>
///
/// <para>
/// NAudio surfaces decoded MP3 as 16-bit signed PCM (its standard
/// output format for the reader). We convert to normalized float on
/// the way out so callers — including <see cref="Wav.WavStreamWriter"/>
/// — see the same float contract every other decoder produces.
/// </para>
/// </summary>
public sealed class NAudioMp3Decoder : IAudioDecoder
{
    private readonly Mp3FileReader _reader;
    private bool                   _disposed;

    public AudioFormat Format { get; }

    public NAudioMp3Decoder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _reader = new Mp3FileReader(path);
        Format  = ToAudioFormat(_reader.WaveFormat);
        ValidateFormat(_reader.WaveFormat);
    }

    public NAudioMp3Decoder(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _reader = new Mp3FileReader(input);
        Format  = ToAudioFormat(_reader.WaveFormat);
        ValidateFormat(_reader.WaveFormat);
    }

    public int Read(Span<float> buffer)
    {
        ThrowIfDisposed();
        if (buffer.IsEmpty) return 0;

        // NAudio reads as 16-bit PCM bytes. Two bytes per sample.
        var byteCount = buffer.Length * sizeof(short);
        var rented    = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var bytesRead = _reader.Read(rented, 0, byteCount);
            if (bytesRead == 0) return 0;

            // Partial reads at the tail are normal — the last MP3 frame
            // doesn't always align to our buffer size. Round down to a
            // whole sample.
            var samplesRead = bytesRead / sizeof(short);
            ConvertInt16ToFloat(
                rented.AsSpan(0, samplesRead * sizeof(short)),
                buffer[..samplesRead]);
            return samplesRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask<int> ReadAsync(
        Memory<float>     buffer,
        CancellationToken ct = default)
    {
        // Mp3FileReader's underlying read isn't truly async — it
        // decodes on the calling thread. Hand it off to a worker so
        // the UI thread doesn't block on a long read; the work is
        // CPU-bound MP3 decoding either way.
        ct.ThrowIfCancellationRequested();
        return await Task.Run(() => Read(buffer.Span), ct).ConfigureAwait(false);
    }

    private static AudioFormat ToAudioFormat(WaveFormat wf) =>
        new(wf.Channels, wf.SampleRate);

    private static void ValidateFormat(WaveFormat wf)
    {
        if (wf.Channels is < 1 or > 2)
            throw new NotSupportedException(
                $"MP3 has {wf.Channels} channels; only mono and stereo are supported.");
        if (wf.BitsPerSample != 16)
            throw new InvalidOperationException(
                $"Mp3FileReader returned {wf.BitsPerSample}-bit PCM; expected 16.");
    }

    private static void ConvertInt16ToFloat(
        ReadOnlySpan<byte> source,
        Span<float>        destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            var s = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 2)..]);
            destination[i] = s / 32768f;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _reader.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
#endif
