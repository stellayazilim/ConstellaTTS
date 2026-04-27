namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Carried on <see cref="ISampleManager"/> mutation events. Wraps
/// the affected <see cref="Sample"/>; the event itself communicates
/// the kind of change (added vs removed).
/// </summary>
public sealed record SampleEventArgs(Sample Sample);
