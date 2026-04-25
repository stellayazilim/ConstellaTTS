namespace ConstellaTTS.SDK.IO;

/// <summary>
/// Describes the shape of raw PCM audio data. Required when encoding raw
/// PCM (which carries no header). Returned alongside decoded PCM so
/// downstream consumers know how to interpret the byte stream.
/// </summary>
/// <param name="SampleRate">Samples per second (e.g. 44100, 48000).</param>
/// <param name="Channels">Number of audio channels (1 = mono, 2 = stereo).</param>
/// <param name="BitDepth">Bits per sample (commonly 16, 24, or 32).</param>
public sealed record AudioFormat(int SampleRate, int Channels, int BitDepth);
