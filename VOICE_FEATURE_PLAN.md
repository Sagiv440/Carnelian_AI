# Voice Feature Plan — Text-to-Speech (read responses aloud)

> Goal: give **AI_Interface** a generated voice that can read AI replies aloud, built to the
> project's existing architecture (Models → Services → ViewModels → Views, DI, MVVM).
> Cross-platform: **Windows + Linux**.

---

## 1. Where we are (recap)

A cross-platform Avalonia / .NET 9 AI client with four modes (Chat, Web Search, Deep Research,
Project agent), multi-provider routing (Ollama + OpenAI/Gemini/Anthropic), a 🧠 Thinking toggle,
attachments, hardware-aware model config, theming, and a sandboxed project agent.

**Today output is text-only.** This plan adds an audio output layer.

## 2. Where we're going

1. **Phase 1 — TTS MVP:** a "🔈 Read aloud" button on each assistant message that speaks it.
2. **Phase 2 — Settings + voices:** a Voice tab to pick provider / voice, plus persistence.
3. **Phase 3 — Cloud + auto-speak:** high-quality cloud voice, auto-read every reply, polish.
4. **Phase 4 (future) — STT:** speak *to* the app (microphone → text) for a hands-free loop.

---

## 3. Key decisions — ✅ DECIDED

| Decision | Choice | Notes |
|---|---|---|
| **TTS engine** | ✅ **Piper (local/offline)** | Free, private, offline, cross-platform. Built behind the pluggable `ISpeechService` so OpenAI/OS can be added later. Piper binary + voice-model paths come from the Voice settings tab. |
| **Scope for Phase 1** | ✅ **Per-message button + Voice settings tab** | The 🔈 button on each reply **and** a Settings → Voice tab (provider/voice/test + Piper paths) in this pass. Auto-speak stays Phase 3. |
| **Audio playback** | ✅ **Shell to OS player behind `IAudioPlayer`** | Zero NuGet deps. Windows: `System.Media.SoundPlayer` via a short PowerShell call; Linux: `paplay` → `aplay` fallback. Stop = kill the player process. |

> Pluggable abstraction is still the spine (mirrors `IChatClient` + `IModelRouter`); we just
> implement the **Piper** provider first.

> **Why a pluggable provider mirrors the existing design:** the app already routes chat through
> `IChatClient` + `IModelRouter`. TTS should follow the same shape: one `ISpeechService` surface,
> multiple provider implementations, selected from settings.

---

## 4. Architecture

### 4.1 New Models

- **`Models/SpeechProvider.cs`** — enum: `None`, `OpenAi`, `Piper`, `System`.
- **`Models/Voice.cs`** — record `Voice(SpeechProvider Provider, string Id, string DisplayName)`
  (mirrors `ChatModel`); used to populate the voice picker.

### 4.2 New Services (`Services/`)

- **`ISpeechService.cs`** — provider-agnostic TTS surface:
  ```csharp
  public interface ISpeechService
  {
      bool IsSpeaking { get; }
      event EventHandler? SpeakingChanged;
      Task SpeakAsync(string text, CancellationToken ct);   // synthesize + play; respects Stop
      Task StopAsync();                                      // cancel current playback
      Task<bool> IsConfiguredAsync();                        // provider usable? (key/binary present)
      Task<IReadOnlyList<Voice>> ListVoicesAsync();          // for the settings picker
  }
  ```
- **`SpeechRouter.cs : ISpeechService`** — reads `AppSettings.SpeechProvider` on each call and
  delegates to the matching implementation (same idea as `ChatRouter.For(provider)`). Owns the
  single "currently speaking" state + `SpeakingChanged` event and the active `CancellationTokenSource`.
- **Provider implementations** (each behind a marker interface so DI gives each its own typed deps,
  mirroring `IOpenAiClient` etc.):
  - **`OpenAiSpeechService`** — `POST https://api.openai.com/v1/audio/speech`
    (`model: gpt-4o-mini-tts`, `voice`, `input`), reads `AppSettings.OpenAiApiKey`, writes the
    returned mp3 to a temp file, hands it to `IAudioPlayer`. Reuse the OpenAI base-address pattern.
  - **`PiperSpeechService`** — runs the Piper binary as a subprocess
    (`piper --model <voice.onnx> --output_file <tmp.wav>`, text on stdin), plays the wav.
    Paths come from settings (`PiperExecutablePath` / `PiperModelPath`).
  - *(optional later)* **`SystemSpeechService`** — Windows SAPI via `System.Speech.Synthesis`;
    Linux `spd-say`/`speech-dispatcher`. Zero-config fallback.
- **`IAudioPlayer` / `AudioPlayer`** — thin wrapper over `NetCoreAudio` (or OS players). `PlayAsync(path, ct)`, `Stop()`.

### 4.3 DI registration (`App.ConfigureServices`)

