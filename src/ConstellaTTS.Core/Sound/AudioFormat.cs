namespace ConstellaTTS.Core.Sound;

/// <summary>
/// Describes the raw PCM format of audio data produced by the TTS engine.
/// Used to convert between timeline positions (seconds) and buffer offsets (bytes).
/// </summary>
public sealed record AudioFormat
{
    /// <summary>Samples per second. Chatterbox ML default: 24000.</summary>
    public int SampleRate { get; init; } = 24000;

    /// <summary>Number of audio channels. TTS output is typically mono (1).</summary>
    public int Channels { get; init; } = 1;

    /// <summary>Bits per sample. float32 PCM = 32.</summary>
    public int BitsPerSample { get; init; } = 32;

    public int BytesPerSample => BitsPerSample / 8;

    /// <summary>Converts a timeline position in seconds to a byte offset in the raw buffer.</summary>
    public int SecondsToBytes(double seconds) =>
        (int)(seconds * SampleRate * Channels * BytesPerSample);

    /// <summary>Converts a raw buffer byte offset back to a timeline position in seconds.</summary>
    public double BytesToSeconds(int byteOffset) =>
        (double)byteOffset / (SampleRate * Channels * BytesPerSample);
}
