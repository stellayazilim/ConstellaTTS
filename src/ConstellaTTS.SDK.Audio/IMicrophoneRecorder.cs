namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Records audio from the system's default capture device into a
/// WAV file on disk. Stateful — implementations hold an open device
/// handle between <see cref="StartAsync"/> and <see cref="StopAsync"/>;
/// the same instance can serve sequential recordings as long as the
/// caller respects the start/stop pairing.
///
/// <para>
/// <b>Default device only at v1.</b> The recorder targets whatever
/// the OS currently has set as the default input device. A future
/// settings pane will swap this for explicit device selection;
/// until then, "the mic the OS is already using" is the right
/// default — it follows the user's existing system-level choice
/// without asking again inside the app.
/// </para>
///
/// <para>
/// <b>Format is fixed.</b> 44.1 kHz, mono, 16-bit PCM — what speech
/// samples actually need, what every microphone driver supports
/// without resampling, and what <c>WavStreamWriter</c> already
/// produces. A future iteration can expose the format as a
/// parameter if a use case ever justifies it.
/// </para>
///
/// <para>
/// <b>Streaming write.</b> Implementations forward incoming PCM
/// straight into <c>WavStreamWriter</c> as it arrives, so RAM
/// stays bounded regardless of recording length and a clean
/// <see cref="StopAsync"/> finalises the WAV header on the same
/// disk write that closes the file.
/// </para>
/// </summary>
public interface IMicrophoneRecorder
{
    /// <summary>
    /// True between a successful <see cref="StartAsync"/> and the
    /// matching <see cref="StopAsync"/>; false otherwise. UI binds
    /// to this for the "recording" indicator (a filled red button
    /// while live, hollow circle while idle).
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Begins capturing into a fresh WAV file at
    /// <paramref name="outputPath"/>. The caller picks the path —
    /// usually a tmp file under the project's <c>tmp/</c> directory
    /// while the user is still deciding whether to keep the
    /// recording. Throws if a session is already running or the
    /// default input device can't be opened.
    /// </summary>
    Task StartAsync(string outputPath);

    /// <summary>
    /// Stops the active session, finalises the WAV header, and
    /// returns the path that was written. Throws if no session is
    /// running. The caller decides what to do with the file next
    /// (move to <c>samples/</c> under a chosen name, delete on
    /// cancel, etc.).
    /// </summary>
    Task<string> StopAsync();
}
