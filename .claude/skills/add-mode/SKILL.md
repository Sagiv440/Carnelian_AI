---
name: add-mode
description: Add a new operating mode to the AI Interface app (alongside Chat, Web Search, Deep Research, Project). Use when extending the app with a new way of handling a prompt, e.g. a RAG/document mode, a summarize mode, or another agent/tool mode.
---

# Add a new mode

A "mode" is how a sent prompt is handled. Four modes exist today — `Chat`, `WebSearch`,
`DeepResearch`, and `Project` (a tool-using agent). The first three share one **streaming** shape;
`Project` shows the **agent / tool-calling** shape. Adding a mode touches these files, in order:

## 1. Declare the mode — `Models/AppMode.cs`
Add a value to the `AppMode` enum with an XML-doc comment describing the behaviour.

## 2. Register it in the picker — `ViewModels/MainWindowViewModel.cs`
In the constructor's `Modes` array, add `new ModeOption(AppMode.Yours, "Label", "Tooltip description")`.

## 3. Handle it — `MainWindowViewModel.SendAsync`
`SendAsync` first resolves the selected `ChatModel` to its backend — `client = _router.For(SelectedModel.Provider)`
and `model = SelectedModel.Id` — then `switch`es on the mode. Add a `case AppMode.Yours:` calling a private
`RunYourModeAsync(client, model, …)` modelled on `RunChatAsync` / `RunWebSearchAsync` /
`RunDeepResearchAsync` / `RunProjectAgentAsync`. Take the `IChatClient client` so your mode works with
whatever provider (local Ollama or a cloud model) is selected — never reach for `IOllamaClient` directly.

## 4. (If it needs new I/O) add a service — `Services/`
Create `IYourService` + `YourService`, then register it in `App.ConfigureServices()`:
- plain logic → `services.AddTransient/AddSingleton<IYourService, YourService>();`
- needs HTTP → `services.AddHttpClient<IYourService, YourService>(...)` (see the Ollama/web-search examples).
Inject it into `MainWindowViewModel`'s constructor **and** add a matching stub in
`ViewModels/DesignTimeServices.cs` (the parameterless ctor / XAML previewer depends on it, or the build breaks).

## AI backends are provider-agnostic (`IChatClient`)
Chat no longer talks to Ollama directly. `Services/IChatClient.cs` is the common surface
(`ChatStreamAsync`, `CompleteAsync`, `ChatWithToolsAsync`, `ListModelsAsync`,
`IsConfiguredAndReachableAsync`, plus a `Provider` tag from `Models/AiProvider.cs`). `OllamaClient`
implements it (via `IOllamaClient : IChatClient`), as do `OpenAiClient` / `GeminiClient` /
`AnthropicClient`. `IModelRouter` (`ChatRouter`) aggregates every configured/reachable provider's models
into the top-bar picker (each a `ChatModel` = provider + id) and resolves a provider to its client via
`For(provider)`. Modes and the agent receive the resolved `IChatClient`, so they work with any provider.

**To add a provider:** add an `AiProvider` value, implement `IChatClient` in `Services/` (read its
key/URL from `ISettingsService` on every call), register it in `App.ConfigureServices()` (`AddHttpClient`)
and add it to `ChatRouter`'s constructor list, then add any API key to `AppSettings` + a field in the
Settings → AI Model → **Web Models** tab. Ollama-only ops (`PingAsync(baseUrl)`, `PullModelAsync`,
`DeleteModelAsync`) stay on `IOllamaClient` for Model Config.

## The two mode shapes

### Streaming shape (Chat / WebSearch / DeepResearch)
Build a message list (system prompt + conversation turns) and stream tokens from
`client.ChatStreamAsync(model, messages, think, ct)` (the resolved `IChatClient`) — or from a service that
streams via an `Action<string>` callback. Feed deltas into the pending assistant `MessageViewModel`:
visible answer text goes to `.Text`, and any `<think>…</think>` reasoning is split into `.Work` (see
`Models/ReasoningSplit.cs` and `ApplyStreamDelta`), which the transcript shows in a collapsible block.

### Agent / tool-calling shape (Project)
When the mode needs the model to *act* (call tools, take steps), follow `ProjectAgentService` (which is
handed the resolved `IChatClient`):
- `client.ChatWithToolsAsync` runs one **non-streaming** turn with `tools` and returns an
  `AgentTurn` (content + requested `AgentToolCall`s). Wire DTOs live in `Models/OllamaDtos.cs`; the
  domain abstractions (`AgentTool`, `AgentToolCall`, `AgentTurn`) in `Models/AgentModels.cs`. Each cloud
  client translates this tool shape into its own format (OpenAI/Anthropic/Gemini differ).
- The loop — advertise tools → run the tools the model asked for → feed each result back as a
  `ChatRole.Tool` message → repeat until the model replies in plain text (capped by `MaxSteps`) — lives
  in `Services/ProjectAgentService.cs`. Tools are defined there with JSON-schema parameters; the action
  log (tool calls + truncated results) is echoed via `onActivity` into the collapsible "work" block, while
  the model's final plain-text reply goes to `onAnswer` (the answer).
