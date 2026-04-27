namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Native format of a decoded audio stream. Channel count and sample
/// rate are reported as the source had them; the caller decides
/// whether to keep, downmix, or resample on the encode side.
/// </summary>
public readonly record struct AudioFormat(
    int Channels,
    int SampleRate);