```csharp
// Typed HttpClient for OpenAI TTS (reuses the OpenAI base address + long timeout pattern)
services.AddHttpClient<IOpenAiSpeechService, OpenAiSpeechService>(c =>
{
    c.BaseAddress = new Uri("https://api.openai.com/");
    c.Timeout = TimeSpan.FromMinutes(2);
});
services.AddSingleton<IPiperSpeechService, PiperSpeechService>();
services.AddSingleton<IAudioPlayer, AudioPlayer>();
services.AddSingleton<ISpeechService, SpeechRouter>();   // resolves provider from settings
```
Add a matching stub in **`ViewModels/DesignTimeServices.cs`** (no-op `ISpeechService`) so the XAML
previewer still constructs the VMs.

### 4.4 Settings (`Models/AppSettings.cs`)

Add:
```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public SpeechProvider SpeechProvider { get; set; } = SpeechProvider.None;
public string SpeechVoice { get; set; } = "alloy";   // OpenAI voice id / Piper model name
public bool AutoSpeakReplies { get; set; } = false;  // Phase 3
public string PiperExecutablePath { get; set; } = "";
public string PiperModelPath { get; set; } = "";
```

---

## 5. UI integration

> **Read the `app-style` skill before any XAML work** — match tokens/classes, no hard-coded colors.

### 5.1 Per-message "Read aloud" button (Phase 1)

- **`MessageViewModel`** — add `[ObservableProperty] bool _isSpeaking;` to drive the glyph (🔈 ↔ ⏹).
  Only meaningful for assistant messages.
- **`MainWindow.axaml`** — in the assistant message template (near the final-answer
  `SelectableTextBlock` at ~line 387, beside the existing hover copy-message button at ~line 129),
  add a small ghost button:
  ```xml
  <Button Classes="ghost" Content="🔈"
          Command="{Binding DataContext.SpeakMessageCommand, RelativeSource={RelativeSource AncestorType=Window}}"
          CommandParameter="{Binding}" ToolTip.Tip="Read aloud"/>
  ```
  (Reuse the copy-button hover-visibility pattern.)
- **`MainWindowViewModel`** — add:
  ```csharp
  [RelayCommand] private async Task SpeakMessage(MessageViewModel? m) { ... toggle: if speaking -> StopAsync; else SpeakAsync(m.Text) ... }
  ```
  Subscribe to `ISpeechService.SpeakingChanged` and marshal `IsSpeaking` updates back onto the
  message via `Dispatcher.UIThread.Post` (threading rule: services run on background threads).

### 5.2 Settings → Voice tab (Phase 2)

- **`SettingsWindow.axaml`** — new left-rail category `TabItem` (`Classes="settings"`): provider
  picker (`SpeechProvider`), voice `ComboBox` (from `ListVoicesAsync`), **Test voice** button,
  Piper path fields (visible only when Piper selected), and the **Auto-speak replies** toggle.
- **`SettingsViewModel`** — observable props for the above + a `TestVoiceCommand`
  (`SpeakAsync("This is a test of the selected voice.")`), persisted through `ISettingsService`
  (guard with the existing `_loading` flag).

### 5.3 Auto-speak (Phase 3)

When `AutoSpeakReplies` is on, call `ISpeechService.SpeakAsync` once an assistant message finishes
streaming (where `IsStreaming` flips to false at the end of `SendAsync`).

---

## 6. Threading & cancellation

