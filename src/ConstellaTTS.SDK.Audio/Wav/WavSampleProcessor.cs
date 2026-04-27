using ConstellaTTS.SDK.Audio.Wav;

#if WINDOWS
using ConstellaTTS.SDK.Audio.Mp3;
#endif

namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Orchestrator that turns an arbitrary input file into a 16-bit PCM
/// WAV on disk. Picks an <see cref="IAudioDecoder"/> based on the
/// source file's extension, streams decoded float samples through a
/// <see cref="WavStreamWriter"/>, and raises completion / failure
/// events for any subscribers that care about the result.
///
/// <para>
/// <b>Pipeline shape.</b> The decoder reports the source's native
/// sample rate and channel count; the writer is opened with those
/// same values and passes the samples through unchanged. No
/// resampling, no channel mixing — the goal at v1 is "store what
/// the user gave us, in our preferred container". Resampling and
/// downmixing belong in a later processing stage when there's an
/// actual reason to enforce a project-wide format.
/// </para>
///
/// <para>
/// <b>Format dispatch.</b> Right now the lookup is a small switch on
/// extension: <c>.wav</c> → <see cref="WavStreamReader"/>,
/// <c>.mp3</c> → <see cref="NAudioMp3Decoder"/> (Windows only).
/// Adding a format means adding a case and a decoder type; nothing
/// else in the pipeline changes. When the format zoo grows, this
/// will graduate into a registry indexed by both extension and
/// magic bytes.
/// </para>
/// </summary>
public sealed class WavSampleProcessor : ISampleProcessor
{
    private const int ReadBufferSamples = 8192;

    public event EventHandler<SampleProcessedEventArgs>?    ProcessFinished;
    public event EventHandler<SampleProcessFailedEventArgs>? ProcessFailed;

    public async Task ProcessAsync(string sourcePath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        try
        {
            await using var decoder = OpenDecoder(sourcePath);
            await using var writer  = new WavStreamWriter(
                outputPath,
                decoder.Format.Channels,
                decoder.Format.SampleRate);

            await PumpAsync(decoder, writer).ConfigureAwait(false);

            ProcessFinished?.Invoke(
                this,
                new SampleProcessedEventArgs(sourcePath, outputPath));
        }
        catch (Exception ex)
        {
            ProcessFailed?.Invoke(
                this,
                new SampleProcessFailedEventArgs(sourcePath, ex));
            throw;
        }
    }

    private static IAudioDecoder OpenDecoder(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        return ext switch
        {
            ".wav" => new WavStreamReader(sourcePath),
#if WINDOWS
            ".mp3" => new NAudioMp3Decoder(sourcePath),
#endif
            _ => throw new NotSupportedException(
                $"No decoder registered for '{ext}' files.")
        };
    }

    private static async Task PumpAsync(IAudioDecoder decoder, WavStreamWriter writer)
    {
        // Pool the decode buffer — these calls run for the length of
        // the source file and we don't want a fresh array per chunk.
        var buffer = System.Buffers.ArrayPool<float>.Shared.Rent(ReadBufferSamples);
        try
        {
            while (true)
            {
                var read = await decoder.ReadAsync(buffer.AsMemory(0, ReadBufferSamples))
                                        .ConfigureAwait(false);
                if (read == 0) break;
                await writer.WriteAsync(buffer.AsMemory(0, read))
                            .ConfigureAwait(false);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
