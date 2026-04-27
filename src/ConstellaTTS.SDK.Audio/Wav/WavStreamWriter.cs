using System.Buffers;
using System.Buffers.Binary;

namespace ConstellaTTS.SDK.Audio.Wav;

/// <summary>
/// Streaming RIFF/WAVE writer for 16-bit signed little-endian PCM.
/// Accepts normalized float samples in [-1, 1] and emits a standards-
/// compliant WAV file. Header length fields are written as
/// placeholders during construction and back-filled on dispose, so the
/// caller never needs to know the total sample count up front.
///
/// <para>
/// Scope is deliberately narrow: mono or stereo, 16-bit depth, integer
/// PCM. Speech-oriented sample libraries don't need more, and keeping
/// the surface tight makes the writer easy to verify against the WAV
/// spec. Higher bit depths or float formats can be added later by
/// branching on a format parameter; the on-disk layout for those is
/// the same RIFF skeleton with a different fmt chunk payload.
/// </para>
///
/// <para>
/// Not thread-safe. One writer per output stream, one caller at a
/// time. The underlying stream must be seekable — header finalization
/// requires seeking back to byte 4 and byte 40.
/// </para>
/// </summary>
public sealed class WavStreamWriter : IAsyncDisposable, IDisposable
{
    private const int RiffHeaderSize = 44;
    private const int RiffSizeOffset = 4;
    private const int DataSizeOffset = 40;

    private readonly Stream _output;
    private readonly bool   _leaveOpen;
    private readonly int    _channels;
    private readonly int    _sampleRate;

    private long _dataBytesWritten;
    private bool _disposed;

    /// <summary>
    /// Wraps an existing stream. Caller controls the stream's lifetime
    /// when <paramref name="leaveOpen"/> is true; otherwise the writer
    /// disposes it on shutdown.
    /// </summary>
    public WavStreamWriter(
        Stream output,
        int    channels,
        int    sampleRate,
        bool   leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException(
            "Output stream must be writable.", nameof(output));
        if (!output.CanSeek)  throw new ArgumentException(
            "Output stream must be seekable for header finalization.",
            nameof(output));
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(
            nameof(channels), channels,
            "Only mono (1) and stereo (2) are supported.");
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(
            nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        _output     = output;
        _leaveOpen  = leaveOpen;
        _channels   = channels;
        _sampleRate = sampleRate;

        WriteHeaderPlaceholder();
    }

    /// <summary>
    /// Convenience constructor for file output. Creates or overwrites
    /// the file at <paramref name="path"/>.
    /// </summary>
    public WavStreamWriter(
        string path,
        int    channels,
        int    sampleRate)
        : this(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
            channels,
            sampleRate,
            leaveOpen: false)
    { }

    /// <summary>
    /// Writes interleaved float samples (range [-1, 1]) as 16-bit PCM.
    /// For stereo the layout is L, R, L, R, ...; the buffer length must
    /// be a multiple of the channel count.
    /// </summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        ThrowIfDisposed();
        if (samples.IsEmpty) return;
        if (samples.Length % _channels != 0) throw new ArgumentException(
            $"Sample count ({samples.Length}) is not a multiple of channel count ({_channels}).",
            nameof(samples));

        var byteCount = samples.Length * sizeof(short);
        var rented    = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var dest = rented.AsSpan(0, byteCount);
            ConvertFloatToInt16(samples, dest);
            _output.Write(dest);
            _dataBytesWritten += byteCount;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Async counterpart of <see cref="Write"/>. Same buffer layout
    /// rules apply.
    /// </summary>
    public async ValueTask WriteAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken     ct = default)
    {
        ThrowIfDisposed();
        if (samples.IsEmpty) return;
        if (samples.Length % _channels != 0) throw new ArgumentException(
            $"Sample count ({samples.Length}) is not a multiple of channel count ({_channels}).",
            nameof(samples));

        var byteCount = samples.Length * sizeof(short);
        var rented    = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            ConvertFloatToInt16(samples.Span, rented.AsSpan(0, byteCount));
            await _output.WriteAsync(rented.AsMemory(0, byteCount), ct)
                         .ConfigureAwait(false);
            _dataBytesWritten += byteCount;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ConvertFloatToInt16(
        ReadOnlySpan<float> source,
        Span<byte>          destination)
    {
        // Symmetric mapping: ±1.0 → ±32767. Clamping at write time is
        // cheaper than asking callers to pre-validate, and the cost
        // (one branch per sample) disappears next to the disk write.
        for (int i = 0; i < source.Length; i++)
        {
            var f      = Math.Clamp(source[i], -1f, 1f);
            var sample = (short)(f * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(
                destination[(i * 2)..], sample);
        }
    }

    private void WriteHeaderPlaceholder()
    {
        Span<byte> header = stackalloc byte[RiffHeaderSize];

        // RIFF chunk
        WriteAscii(header, 0, "RIFF");
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0u); // size, fixed up on dispose
        WriteAscii(header, 8, "WAVE");

        // fmt sub-chunk (PCM, 16 bytes)
        WriteAscii(header, 12, "fmt ");
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 16u);          // fmt chunk size
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], 1);            // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..], (ushort)_channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)_sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..],
            (uint)(_sampleRate * _channels * sizeof(short)));                 // byte rate
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..],
            (ushort)(_channels * sizeof(short)));                             // block align
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..], 16);           // bits per sample

        // data sub-chunk
        WriteAscii(header, 36, "data");
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 0u);           // data size, fixed up on dispose

        _output.Write(header);
    }

    private void FinalizeHeader()
    {
        // RIFF size is total file size minus 8 (the "RIFF" tag and the
        // size field itself). data size is just the PCM byte count.
        var riffSize = (uint)(RiffHeaderSize + _dataBytesWritten - 8);
        var dataSize = (uint)_dataBytesWritten;

        Span<byte> scratch = stackalloc byte[4];

        _output.Seek(RiffSizeOffset, SeekOrigin.Begin);
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, riffSize);
        _output.Write(scratch);

        _output.Seek(DataSizeOffset, SeekOrigin.Begin);
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, dataSize);
        _output.Write(scratch);

        _output.Seek(0, SeekOrigin.End);
        _output.Flush();
    }

    private static void WriteAscii(Span<byte> dest, int offset, string ascii)
    {
        for (int i = 0; i < ascii.Length; i++)
            dest[offset + i] = (byte)ascii[i];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            FinalizeHeader();
        }
        finally
        {
            if (!_leaveOpen) _output.Dispose();
            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            FinalizeHeader();
            await _output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            if (!_leaveOpen) await _output.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }
    }
}
