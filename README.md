# ConstellaTTS Studio

> A DAW-based, plugin-driven TTS editor and audio production pipeline, built specifically for the Viteria universe.

ConstellaTTS Studio is a local-first desktop application for generating in-game dialogue, cutscene narration, and character voices. It works like a MIDI piano roll: each spoken sentence is a "section" positioned on a timeline, generated independently, and assembled into a final audio output.

**Stack:** Avalonia UI + C# · CommunityToolkit.Mvvm · Python IPC daemon · NAudio

---

## Architecture

### Slot System

Every window declares a `SlotMap` — a tree of named slots typed by `SlotType`:

| Type | Description |
|------|-------------|
| `Window` | Top-level window |
| `Layout` | Container that defines child slots |
| `Page` | Full view, exposes its own child slots when mounted |
| `Control` | Leaf node — no children |

```
MainWindow
  └── LayoutSlot [ContentControl]
        └── MainLayout              ← mounted at startup
              ├── ToolbarSlot       ← plugins add controls here
              └── ViewToolsSlot     ← active view's contextual tools
```

### Navigation

```csharp
app.NavigationManager.Navigate(nav => nav
    .OpenWindow<SectionEditorWindow>()
    .SwapLayout<SectionEditorLayout>()
    .Mount(Slots.Content, typeof(SectionEditorView))
    .Mount(Slots.Toolbar, typeof(SectionToolbarView)));
```

Every navigation is recorded in `IHistoryManager` — undoable with Ctrl+Z.

### Plugin System

```csharp
public class MyPlugin : IConstellaModule
{
    public string Id   => "com.example.myplugin";
    public string Name => "My Plugin";

    // Assembly deps — topological load order
    public IReadOnlyList<Assembly> Dependencies =>
        [typeof(ConstellaTTSCoreModule).Assembly];

    public void Build(IServiceCollection services)
    {
        services.AddSingleton<MyView>();
        // Optional: deferred UI mount
        // windowManager.DeferMount(w => { ... });
    }
}
```

Plugins are discovered from `./plugins/` — DLLs scanned, `IConstellaModule` implementations instantiated, sorted by dependency graph, then `Build()` called in order.

### IPC Daemon

```
Avalonia (C#)
    ↕  stdin/stdout JSON lines
Python daemon (single process, model cache)
    ↕
TTS models — lazy load, LRU eviction
    ↓
WAV chunk → NAudio → speaker
```

---

## TTS Engine Matrix

| Engine | Turkish | VRAM | Duration Strategy | License |
|--------|---------|------|-------------------|---------|
| Chatterbox ML | ✅ | ~4–6 GB | Auto, Rubberband | MIT |
| F5-TTS | ✅ community | ~6 GB | Auto, Rubberband, Interpolate | MIT |
| Fish S2 local | ✅ 83 langs | ~12 GB (NF4) | Auto, Rubberband | Source-available* |
| XTTS-v2 | ✅ native | ~6–8 GB | Auto, Rubberband, Interpolate | CPML* |
| IndexTTS-2 | ❌ | ~8 GB | All | Research |
| Whisper large-v3-turbo | — | ~6 GB | — (QC only) | MIT |

*Fish S2 and XTTS-v2 have commercial restrictions — Chatterbox and F5-TTS are clear for commercial use.

---

## Getting Started

```bash
git clone https://github.com/stellayazilim/ConstellaTTS.git
cd ConstellaTTS
dotnet restore
dotnet run --project src/ConstellaTTS.Avalonia
```

Place plugin DLLs in `./plugins/` next to the executable.

---

## Writing a Plugin

1. Create a class library targeting `net10.0`
2. Add `ConstellaTTS.SDK` NuGet reference — Avalonia comes transitively
3. Implement `IConstellaModule`
4. Register services in `Build()`
5. Optionally mount UI into existing slots via `DeferMount` or post-`GetDefaultWindow()`
6. Drop compiled DLL into `./plugins/`

---

## Roadmap

| Phase | Focus |
|-------|-------|
| **Now** | Domain model (Section, Track, Project), `.wwv` file format |
| **Next** | Timeline UI, section views, emotion color system |
| **Soon** | Python daemon, first adapter (Chatterbox), NAudio queue |
| **Later** | Plugin manifest system, adapter generator, drag & drop model install |
| **Future** | Duration control (Rubberband, ScalerGAN, IndexTTS-2), export pipeline |

---

## Progress Checklist

### ✅ Platform & Architecture
- [x] Avalonia UI selected (Skia-based, cross-platform, WPF heritage)
- [x] Solution structure — SDK / Core / Avalonia separation
- [x] Plugin module system — `IConstellaModule`, `ConstellaModuleRegistry`, topological sort
- [x] `ConstellaApp` singleton — central application context
- [x] DI via `Microsoft.Extensions.DependencyInjection`

### ✅ Window & Layout System
- [x] Custom title bar — ribbon menu integrated, native chrome removed
- [x] `MainWindow` — shell with layout slot
- [x] `MainLayout` — DAW chrome (toolbar, logo row, section panel, timeline)
- [x] `IWindowManager` + `IWindowFactory` — window lifecycle, DI-resolved instances
- [x] Deferred mount — plugins register UI mounts before window is ready

