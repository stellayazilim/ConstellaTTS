
using ConstellaTTS.SDK.Engine;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Hard-coded built-in engine list. Enough to make the editor functional
/// today; will be replaced by a manifest-driven catalog once the plugin
/// system lands. The list mirrors the engines we actually plan to ship
/// adapters for, in the order they're prioritised on the roadmap.
///
/// Engines marked <c>RequiresVoiceSample = false</c> hide the sample
/// picker in the editor — useful for hypothetical text-only engines or
/// "default voice" presets. Every engine here currently expects a sample
/// because the project's first-class story is voice cloning.
/// </summary>
public sealed class StaticEngineCatalog : IEngineCatalog
{
    public IReadOnlyList<EngineDescriptor> Engines { get; } =
    [
        new("Chatterbox", "Chatterbox ML"),
        new("F5-TTS",     "F5-TTS"),
        new("IndexTTS-2", "IndexTTS-2"),
        new("XTTSv2",     "XTTS v2"),
    ];

    public EngineDescriptor? Find(string id) =>
        Engines.FirstOrDefault(e => e.Id == id);
}
