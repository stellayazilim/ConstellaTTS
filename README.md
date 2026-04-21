# ConstellaTTS Studio

> A DAW-based, plugin-driven TTS editor and audio production pipeline, built specifically for the Viteria universe.

ConstellaTTS Studio is a local-first desktop application for generating in-game dialogue, cutscene narration, and character voices. It works like a MIDI piano roll: each spoken sentence is a "section" positioned on a timeline, generated independently, and assembled into a final audio output.

**Stack:** Avalonia UI + C# · CommunityToolkit.Mvvm · Python IPC daemon (MessagePack) · Embedded Python 3.11

---

## Screenshots

![Main Timeline](resources/main_screen.png)

<p align="center">
  <img src="resources/section_modal.png" width="48%" />
  &nbsp;
  <img src="resources/sound_bank_modal.png" width="48%" />
</p>

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
        └── MainLayout              ← mounted at startup via DeferMount
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

    public IReadOnlyList<Assembly> Dependencies =>
        [typeof(ConstellaTTSCoreModule).Assembly];

    public void Build(IServiceCollection services)
    {
        services.AddSingleton<MyView>();
    }
}
```

Plugins are discovered from `./plugins/` — DLLs scanned, `IConstellaModule` implementations instantiated, sorted by dependency graph, then `Build()` called in order.

### IPC Daemon

```
Avalonia (C#)
    ↕  stdin/stdout — MessagePack frames [4-byte LE length][msgpack bytes]
Python daemon (single process, embedded Python 3.11)
    ↕
TTS models — lazy load, LRU eviction
```

**Threading model:**
```
Dedicated Thread (IPC.PipeReader)   — blocking pipe read, never touches thread pool
    ↓  Channel<IPCMessage>          — lock-free handoff
Async Dispatch Loop                 — routes tts.chunk → SoundService
    ↓
SoundService + BufferStreamer       — per-job PCM accumulator, temp .raw file
    ↓  FileShare.ReadWrite
BufferReadStream (players)          — IAsyncEnumerable<byte[]>, seek by byte or seconds
```

### Audio Streaming Pipeline

```
Python → raw PCM chunks
    ↓
BufferStreamer.Append()             — writes to temp .raw file
    ↓  threshold OR final=true
BufferReadStream.ReadChunksAsync()  — streams to player as available
    ↓  final=true
Opus encode → project audio file    — temp file deleted
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

# Dev setup — downloads embedded Python 3.11, installs pip + daemon deps
dotnet script infra/setup.csx

dotnet restore
dotnet run --project src/ConstellaTTS.Avalonia
```

Place plugin DLLs in `./plugins/` next to the executable.

---

## Writing a Plugin

1. Create a class library targeting `net9.0`
2. Reference `ConstellaTTS.SDK` and optionally `ConstellaTTS.SDK.IPC`
3. Implement `IConstellaModule`
4. Register services in `Build()`
5. Optionally mount UI into existing slots via `DeferMount`
6. Drop compiled DLL into `./plugins/`

---

## Roadmap

| Phase | Focus |
|-------|-------|
| **Now** | Domain model (Section, Track, Project), `.wwv` file format |
| **Next** | Timeline UI, section views, emotion color system |
| **Soon** | Python daemon integration, first adapter (Chatterbox), audio playback |
| **Later** | Plugin manifest system, adapter generator, drag & drop model install |
| **Future** | Duration control (Rubberband, ScalerGAN, IndexTTS-2), export pipeline |

---

## Progress Checklist

### ✅ Platform & Architecture
- [x] Avalonia UI selected (Skia-based, cross-platform, WPF heritage)
- [x] Solution structure — SDK / SDK.IPC / Core / Avalonia separation
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

### ✅ Navigation
- [x] `NavigationRequest` hierarchy — Open/Close/Mount/Unmount/SwapLayout/Queue
- [x] `NavigationBuilder` — fluent API
- [x] `INavigationManager` — orchestrates slot + window + layout operations
- [x] Navigation pushes to `IHistoryManager` — fully undoable

### ✅ History
- [x] `IHistoryManager` + `HistoryManager` — stack-based undo/redo
- [x] `IHistoryEntry` — each operation owns its own rollback
- [x] `NavigationHistoryEntry` — navigation is reversible

### ✅ Theme System
- [x] `IThemeProvider` — global/per-theme style registration, JSON color theme loading
- [x] `ThemeProvider` — `RegisterGlobal`, `RegisterForTheme`, `LoadColorTheme`, `ApplyTheme`
- [x] Module self-registers its own styles via `ThemeProvider` — no hardcoded `App.axaml` imports

### ✅ UI & Controls
- [x] Füme + neon purple color scheme (`#0D0D14` base, `#7c6af7 → #d060ff` gradient)
- [x] `MainTheme.axaml` — all colors in one resource dictionary
- [x] `PlayerButton` + `PlayerIcon` — vector icon controls, path-based, color via `Foreground`
- [x] Transport controls — GoToStart / Play / Stop with hover + press states
- [x] Timeline ruler with accent line
- [x] Playhead — gradient vertical line
- [x] ConstellaTTS logo — SVG with crescent, constellation, waveform

### ✅ Exception Handling
- [x] `ConstellaException` base class — normalized exception hierarchy (SDK)
- [x] `IExceptionHandler` — `Handle(ConstellaException)` interface (SDK)
- [x] `ExceptionHandler` — logs + raises `ExceptionHandled` event for UI layer (Core)
- [x] `IPCException` hierarchy — `DaemonNotRespondingException`, `DaemonStartFailedException`, `IPCTimeoutException`

### ✅ IPC Daemon
- [x] `IIPCService` + `IPCMessage` — MessagePack protocol contract (`SDK.IPC`)
- [x] `IPCService` — dedicated pipe reader thread + `Channel<IPCMessage>` dispatch loop
- [x] Watchdog — fires `DaemonNotRespondingException` via `IExceptionHandler` on chunk timeout
- [x] Two subscription styles — event-style (`MessageReceived +=`) and method-style (`On("event", ...)`)
- [x] Request/response correlation — `SendAsync` with timeout returns matched response
- [x] Single-instance daemon — lock file prevents duplicate processes
- [x] Embedded Python 3.11 — `infra/setup.csx` downloads and configures
- [x] `infra/setup.csx` — idempotent dev environment setup
- [x] `infra/teardown.csx` — cleanup script

### ✅ Audio Streaming Pipeline
- [x] `BufferWriteStream` — single writer, appends PCM chunks to temp `.raw` file
- [x] `BufferReadStream` — multi-reader, seek by byte offset or seconds, `IAsyncEnumerable<byte[]>`
- [x] `BufferStreamer` — per-job coordinator, `CreateReader(atOffset)` / `CreateReader(atSeconds)`
- [x] `AudioFormat` — sample rate/channel/bit depth, `SecondsToBytes` / `BytesToSeconds`
- [x] `SoundService` — job index, `StartJob` / `Append`, `JobStarted` event
- [x] Flush policy — flush to disk on threshold OR `final=true`, then Opus encode placeholder

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
- [ ] Opus encoder integration — Concentus or libopus P/Invoke
- [ ] NAudio playback — `BufferedWaveProvider` fed from `BufferReadStream`
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
- [MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- [ScalerGAN](https://github.com/MLSpeech/scaler_gan)
- [IndexTTS-2](https://github.com/index-tts/index-tts)
- [Chatterbox TTS Server](https://github.com/devnen/Chatterbox-TTS-Server)
- [ComfyUI-FishAudioS2](https://github.com/Saganaki22/ComfyUI-FishAudioS2)
