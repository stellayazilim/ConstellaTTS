#if WINDOWS
using System.Buffers.Binary;
using ConstellaTTS.SDK.Audio.Wav;
using NAudio.Wave;

namespace ConstellaTTS.SDK.Audio.Recording;

/// <summary>
/// <see cref="IMicrophoneRecorder"/> backed by NAudio's
/// <see cref="WaveInEvent"/>. Captures from the system's default
/// input device at 44.1 kHz / mono / 16-bit and forwards each
/// buffer straight into a <see cref="WavStreamWriter"/> on the
/// caller-supplied path.
///
/// <para>
/// <b>Windows-only.</b> NAudio's WaveIn talks to the Windows
/// multimedia API; non-Windows builds skip this file entirely
/// through the project's <c>WINDOWS</c> compile constant. A
/// cross-platform sibling (PortAudio, OpenAL, miniaudio) would
/// implement the same interface in an <c>#else</c> branch.
/// </para>
///
/// <para>
/// <b>Sample conversion.</b> The capture device hands us raw 16-bit
/// PCM bytes; <see cref="WavStreamWriter"/> wants normalized floats
/// in [-1, 1]. We convert in the data-available callback rather
/// than holding the bytes for later — the buffer the device hands
/// over is reused on the next callback, so any deferred work would
/// have to copy first. Converting on the spot also keeps RAM
/// bounded regardless of recording length.
/// </para>
/// </summary>
public sealed class NAudioMicrophoneRecorder : IMicrophoneRecorder
{
    private const int SampleRate    = 44100;
    private const int Channels      = 1;
    private const int BitsPerSample = 16;

    private WaveInEvent?    _waveIn;
    private WavStreamWriter? _writer;
    private string?          _outputPath;

    public bool IsRecording => _waveIn is not null;

    public Task StartAsync(string outputPath)
    {
        if (_waveIn is not null)
            throw new InvalidOperationException(
                "A recording session is already running.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(
                "Output path must be provided.", nameof(outputPath));

        // Make sure the directory exists — the caller is meant to
        // have ensured that, but a missing directory at this point
        // would surface as a confusing FileNotFoundException out of
        // FileStream's constructor.
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _outputPath = outputPath;
        _writer     = new WavStreamWriter(outputPath, Channels, SampleRate);

        _waveIn = new WaveInEvent
        {
            WaveFormat         = new WaveFormat(SampleRate, BitsPerSample, Channels),
            // 100 ms callback granularity — small enough that Stop
            // reacts promptly, large enough to keep callback overhead
            // negligible on the audio thread.
            BufferMilliseconds = 100,
        };
        _waveIn.DataAvailable    += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();

        return Task.CompletedTask;
    }

    public async Task<string> StopAsync()
    {
        var waveIn = _waveIn
            ?? throw new InvalidOperationException(
                "No recording session is running.");

        // StopRecording is asynchronous — it queues a final
        // DataAvailable callback (with the tail of the buffer) and
        // then a RecordingStopped event on the threadpool. We let
        // that drain naturally; the writer dispose below blocks
        // until both have run because the callbacks acquire no
        // lock we hold here.
        waveIn.StopRecording();

        // Detach handlers and dispose the device. Order matters:
        // unsubscribe before dispose so a late callback can't fire
        // into a half-disposed writer.
        waveIn.DataAvailable    -= OnDataAvailable;
        waveIn.RecordingStopped -= OnRecordingStopped;
        waveIn.Dispose();

        // Flush and finalize the WAV header. DisposeAsync writes the
        // RIFF / data size fields back into the header bytes, so the
        // file on disk only becomes a valid playable WAV after this
        // returns.
        if (_writer is not null)
            await _writer.DisposeAsync().ConfigureAwait(false);

        var path = _outputPath
            ?? throw new InvalidOperationException(
                "Recorder finished with no output path tracked.");

        _waveIn     = null;
        _writer     = null;
        _outputPath = null;

        return path;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var writer = _writer;
        if (writer is null) return;

        // NAudio's buffer is reused across callbacks — we have to
        // consume it inside this method, not stash it for later. The
        // buffer length isn't always == BytesRecorded; trailing space
        // belongs to the next callback. Honouring BytesRecorded keeps
        // the WAV from picking up stale samples between captures.
        var bytes        = e.Buffer.AsSpan(0, e.BytesRecorded);
        var sampleCount  = bytes.Length / sizeof(short);
        Span<float> samples = sampleCount <= 1024
            ? stackalloc float[sampleCount]
            : new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            var s = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]);
            samples[i] = s / 32768f;
        }

        writer.Write(samples);
    }

    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // The WaveIn driver raises this once it has flushed the last
        // DataAvailable. We don't need to do anything here today —
        // StopAsync owns teardown — but exposing the hook keeps the
        // door open for status reporting and lets us see driver
        // errors in the e.Exception field if one ever surfaces.
        // (No logger injected here yet; ILogger plumbing arrives
        // when this layer needs anything else from it.)
    }
}
#endif
