namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Carried on <see cref="ISampleProcessor.ProcessFailed"/> when a
/// transcode raises. The exception is exposed verbatim so callers
/// that want to surface it (status text, error toast) have the
/// underlying message; the source path is repeated for log
/// correlation when the subscriber doesn't want to keep the
/// original request lying around.
/// </summary>
public sealed record SampleProcessFailedEventArgs(
    string    SourcePath,
    Exception Exception);
