namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Encodes raw PCM audio data into a codec-specific byte representation.
/// Implementations choose their own format (FLAC, MP3, Opus, WAV, ...).
/// </summary>
public interface IPcmEncoder
{
    Task<byte[]> EncodeAsync(byte[] pcm, AudioFormat format);

    Task<Stream> EncodeAsync(Stream pcm, AudioFormat format);
}