### ✅ Slot System
- [x] `SlotType` enum — Window / Layout / Page / Control
- [x] `SlotNode` + `SlotMap` — hierarchical slot tree
- [x] `WindowDescriptor` — window declares its own slot map
- [x] `ISlotService` — slot registration, mount/unmount, recursive child slot search
- [x] Layout mount exposes child slots — plugins mount into them
- [x] Slot mount tested end-to-end with `TestPluginSimulator`

### ✅ Navigation
- [x] `NavigationRequest` hierarchy — Open/Close/Mount/Unmount/SwapLayout/Queue
- [x] `NavigationBuilder` — fluent API
- [x] `INavigationManager` — orchestrates slot + window + layout operations
- [x] Navigation pushes to `IHistoryManager` — fully undoable

### ✅ History
- [x] `IHistoryManager` + `HistoryManager` — stack-based undo/redo
- [x] `IHistoryEntry` — each operation owns its own rollback
- [x] `NavigationHistoryEntry` — navigation is reversible

### ✅ UI & Controls
- [x] Füme + neon purple color scheme (`#0D0D14` base, `#7c6af7 → #d060ff` gradient)
- [x] `MainTheme.axaml` — all colors in one resource dictionary
- [x] `PlayerButton` + `PlayerIcon` — vector icon controls, path-based, color via `Foreground`
- [x] Transport controls — GoToStart / Play / Stop with hover + press states
- [x] Timeline ruler with accent line at 0:16
- [x] Playhead — gradient vertical line, hover expand effect
- [x] Off-canvas panel — slide-in, rounded corners, 40px margin
- [x] ConstellaTTS logo — SVG with crescent, constellation, waveform

### 🔲 Core Application
- [ ] `Section` domain model — text, seed, emotion, voice_ref, duration, dirty flag, WER
- [ ] `Track` domain model — name, engine, color, global ref, expanded state
- [ ] `SectionViewModel` — core observable properties + `ExtraParamsBefore/After` slots
- [ ] `Project` / `.wwv` file format (ZIP: project.json + refs/ + generated/ + meta.json)
- [ ] Timeline UI — `AbsoluteLayout` lanes, section positioning (`StartSec * PPS`)
- [ ] Section collapsed view — seed, text, voice ref, emotion border color
- [ ] Section expanded view — core params + plugin extra params
- [ ] Emotion spring color scale (blue → green → yellow → orange → red)
- [ ] Dirty indicator (yellow left border) + WER warning icon

### 🔲 Python TTS Daemon
- [ ] `WwvAdapter` ABC — `load / unload / generate / capabilities / vram_usage_bytes`
- [ ] Daemon stdin/stdout IPC (JSON lines)
- [ ] LRU model cache — lazy load, evict on VRAM pressure
- [ ] Waveform extraction — peak sampling, float array, returned alongside wav path
- [ ] First adapter: Chatterbox Multilingual

### 🔲 Plugin System
- [ ] `plugin.json` manifest schema
- [ ] `PluginManifest.Compute()` → `ExtraParamsBefore / ExtraParamsAfter`
- [ ] `AdapterGenerator` — generates `adapter.py` from manifest
- [ ] `inspector.py` — AST analysis for drag & drop model auto-detection
- [ ] Manifest Edit screen — ✓ green / ? yellow / ✗ red wizard UI
- [ ] Model drag & drop → auto manifest → adapter generate flow
- [ ] Extension plugin type (`IExtensionPlugin` — C# assembly, new UI panels)

### 🔲 Audio Generation
- [ ] Generate + NAudio queue — `BufferedWaveProvider` playback
- [ ] Space → generate + play selected section
- [ ] Shift+Space → queue from selected section
- [ ] Whisper large-v3-turbo integration — transcript, WER quality control
- [ ] Generation cache — SHA256 hash (text + voice_ref + emotion + seed + engine + extras)
- [ ] Global cache (`AppData`) + project-local cache (`.wwv/generated/`)

### 🔲 Duration Control
- [ ] `DurationStrategy` — Auto / Rubberband / Interpolate / Generate
- [ ] Rubberband post-processing with stretch ratio warnings
- [ ] ScalerGAN integration for extreme stretch
- [ ] IndexTTS-2 token control (`target_tokens = target_sec * 21`)

### 🔲 Visual Enhancements
- [ ] Emotion graph — `Polyline` per track, emotion over time axis
- [ ] Waveform overlay — peak-sampled float array rendered on sections
- [ ] Stage direction sections — dashed style, no waveform, excluded from emotion graph
- [ ] Track expand/collapse animation
- [ ] Ctrl+Scroll zoom on timeline
- [ ] `ItemsRepeater` virtualization for long timelines

### 🔲 Export
- [ ] All tracks → single mixed WAV
- [ ] Per-track WAV export
- [ ] Per-section WAV export (game asset pipeline)
- [ ] Mix + stems
- [ ] SRT / VTT subtitle export
- [ ] WAV → Opus compression (~93% size reduction)

### 🔲 Import
- [ ] SRT / VTT import → auto section generation
- [ ] Reference audio drag & drop → Whisper auto-transcript

---

## License

[MIT](./LICENSE) — fork freely, keep the copyright notice.

---

## References

- [Avalonia UI](https://avaloniaui.net)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [ScalerGAN](https://github.com/MLSpeech/scaler_gan)
- [IndexTTS-2](https://github.com/index-tts/index-tts)
- [Chatterbox TTS Server](https://github.com/devnen/Chatterbox-TTS-Server)
- [ComfyUI-FishAudioS2](https://github.com/Saganaki22/ComfyUI-FishAudioS2)
