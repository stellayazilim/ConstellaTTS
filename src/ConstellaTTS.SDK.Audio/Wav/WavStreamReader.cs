using System.Buffers;
using System.Buffers.Binary;

namespace ConstellaTTS.SDK.Audio.Wav;

/// <summary>
/// Native RIFF/WAVE decoder. Parses the header, locates the data
/// chunk, and exposes the PCM body as a stream of normalized float
/// samples in [-1, 1] via <see cref="IAudioDecoder"/>.
///
/// <para>
/// Supports 8-bit unsigned, 16-bit signed, 24-bit signed, and 32-bit
/// signed integer PCM, plus 32-bit IEEE float — the formats real-
/// world WAV files actually use. Mono and stereo for now; higher
/// channel counts would parse fine as bytes but the project layer
/// has no use for them. Extensible (WAVEFORMATEXTENSIBLE) headers
/// are read down to the underlying integer/float subformat; channel
/// masks are ignored because we only support 1–2 channels anyway.
/// </para>
///
/// <para>
/// Extra chunks (LIST, INFO, bext, …) between fmt and data are
/// skipped. Files that put data before fmt are rejected — they're
/// rare, technically legal, and adding two-pass support isn't worth
/// the complexity for a v1 decoder.
/// </para>
///
/// <para>
/// No native dependencies. Reading is buffered through the supplied
/// <see cref="Stream"/>; the reader doesn't care whether that's a
/// FileStream, MemoryStream, or anything else seekable enough to
/// reach the data chunk.
/// </para>
/// </summary>
public sealed class WavStreamReader : IAudioDecoder
{
    // Subset of WAVE format codes we recognize. The full list is
    // huge (Microsoft has registered hundreds of codecs); these are
    // the ones that show up in the wild for uncompressed audio.
    private const ushort WaveFormatPcm        = 0x0001;
    private const ushort WaveFormatIeeeFloat  = 0x0003;
    private const ushort WaveFormatExtensible = 0xFFFE;

    private readonly Stream _input;
    private readonly bool   _leaveOpen;
    private readonly ushort _formatCode;     // resolved (post-EXTENSIBLE)
    private readonly ushort _bitsPerSample;
    private readonly long   _dataEndOffset;  // absolute position of last byte + 1

    private bool _disposed;

    public AudioFormat Format { get; }

    /// <summary>
    /// Bits per sample as written in the fmt chunk. Useful for
    /// callers that want to log the file's source precision or
    /// build a metadata record without re-parsing the header.
    /// </summary>
    public int BitsPerSample => _bitsPerSample;

    /// <summary>
    /// Number of interchannel samples in the data chunk — i.e. the
    /// length in samples per channel. Multiply by the channel count
    /// for the total scalar count, divide by
    /// <see cref="AudioFormat.SampleRate"/> for the duration in
    /// seconds. Computed from the fmt + data chunks at parse time;
    /// no extra disk work to read.
    /// </summary>
    public long TotalFrames => _totalFrames;

    private readonly long _totalFrames;

    public WavStreamReader(Stream input, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException(
            "Input stream must be readable.", nameof(input));
        if (!input.CanSeek) throw new ArgumentException(
            "Input stream must be seekable; data chunk may not come first.",
            nameof(input));

        _input     = input;
        _leaveOpen = leaveOpen;

        ParseHeader(
            out var channels,
            out var sampleRate,
            out _formatCode,
            out _bitsPerSample,
            out var dataStart,
            out var dataLength);

        Format          = new AudioFormat(channels, sampleRate);
        _dataEndOffset  = dataStart + dataLength;
        _totalFrames    = dataLength / (channels * (_bitsPerSample / 8));
        _input.Position = dataStart;
    }

