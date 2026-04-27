namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// A processed sample stored in the active project's <c>samples/</c>
/// directory. Identity is the on-disk path; everything else is
/// metadata the UI uses to render rows and the engine uses to plan
/// playback.
///
/// <para>
/// <b>Lightweight by design.</b> The record carries only what's
/// already known after the encode step (path, basic format facts).
/// Heavier metadata — waveform peaks, tag-extracted display names,
/// transcript references — gets layered on by other services once
/// the sample is live, rather than packed into this contract. Keeps
/// the manager-to-UI hand-off cheap and the contract durable.
/// </para>
///
/// <para>
/// Output format is implicitly WAV at the moment; once a codec
/// abstraction returns, this record will gain a codec field again.
/// </para>
/// </summary>
public sealed record Sample(
    string Path,
    string OriginalName,
    double DurationSec,
    int    SampleRate,
    int    Channels);
