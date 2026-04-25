using System;

namespace ConstellaTTS.Domain;

/// <summary>
/// Sidecar metadata for a stored voice sample. Lives next to the audio
/// file as a JSON document; the sample's stable identity is the file's
/// stem (a GUID), and this record carries everything the UI needs to
/// label the sample without reading the audio file itself.
///
/// <para>
/// <b>Why a sidecar instead of embedding in the audio.</b> Audio
/// containers (WAV, FLAC) do support metadata, but reading it
/// reliably across formats means owning a parser per format. Keeping
/// the metadata in a plain JSON file lets every part of the system
/// — the .NET app, the Python daemon, future scripts, the user with
/// a text editor — read and write sample annotations without an
/// audio library round-trip.
/// </para>
///
/// <para>
/// <b>Layout on disk.</b> Each sample is two files in the project's
/// samples directory:
/// <code>
///   samples/{guid}.flac      — the audio (FLAC for now; WAV in MVP)
///   samples/{guid}.json      — this record, serialised
/// </code>
/// The pair is loaded together by <c>FileSampleProvider</c> at startup
/// and rewritten together when the user edits a sample's label.
/// </para>
///
/// <para>
/// <b>In-place mode.</b> <see cref="ExternalPath"/> lets a user
/// reference an audio file living somewhere else on disk (e.g. a
/// directory of recordings they don't want to copy). When set, the
/// FLAC/WAV next to the JSON is skipped on load and the engine reads
/// from <see cref="ExternalPath"/> instead. The current MVP always
/// imports a copy and leaves <see cref="ExternalPath"/> null; the
/// field is included so the on-disk schema doesn't need to change
/// when in-place mode lands.
/// </para>
/// </summary>
public sealed class SampleManifest
{
    /// <summary>
    /// Stable identity. Matches the file stem on disk (the JSON itself
    /// lives at <c>{Id}.json</c>, the audio at <c>{Id}.flac</c>).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable label shown in the picker. Defaults to the
    /// original imported filename's stem at upload time; the user can
    /// rename it later without renaming the file on disk.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Original file path the user picked at import time. Purely
    /// informational — used to show "imported from …" in tooltips —
    /// not for reading audio. <see cref="ExternalPath"/> is what
    /// engines read when in-place mode is active.
    /// </summary>
    public string? OriginalSourcePath { get; set; }

    /// <summary>
    /// Path to the actual audio file when in-place mode is active.
    /// Null in the default copy-on-import flow; engines fall back to
    /// the colocated <c>{Id}.flac</c> next to this JSON. When set,
    /// engines must read from this path instead.
    /// </summary>
    public string? ExternalPath { get; set; }

    /// <summary>UTC timestamp the sample was added to the library.</summary>
    public DateTime AddedUtc { get; set; }

    /// <summary>Duration of the audio in seconds, cached at import.</summary>
    public double DurationSec { get; set; }

    /// <summary>Sample rate of the audio in Hz, cached at import.</summary>
    public int SampleRateHz { get; set; }

    /// <summary>
    /// Channel count, cached at import. Voice cloning engines expect
    /// mono; the importer downmixes on the way in, so this is almost
    /// always 1, but the field exists in case someone hand-edits a
    /// stereo sample in via in-place mode.
    /// </summary>
    public int Channels { get; set; }
}
