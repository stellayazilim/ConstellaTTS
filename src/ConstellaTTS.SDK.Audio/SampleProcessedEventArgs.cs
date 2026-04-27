namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Carried on <see cref="ISampleProcessor.ProcessFinished"/> when a
/// transcode finishes successfully. Source path is the input the
/// caller handed in (typically a tmp staging file); output path is
/// where the encoded result was written.
/// </summary>
public sealed record SampleProcessedEventArgs(
    string SourcePath,
    string OutputPath);