- Synthesis + playback run on a background thread; **marshal all VM/UI state changes with
  `Dispatcher.UIThread.Post`** (per the project's threading rules).
- `SpeechRouter` owns one `CancellationTokenSource`; starting a new utterance cancels the previous
  one (single voice at a time). `StopAsync` cancels + stops the player.
- Stopping/closing the app or hitting Stop must kill the Piper subprocess / cancel the HTTP read.

---

## 7. Dependencies

| Need | Choice | Notes |
|---|---|---|
| Audio playback | `NetCoreAudio` (NuGet) | Cross-platform wav/mp3; swap behind `IAudioPlayer` if needed |
| OpenAI TTS | none (HttpClient) | Reuses existing key + DI pattern |
| Piper | bundle binary + a voice model | Ship under `Assets/` or resolve from settings path; update `build/publish-*.ps1`/`.sh` |
| System TTS (optional) | `System.Speech` (Win) / `spd-say` (Linux) | Only if we add the OS fallback |

---

## 8. File-by-file change list

**New**
- `Models/SpeechProvider.cs`, `Models/Voice.cs`
- `Services/ISpeechService.cs`, `Services/SpeechRouter.cs`
- `Services/IOpenAiSpeechService.cs` + `OpenAiSpeechService.cs`
- `Services/IPiperSpeechService.cs` + `PiperSpeechService.cs`
- `Services/IAudioPlayer.cs` + `AudioPlayer.cs`

**Edited**
- `App.axaml.cs` (DI registration)
- `Models/AppSettings.cs` (speech settings)
- `ViewModels/MessageViewModel.cs` (`IsSpeaking`)
- `ViewModels/MainWindowViewModel.cs` (`SpeakMessageCommand`, subscribe to `SpeakingChanged`)
- `Views/MainWindow.axaml` (per-message button)
- `ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.axaml` (Voice tab) — Phase 2
- `ViewModels/DesignTimeServices.cs` (stub)
- `build/publish-windows.ps1`, `build/publish-linux.sh` (bundle Piper, if used)

---

## 9. Verification (no test project yet → manual)

1. `dotnet build AI_Interface.sln` clean.
2. Run; send a chat message; click 🔈 on the reply → audio plays.
3. Click again mid-playback → stops (glyph toggles back).
4. Switch provider in Settings → Voice; **Test voice** plays in the chosen voice.
5. (Phase 3) Enable Auto-speak → next reply reads itself once streaming ends.
6. Cross-check on Linux (Piper / OpenAI). Confirm no UI-thread exceptions.

> Reminder: stop the running app before rebuilding on Windows (it locks `AI_Interface.exe`):
> `Stop-Process -Name AI_Interface -Force`.

---

## 10. Build order

1. ✅ `IAudioPlayer` + `AudioPlayer` (cross-platform, shells to OS players).
2. ✅ `ISpeechService` + `ITtsEngine` + `PiperSpeechService` + `SpeechRouter`.
3. ✅ DI wiring (`App.axaml.cs`) + design-time stub (`DesignSpeechService`).
4. ✅ `MessageViewModel.IsSpeaking`/`SpeakGlyph` + `SpeakMessageCommand` + per-message 🔈 button.
5. ✅ Settings → Voice tab (Off/Piper, Piper paths + Browse, Test voice button).
6. ⬜ Piper bundling in `build/publish-*` (offline-by-default install).
7. ⬜ Auto-speak every reply + voice/rate options (Phase 3).
8. ⬜ Add OpenAI TTS engine behind the same `ISpeechService` (cloud option).

---

## ✅ Phase 1 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors)

**New files:** `Models/SpeechProvider.cs`, `Services/ISpeechService.cs`, `Services/ITtsEngine.cs`,
`Services/IAudioPlayer.cs`, `Services/AudioPlayer.cs`, `Services/PiperSpeechService.cs`,
`Services/SpeechRouter.cs`.
**Edited:** `App.axaml.cs`, `Models/AppSettings.cs`, `ViewModels/DesignTimeServices.cs`,
`ViewModels/MessageViewModel.cs`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/SettingsViewModel.cs`,
`Views/MainWindow.axaml(.cs)`, `Views/SettingsWindow.axaml(.cs)`.

**To try it:** Settings → **Voice** → pick **Piper**, point it at a `piper` binary + a `.onnx` voice
(see links in the tab), **Test voice**, then click 🔈 on any reply.

---

## ✅ Phase 2 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors)

One-click setup, a voice catalog browser, and automatic language-aware voice selection.

**Auto-install.** Settings → Voice → **Download & install Piper** fetches the correct release for the
OS/arch into `%LOCALAPPDATA%/AI_Interface/piper`, extracts it (zip on Windows, tar.gz on Linux/macOS),
marks the binary executable, and wires the path in — no manual browsing. (`IPiperInstaller`.)

**Voice browser.** Settings → Voice → **Browse voices…** opens a window listing the published Piper
catalog (`voices.json`) with a **language dropdown**, a **Downloaded-only** toggle, and inline
**Download/Remove** per voice — modelled on the Ollama Model Config window. Voices download into the
managed `…/piper/voices` folder. (`IPiperVoiceCatalog` + `VoiceBrowserWindow`/`VoiceBrowserViewModel`.)

**Language-aware playback.** Each reply's language is detected (`ILanguageDetector` — script + stop-word
heuristics) and `PiperSpeechService` picks a downloaded voice for that language (falling back to the
default/any installed voice). So a Spanish reply is read by a Spanish voice when one is installed.

**New files:** `Models/PiperVoiceInfo.cs`, `Models/LanguageOption.cs`, `Services/ILanguageDetector.cs`
+ `LanguageDetector.cs`, `Services/IPiperInstaller.cs` + `PiperInstaller.cs`,
`Services/IPiperVoiceCatalog.cs` + `PiperVoiceCatalog.cs`, `Services/HttpDownloads.cs`,
`ViewModels/VoiceBrowserViewModel.cs`, `Views/VoiceBrowserWindow.axaml(.cs)`.
**Edited:** `App.axaml.cs` (DI), `Services/PiperSpeechService.cs` (language selection + Linux libs),
`ViewModels/DesignTimeServices.cs`, `ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.axaml(.cs)`.

**To try it:** Settings → **Voice** → **Piper** → **Download & install Piper** → **Browse voices…**,
download a voice or two (e.g. English + your other language), close, then 🔈 a reply.
