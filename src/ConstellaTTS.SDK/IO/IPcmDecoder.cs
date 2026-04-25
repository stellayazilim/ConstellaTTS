namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Decodes encoded audio bytes into raw PCM. The audio format is parsed from
/// the encoded payload's header (FLAC, MP3, WAV all carry format metadata),
/// so callers do not need to supply it up front.
/// </summary>
public interface IPcmDecoder
{
    Task<(byte[] pcm, AudioFormat format)> DecodeAsync(byte[] encoded);

    Task<(Stream pcm, AudioFormat format)> DecodeAsync(Stream encoded);
}