    public WavStreamReader(string path)
        : this(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read),
            leaveOpen: false)
    { }

    public int Read(Span<float> buffer)
    {
        ThrowIfDisposed();
        if (buffer.IsEmpty) return 0;

        // Round down to a whole frame; partial frames would desync
        // channel interleaving on the next call.
        var bytesPerSample = _bitsPerSample / 8;
        var samplesAvailable = (int)Math.Min(
            buffer.Length,
            (_dataEndOffset - _input.Position) / bytesPerSample);
        if (samplesAvailable == 0) return 0;

        var byteCount = samplesAvailable * bytesPerSample;
        var rented    = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var raw = rented.AsSpan(0, byteCount);
            ReadExact(raw);
            ConvertToFloat(raw, buffer[..samplesAvailable]);
            return samplesAvailable;
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
        ThrowIfDisposed();
        if (buffer.IsEmpty) return 0;

        var bytesPerSample = _bitsPerSample / 8;
        var samplesAvailable = (int)Math.Min(
            buffer.Length,
            (_dataEndOffset - _input.Position) / bytesPerSample);
        if (samplesAvailable == 0) return 0;

        var byteCount = samplesAvailable * bytesPerSample;
        var rented    = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            await ReadExactAsync(rented.AsMemory(0, byteCount), ct)
                .ConfigureAwait(false);
            ConvertToFloat(
                rented.AsSpan(0, byteCount),
                buffer.Span[..samplesAvailable]);
            return samplesAvailable;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void ParseHeader(
        out int    channels,
        out int    sampleRate,
        out ushort formatCode,
        out ushort bitsPerSample,
        out long   dataStart,
        out long   dataLength)
    {
        Span<byte> buf = stackalloc byte[12];

        // RIFF / WAVE preamble
        ReadExact(buf);
        if (!Match(buf[..4], "RIFF")) throw new InvalidDataException(
            "Not a RIFF file (missing 'RIFF' tag).");
        if (!Match(buf[8..12], "WAVE")) throw new InvalidDataException(
            "RIFF file is not a WAVE.");

        // Walk sub-chunks until we've seen fmt and data
        var foundFmt    = false;
        formatCode      = 0;
        channels        = 0;
        sampleRate      = 0;
        bitsPerSample   = 0;
        dataStart       = 0;
        dataLength      = 0;

        Span<byte> chunkHeader = stackalloc byte[8];
        while (_input.Position < _input.Length)
        {
            ReadExact(chunkHeader);
            var id   = chunkHeader[..4];
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);

            if (Match(id, "fmt "))
            {
                ReadFmtChunk(
                    (int)size,
                    out channels,
                    out sampleRate,
                    out formatCode,
                    out bitsPerSample);
                foundFmt = true;
            }
            else if (Match(id, "data"))
            {
                if (!foundFmt) throw new InvalidDataException(
                    "data chunk encountered before fmt; not supported.");
                dataStart  = _input.Position;
                dataLength = size;
                return;
            }
            else
            {
                // Unknown chunk — skip its body. Chunks are word-aligned,
                // so an odd size means one pad byte follows.
                var skip = size + (size & 1);
                _input.Seek(skip, SeekOrigin.Current);
            }
        }

        throw new InvalidDataException("WAV file has no data chunk.");
    }

    private void ReadFmtChunk(
        int        chunkSize,
        out int    channels,
        out int    sampleRate,
        out ushort formatCode,
        out ushort bitsPerSample)
    {
        if (chunkSize < 16) throw new InvalidDataException(
            $"fmt chunk too small ({chunkSize} bytes).");

        Span<byte> fmt = stackalloc byte[40]; // worst case: WAVEFORMATEXTENSIBLE
        var toRead     = Math.Min(chunkSize, fmt.Length);
        ReadExact(fmt[..toRead]);

        formatCode    = BinaryPrimitives.ReadUInt16LittleEndian(fmt[0..2]);
        channels      = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..4]);
        sampleRate    = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..8]);
        bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..16]);

        if (formatCode == WaveFormatExtensible)
        {
            if (toRead < 40) throw new InvalidDataException(
                "EXTENSIBLE fmt chunk truncated.");
            // Bytes 24..40 hold the SubFormat GUID. The first two
            // bytes of the GUID match the underlying format code.
            formatCode = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..26]);
        }

        // Skip any trailing fmt bytes we didn't read (rare)
        var leftover = chunkSize - toRead;
        if (leftover > 0) _input.Seek(leftover, SeekOrigin.Current);

        // Validate
        if (channels is < 1 or > 2) throw new NotSupportedException(
            $"Unsupported channel count: {channels}. Only mono and stereo.");
        if (sampleRate <= 0) throw new InvalidDataException(
            $"Invalid sample rate: {sampleRate}.");
        if (formatCode is not (WaveFormatPcm or WaveFormatIeeeFloat))
            throw new NotSupportedException(
                $"Unsupported WAVE format code: 0x{formatCode:X4}. " +
                "Only PCM and IEEE float are handled.");
        if (formatCode == WaveFormatPcm && bitsPerSample is not (8 or 16 or 24 or 32))
            throw new NotSupportedException(
                $"Unsupported PCM bit depth: {bitsPerSample}.");
        if (formatCode == WaveFormatIeeeFloat && bitsPerSample != 32)
            throw new NotSupportedException(
                $"IEEE float WAV must be 32-bit; got {bitsPerSample}.");
    }

    private void ConvertToFloat(ReadOnlySpan<byte> source, Span<float> destination)
    {
        switch (_formatCode, _bitsPerSample)
        {
            case (WaveFormatPcm, 8):
                // 8-bit WAV is unsigned: 0..255, midpoint 128.
                for (int i = 0; i < destination.Length; i++)
                    destination[i] = (source[i] - 128) / 128f;
                break;

            case (WaveFormatPcm, 16):
                for (int i = 0; i < destination.Length; i++)
                {
                    var s = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 2)..]);
                    destination[i] = s / 32768f;
                }
                break;

            case (WaveFormatPcm, 24):
                for (int i = 0; i < destination.Length; i++)
                {
                    // Sign-extend 24-bit little-endian into int32.
                    var off = i * 3;
                    int s = source[off]
                          | (source[off + 1] << 8)
                          | (source[off + 2] << 16);
                    if ((s & 0x00800000) != 0) s |= unchecked((int)0xFF000000);
                    destination[i] = s / 8388608f;
                }
                break;

            case (WaveFormatPcm, 32):
                for (int i = 0; i < destination.Length; i++)
                {
                    var s = BinaryPrimitives.ReadInt32LittleEndian(source[(i * 4)..]);
                    destination[i] = s / 2147483648f;
                }
                break;

            case (WaveFormatIeeeFloat, 32):
                for (int i = 0; i < destination.Length; i++)
                    destination[i] = BinaryPrimitives.ReadSingleLittleEndian(
                        source[(i * 4)..]);
                break;

            default:
                // Should be unreachable — header validation rejects others.
                throw new InvalidOperationException(
                    $"Unhandled format/bit-depth combo: " +
                    $"0x{_formatCode:X4} / {_bitsPerSample}.");
        }
    }

    private void ReadExact(Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var n = _input.Read(destination[read..]);
            if (n == 0) throw new EndOfStreamException(
                "Unexpected end of WAV stream.");
            read += n;
        }
    }

    private async ValueTask ReadExactAsync(
        Memory<byte>      destination,
        CancellationToken ct)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var n = await _input.ReadAsync(destination[read..], ct)
                                .ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException(
                "Unexpected end of WAV stream.");
            read += n;
        }
    }

    private static bool Match(ReadOnlySpan<byte> bytes, string ascii)
    {
        if (bytes.Length != ascii.Length) return false;
        for (int i = 0; i < ascii.Length; i++)
            if (bytes[i] != (byte)ascii[i]) return false;
        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_leaveOpen) _input.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!_leaveOpen) await _input.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }
}