- **User gating:** each call is gated by `AgentApprovalMode` — the single global approval setting
  (`AppSettings.AgentApproval`, Settings → Autonomy & Memory; mapped to (approval, step budget) by
  `AutonomyMap.ForApprovalMode`). The service awaits an
  `approve` callback; the VM raises `ToolApprovalRequested`, the code-behind shows `ToolApprovalWindow`,
  and the decision returns through a `TaskCompletionSource<bool>`.
- **Sandboxing:** all file ops are confined to the active project directory (`TryResolve` rejects paths
  outside it) and commands run with that directory as the working dir. Keep any new tool inside that guard.
- **Software installs** are a separate permission (`AppSettings.SoftwareInstall`, a
  `SoftwareInstallPermission` of `Never` / `Ask` / `Allow`): when not `Never` the agent gets an
  `install_software` tool; under `Never`, system package-manager installs (winget/apt/brew/`npm -g`/…)
  are refused. `Ask` permits installs but forces a confirmation for each one even under `AutoRun`;
  `Allow` follows the approval mode.
- **Project state** is a single in-memory `Project` (Name + Directory). Its chats persist to
  `<project>/.AI/chats` (via `IChatHistoryService.LoadFrom/SaveTo`), and skill files found in the project
  (`IProjectSkillService`) are appended to the agent's system prompt. `MainWindowViewModel` routes chat
  save/load through `SaveLog()` / `LoadLog()` depending on whether a project is active.
- **Project skills & `create_skill`.** `IProjectSkillService` scans `SKILL.md` / `*.skill.md` / any `*.md`
  under a `skills/` folder, **and explicitly `<project>/.AI/skills/`** (the general walk skips `.AI`). The
  agent also has an always-on `create_skill` tool (`ProjectAgentService.CreateSkill`) that writes
  `.AI/skills/<slug>.skill.md` — use this shape (a meta tool writing only to a fixed, slugified path) for
  any "author a project file" tool. After each agent turn the VM re-scans (`LoadProjectSkillsAsync`).
- **Memory & a `remember` tool.** When memory is active (`MainWindowViewModel.MemoryActive()`), the agent
  gets a `remember` tool and `MemoryBlock()` is woven into the prompt. A new tool that isn't a file/command
  op (like `remember`/`create_skill`) returns `null` from `ToolGroupOf`, so it bypasses the `AgentTools`
  allow-list — gate it by its own flag instead, and mark it non-destructive in `Describe`.

## Opening a dialog from a mode (the Project / Settings window pattern)
Views are mostly data-templated, but dialogs are opened imperatively: the VM raises an event
(`ProjectRequested`, `SettingsRequested`), the code-behind resolves the dialog's VM from the static
`App.Services` and `ShowDialog`s it, then calls back into the VM with the result (e.g. `ActivateProject`).
Reuse this — never `new` a window or service inside a VM.

## Persona, skills & memory (cross-cutting, not a mode)
Every mode's system prompt carries the active **agent's** persona + built-in skill packs + persistent
**memory**, built by `AgentPromptBuilder`. For a streaming mode, compose the system prompt with
`AgentPromptBuilder.Compose(SelectedAgent, baseInstructions, ThinkingDirective(), MemoryBlock())` (as Chat
does); for a service-owned prompt, prepend `PersonaPrefix()` (which the VM now builds with `MemoryBlock()`,
as Web / Deep / Project do). Do this so your mode speaks in the chosen agent's voice and recalls memory —
don't hand-roll a bare system prompt. Memory is gated by `MemoryActive()` (global switch + agent opt-in).

## Thinking toggle (cross-cutting, not a mode)
The composer's 🧠 Thinking toggle (`MainWindowViewModel.ThinkingEnabled`) appends a planning instruction,
scaled by the Effort setting (`AppSettings.ThinkingEffort`), to the **system prompt**. If your new mode
builds a system prompt, append `ThinkingDirective()` to it (as Chat / WebSearch / Project do) so the
toggle takes effect in your mode too.

## Threading rules (do not skip)
UI-bound state may only change on the UI thread.
- In the VM's own `await foreach` over `ChatStreamAsync`, **do not** add `ConfigureAwait(false)` — the
  loop body must resume on the UI thread to call `MessageViewModel.Append`.
- If work runs on a background thread (a service using `ConfigureAwait(false)` internally, like
  `DeepResearchService` or `ProjectAgentService`), marshal its callbacks with `Dispatcher.UIThread.Post(...)`.
- Report step-by-step progress with `IProgress<string>` constructed on the UI thread (auto-marshals) and
  set `StatusText` from it.
- Call `RequestScroll()` after appending streamed text so the transcript follows along.

## Verify
`dotnet build AI_Interface.sln` must stay at 0 warnings / 0 errors, then run the app and exercise the
new mode (see the `run-app` skill). Note: a running instance locks `AI_Interface.exe` on Windows — stop
it (`Stop-Process -Name AI_Interface -Force`) before rebuilding.
