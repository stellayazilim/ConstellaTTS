
namespace ConstellaTTS.SDK.Engine;

/// <summary>
/// One TTS engine registration in the catalog. The Id is what gets stored
/// on a <see cref="ISectionViewModel.EngineId"/>; the DisplayName is what
/// the model dropdown shows.
///
/// <see cref="SupportsTemperature"/> / <see cref="SupportsEmotion"/>
/// hint to the editor whether the engine consumes those knobs at all —
/// future engines that don't accept a temperature can have its slider
/// hidden rather than silently ignored.
/// </summary>
public sealed record EngineDescriptor(
    string Id,
    string DisplayName,
    bool   SupportsTemperature = true,
    bool   SupportsEmotion     = true,
    bool   RequiresVoiceSample = true);

/// <summary>
/// Read-only registry of available TTS engines. Implementations may load
/// from a manifest file, scan a plugin directory, or hard-code defaults.
/// The default <c>StaticEngineCatalog</c> ships a small built-in list
/// until the plugin system lands.
/// </summary>
public interface IEngineCatalog
{
    /// <summary>All registered engines in display order.</summary>
    IReadOnlyList<EngineDescriptor> Engines { get; }

    /// <summary>Look up an engine by id; null if not registered.</summary>
    EngineDescriptor? Find(string id);
}
