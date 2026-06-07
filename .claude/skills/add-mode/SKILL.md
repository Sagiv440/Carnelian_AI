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
Add a `case AppMode.Yours:` to the `switch`, calling a private `RunYourModeAsync(...)` modelled on
`RunChatAsync` / `RunWebSearchAsync` / `RunDeepResearchAsync` / `RunProjectAgentAsync`.

## 4. (If it needs new I/O) add a service — `Services/`
Create `IYourService` + `YourService`, then register it in `App.ConfigureServices()`:
- plain logic → `services.AddTransient/AddSingleton<IYourService, YourService>();`
- needs HTTP → `services.AddHttpClient<IYourService, YourService>(...)` (see the Ollama/web-search examples).
Inject it into `MainWindowViewModel`'s constructor **and** add a matching stub in
`ViewModels/DesignTimeServices.cs` (the parameterless ctor / XAML previewer depends on it, or the build breaks).

## The two mode shapes

### Streaming shape (Chat / WebSearch / DeepResearch)
Build a message list (system prompt + conversation turns) and stream tokens from
`IOllamaClient.ChatStreamAsync` — or from a service that streams via an `Action<string>` callback — into
the pending assistant `MessageViewModel.Append`.

### Agent / tool-calling shape (Project)
When the mode needs the model to *act* (call tools, take steps), follow `ProjectAgentService`:
- `IOllamaClient.ChatWithToolsAsync` runs one **non-streaming** turn with `tools` and returns an
  `AgentTurn` (content + requested `AgentToolCall`s). Wire DTOs live in `Models/OllamaDtos.cs`; the
  domain abstractions (`AgentTool`, `AgentToolCall`, `AgentTurn`) in `Models/AgentModels.cs`.
- The loop — advertise tools → run the tools the model asked for → feed each result back as a
  `ChatRole.Tool` message → repeat until the model replies in plain text (capped by `MaxSteps`) — lives
  in `Services/ProjectAgentService.cs`. Tools are defined there with JSON-schema parameters; results are
  truncated and echoed into the transcript via `onDelta`.
- **User gating:** each call is gated by `AgentApprovalMode` (Settings → Project). The service awaits an
  `approve` callback; the VM raises `ToolApprovalRequested`, the code-behind shows `ToolApprovalWindow`,
  and the decision returns through a `TaskCompletionSource<bool>`.
- **Sandboxing:** all file ops are confined to the active project directory (`TryResolve` rejects paths
  outside it) and commands run with that directory as the working dir. Keep any new tool inside that guard.

## Opening a dialog from a mode (the Project / Settings window pattern)
Views are mostly data-templated, but dialogs are opened imperatively: the VM raises an event
(`ProjectRequested`, `SettingsRequested`), the code-behind resolves the dialog's VM from the static
`App.Services` and `ShowDialog`s it, then calls back into the VM with the result (e.g. `ActivateProject`).
Reuse this — never `new` a window or service inside a VM.

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
