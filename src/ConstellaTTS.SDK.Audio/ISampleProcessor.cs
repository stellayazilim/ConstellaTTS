namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Decodes an arbitrary input audio file and re-encodes it to the
/// project's storage format on disk. Stateless — every call carries
/// its own source / output paths, so the same processor instance can
/// serve many concurrent or sequential conversions without ordering
/// surprises.
///
/// <para>
/// <b>Output is WAV at the moment.</b> The processor takes whatever
/// the registered <see cref="IAudioDecoder"/> set can read (WAV
/// natively, MP3 via NAudio on Windows, more later) and writes
/// linear 16-bit PCM WAV. Lossless storage relative to the decoded
/// signal, no native dependencies on the encode side. A FLAC encoder
/// is on the roadmap as a separate pure-managed library; when it
/// lands, this contract will gain an output-format parameter.
/// </para>
///
/// <para>
/// <b>Async with events.</b> Transcode is fundamentally async (read,
/// decode, encode, write — none of which we want blocking the UI
/// thread) but the caller usually wants to react to <i>completion</i>
/// rather than awaiting a Task. Pipelines like "user clicks Upload →
/// file lands in tmp → processor runs → UI shows the new sample"
/// don't naturally compose into a single awaited call, especially
/// when several uploads are in flight at once. The events let any
/// number of subscribers (the manager that moves the file into the
/// project, the view-model that toasts "ready", a future logger)
/// react without changing the call shape.
/// </para>
///
/// <para>
/// <b>Why both async and events.</b> The Task return tells the
/// caller "kicking off a process" is complete (inputs validated,
/// scheduled). The events tell <i>everyone</i> "the actual encode
/// finished or failed". This split matches how callers think about
/// the operation: the upload command awaits to know its job is
/// queued; the rest of the app reacts to events to know the result
/// is on disk.
/// </para>
/// </summary>
public interface ISampleProcessor
{
    /// <summary>
    /// Decode <paramref name="sourcePath"/> and write the result to
    /// <paramref name="outputPath"/>. Either raises
    /// <see cref="ProcessFinished"/> on success or
    /// <see cref="ProcessFailed"/> on any decode/encode/IO error.
    /// The Task completes when the encode work itself is done; the
    /// matching event fires from the same call frame.
    /// </summary>
    Task ProcessAsync(string sourcePath, string outputPath);

    /// <summary>
    /// Raised after a successful <see cref="ProcessAsync"/>. Argument
    /// carries source / output paths for subscribers that want to
    /// log or move files around.
    /// </summary>
    event EventHandler<SampleProcessedEventArgs>? ProcessFinished;

    /// <summary>
    /// Raised when <see cref="ProcessAsync"/> raises. The Task
    /// returned by ProcessAsync also faults with the same exception,
    /// so callers can choose between the awaited path and the event
    /// path depending on how they want to react.
    /// </summary>
    event EventHandler<SampleProcessFailedEventArgs>? ProcessFailed;
}
