# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A cross-platform (Windows + Linux) Avalonia desktop app that runs AI locally through a local
[Ollama](https://ollama.com) server, and optionally through cloud providers (OpenAI/ChatGPT,
Google Gemini, Anthropic/Claude). .NET 9, MVVM via CommunityToolkit.Mvvm.

Four operating modes (`AppMode`), chosen from the sidebar / composer rather than a single picker:
- **Chat** — talk to the model directly.
- **Web Search** — per-prompt 🌐 toggle in the composer; one search injected as context, then answer.
- **Deep Research** — sidebar toggle; plan queries → read pages → synthesize a cited report.
- **Project** — a tool-using **agent** scoped to a project directory (create/open a project to enter it).

Cross-cutting extras: a per-prompt 🧠 **Thinking** toggle (plan-before-answer, depth set by an *Effort*
slider in Settings), a hardware-aware **Model Config** tool for choosing/downloading Ollama models, and
**Agents** — a selectable persona (top-bar picker) whose voice is layered into every mode's system prompt.

## Commands

```bash
# Build / run (from repo root)
dotnet build AI_Interface.sln
dotnet run --project src/AI_Interface

# Publish self-contained single-file binaries
pwsh ./build/publish-windows.ps1     # -> publish/win-x64/AI_Interface.exe
./build/publish-linux.sh             # -> publish/linux-x64/AI_Interface
```

There is no test project yet. To run the app end-to-end you need Ollama running locally
(`ollama serve`) with at least one model pulled (`ollama pull llama3`); the Project agent needs a
**tool-calling-capable** model (e.g. `llama3.1`, `qwen2.5`, `mistral-nemo`).

> On Windows a running instance locks `AI_Interface.exe`, so a rebuild's final copy step fails while the
> app is open. Stop it first: `Stop-Process -Name AI_Interface -Force`.

## Project skills (`.claude/skills/`)

- **run-app** — launch the app (checks Ollama, then `dotnet run`).
- **publish-app** — build self-contained Windows/Linux distributables.
- **add-mode** — add a new prompt-handling mode end-to-end. Covers both the *streaming* shape and the
  *agent / tool-calling* shape, the dialog/event pattern, the Thinking directive, and threading rules.
- **app-style** — the visual design system (tokens, palette, style classes). Read before any UI work.

## Architecture (the parts worth knowing before editing)

**Layering.** `Models` (plain DTOs / domain types) → `Services` (all I/O and orchestration, behind
interfaces) → `ViewModels` (MVVM, no Avalonia UI types) → `Views` (XAML + thin code-behind).
Services are registered in `App.ConfigureServices()` and constructor-injected; view models resolved
from the container are `MainWindowViewModel` (the window's `DataContext`), `SettingsViewModel`,
`AgentsViewModel` (the Agents panel, nested in `SettingsViewModel.AgentsPanel`), `ProjectViewModel`, and
`ModelConfigViewModel`.

**The modes** all live in `MainWindowViewModel.SendAsync`, which switches on `AppMode`. Every mode is
provider-agnostic: the VM resolves the chosen model's client via `_router.For(SelectedModel.Provider)`
and routes through the `IChatClient` surface (see **Providers & routing**).
- *Chat* streams `IChatClient.ChatStreamAsync` directly, with conversation history rebuilt from `Messages`.
- *Web Search* does one `IWebSearchService.SearchAsync`, injects snippets as context, then streams.
- *Deep Research* delegates to `IDeepResearchService.RunAsync` (plans → searches → reads → synthesizes),
  passing the resolved `IChatClient`; reports progress via `IProgress<string>` and streams via `Action<string>`.
- *Project* delegates to `IProjectAgentService.RunAsync` (also passed the `IChatClient`) — a tool-calling
  loop (see **Project mode**).

**Providers & routing.** `IChatClient` (`Services/IChatClient.cs`) is the provider-agnostic chat surface
(`Provider`, `ChatStreamAsync`, `CompleteAsync`, `ChatWithToolsAsync`, `ListModelsAsync`,
`IsConfiguredAndReachableAsync`). Four implementations: `OllamaClient` (local; `IOllamaClient : IChatClient`)
and the cloud clients `OpenAiClient`/`GeminiClient`/`AnthropicClient` (each behind a marker interface
`IOpenAiClient`/`IGeminiClient`/`IAnthropicClient` so DI gives each its own typed `HttpClient`). Each cloud
client reads its API key from `ISettingsService` on every call (blank key ⇒ empty model list + unreachable),
builds the provider's request/response in that one file, and surfaces HTTP errors via an
`InvalidOperationException` (mirrors `OllamaClient.BuildErrorMessage`). `IModelRouter`/`ChatRouter` holds all
four clients: `ListAllModelsAsync` queries every configured+reachable provider in parallel (best-effort —
a failing provider contributes nothing) and aggregates `ChatModel`s (Ollama first, then cloud);
`For(provider)` resolves the client. The picker is `ObservableCollection<ChatModel>` (`Models`) with
`SelectedModel : ChatModel?`; the saved selection is persisted as `"{provider}:{id}"` in
`AppSettings.DefaultModel` and parsed back on load (a bare legacy value is treated as Ollama). Cloud API
keys live in `AppSettings.OpenAiApiKey`/`GeminiApiKey`/`AnthropicApiKey`. Tool-call id threading: the app's
`ChatMessage` carries only tool *names*, so OpenAI/Anthropic clients synthesise deterministic ids
(`call_{n}` / `toolu_{n}`) per assistant tool call and pair each tool result to the next pending call of the
same name when re-serialising the running conversation. Gemini has no system role (extracted into
`systemInstruction`) and uses `user`/`model` roles. `think` is honored only by Ollama; cloud clients ignore it.

**Ollama integration** (`OllamaClient`). Implements `IChatClient` plus Ollama-only model management
(`PingAsync`/`PullModelAsync`/`DeleteModelAsync`, used by Model Config). The base URL is read from
`ISettingsService` on **every** call.
- *Streaming chat* — `ChatStreamAsync` reads NDJSON from `POST /api/chat` with
  `HttpCompletionOption.ResponseHeadersRead`, so the 10-min `HttpClient.Timeout` (set in DI) only bounds
  time-to-first-byte. No `ConfigureAwait(false)` in the VM loop (continuations must hit the UI thread).
- *Tool calling* — `ChatWithToolsAsync` makes a **non-streaming** `POST /api/chat` with a `tools` array
  and returns an `AgentTurn` (content + requested tool calls). Wire DTOs are in `Models/OllamaDtos.cs`;
  domain abstractions (`AgentTool`/`AgentToolCall`/`AgentTurn`) in `Models/AgentModels.cs`.
- *Model management* — `PingAsync` (reachability probe with a short fail-fast timeout), `PullModelAsync`
  (`POST /api/pull`, streamed progress), `DeleteModelAsync` (`DELETE /api/delete`), `ListModelsAsync`.

**Project mode — the agent** (`ProjectAgentService`). Loop: advertise tools → run the tools the model
requests → feed each result back as a `ChatRole.Tool` message → repeat until the model replies in plain
text (`MaxSteps` cap). Tools: `list_directory`, `read_file`, `write_file`, `create_folder`,
`delete_file`, `delete_folder`, `run_command`, and `install_software` (offered only when permitted).
- **Sandbox.** File ops are confined to the project directory (`TryResolve` rejects paths outside it);
  commands run with the project root as the working directory.
- **Approval.** `AgentApprovalMode` (Settings → Project): `AutoRun` / `ConfirmDestructive` /
  `ConfirmEverything`. The service awaits an `approve` callback; the VM raises `ToolApprovalRequested`,
  the code-behind shows `ToolApprovalWindow`, and the decision returns via a `TaskCompletionSource<bool>`.
- **Software install permission.** `AppSettings.SoftwareInstall` (`SoftwareInstallPermission`:
  `Never` / `Ask` / `Allow`). Under `Never`, `install_software` is withheld and `run_command` refuses
  machine-wide package-manager installs (winget/apt/brew/`npm -g`/…) while still allowing project-local
  deps. `Ask` permits installs but confirms each one even under `AutoRun`; `Allow` follows the approval mode.
- **Active project** is single and in-memory (`Project` = Name + Directory). Entered via the sidebar
  **Project** button → `ProjectWindow` (New tab creates `<location>/<name>/` + a `.AI` folder; Open tab
  uses an existing folder, name = folder name). The code-behind calls `vm.ActivateProjectAsync`.
- **Per-project chats.** While a project is active the chat log persists to `<project>/.AI/chats`
  (one JSON per session) via `IChatHistoryService.LoadFrom/SaveTo`; opening a project loads them,
  exiting restores the global log (`%APPDATA%/AI_Interface/chats.json`). The VM routes every save/load
  through `SaveLog()` / `LoadLog()`.
- **Project skills.** On activation `IProjectSkillService` scans the project for skill files (`SKILL.md`,
  `*.skill.md`, or markdown under a `skills` folder; bounded, skipping `.AI`/`.git`/`node_modules`/…)
  and their text is appended to the agent's system prompt.

**Agents — selectable persona** (`IAgentService`/`AgentService`, `AgentPromptBuilder`). An *agent is data*
(`Models/Agent.cs`): Id/Name/Glyph/**Persona** + forward-compat fields (Skills/Tools/Autonomy/MemoryEnabled/
Proactive) that are **persisted-only** today — Phase 1 wires **only Persona** into behaviour. The registry
de-dupes three sources by id with **project overriding global overriding built-in**: an embedded read-only
seed (`assistant`/`researcher`/`code-buddy`/`autopilot`, `IsBuiltIn=true`), global customs in
`<app-data>/AI_Interface/agents/*.json`, and per-project customs in `<project>/.AI/agents/*.json`
(best-effort JSON store; `SaveCustom`/`DeleteCustom` refuse built-in ids). The active agent's id persists in
`AppSettings.ActiveAgentId`. `MainWindowViewModel` holds the `Agents` collection + `SelectedAgent` (top-bar
picker beside the model dropdown — agent and model are independent) and reloads on project enter/exit.
`AgentPromptBuilder.Compose(agent, baseInstructions, thinkingDirective)` builds the streaming modes' system
prompt (persona → base → Thinking), and `PersonaPrefix(agent)` is threaded into `DeepResearchService.RunAsync`
and `ProjectAgentService.RunAsync` so the persona applies in **all four modes**. The assistant
`MessageViewModel` carries `AgentGlyph`/`AgentName`; the transcript header shows the agent's glyph + name
(model id moves to a tooltip). Settings → AI Features → **Agents** is a master/detail panel (`AgentsViewModel`):
list with a built-in badge, **＋ New** / **Duplicate** (always a global custom) / **Delete** (disabled for
built-ins), editing Name / Glyph / Persona / Default model (the provider-aware picker). The main window calls
`AgentsPanel.Initialize(projectDir)` before opening Settings and `vm.LoadAgents()` after it closes.

**Model Config — hardware-aware recommender.** `IHardwareService` scans CPU/RAM and GPU/VRAM
(nvidia-smi first, best-effort cross-platform). `ModelCatalog` (in `Models/ModelCatalog.cs`) ranks a
curated model list by fit to the memory budget plus the chosen use case / quant / context. Opened from
Settings → AI Model → Local AI (enabled only when Ollama is connected); `ModelConfigWindow` lists models
with inline Download/Remove and a "Downloaded" filter.

**Threading model — important.** Avalonia UI may only be touched on the UI thread.
- In the VM's own `await foreach` streaming loops, do **not** use `ConfigureAwait(false)`.
- Background-thread services (`DeepResearchService`, `ProjectAgentService` — both `ConfigureAwait(false)`
  internally) marshal their delta callbacks with `Dispatcher.UIThread.Post`.
- `IProgress<string>` built on the UI thread auto-marshals; use it for `StatusText`.
- Scroll-to-bottom is signalled via the `ScrollToEndRequested` event, handled in `MainWindow.axaml.cs`.

**Web scraping** (`WebSearchService`): keyless DuckDuckGo HTML endpoint + HtmlAgilityPack. `NormalizeUrl`
unwraps `//duckduckgo.com/l/?uddg=…` links. The injected `HttpClient` has a desktop User-Agent.

**Attachments** (`AttachmentService`): images → base64 (vision models); documents → `ExtractTextAsync`
(PDF via PdfPig, DOCX/ODT via their zip XML, everything else read as plain text), injected into the
prompt as `[Attached documents]`. The composer's 📎 menu offers *Photos* and *Documents & text*.

**Settings** (`SettingsService`): JSON under the per-user app-data folder; all reads/writes best-effort.
`SettingsWindow` is a **grouped left nav + content host** (not a flat `TabControl` — a flat one can't show
non-clickable group headers): a left rail with two muted headers (`TextBlock.navheader`) and `Button.nav`
entries, and a right `Panel` whose category panels toggle by `IsVisible` bound to per-category bools on
`SettingsViewModel` (`SelectCategoryCommand` + `SettingsCategory` enum). Panels are moved **verbatim** under:
- **EDITOR FEATURES** — *Appearance* (light/dark + accent/bubble colors), *Typography* (font + size),
  *Layout* (placeholder).
- **AI FEATURES** — *Models* (Local AI: Ollama URL, *Quick setup*, *Test connection*, *Model_Config*; and
  Web Models: per-provider API key + Connect, which persists the key, probes, and raises `ConnectRequested`
  so the main window reloads the dropdown), **Agents** (the agent roster master/detail), *Autonomy & Memory*
  (agent approval mode + software-install permission), *Web Search* (provider + keys), *Voice*,
  *Research & Thinking* (research depth + Thinking *Effort*).

**Theming & design system** (`ThemeService` + `SettingsWindow` + `Styles/ControlStyles.axaml`): a flat
"IDE" look modelled on VS Code / Photoshop — the system UI font (Poppins still embedded in `Assets/Fonts`
and selectable by name), a red-orange accent (`#F2542D`), neutral dark-gray surfaces, sharp 3–5px corners,
hairline borders, flat (no gradients/glass/shadows). **Read the `app-style` skill before UI work.**
- Design tokens in `App.axaml`: shared brushes (`AppAccentBrush`, `UserBubbleBrush`, `AssistantBubbleBrush`,
  plus a near-flat compat `AccentGradientBrush`), font tokens `AppFont` + `AppFontSize` (**DynamicResource**
  so the font family/size can change live), plus **theme-variant-aware** structural tokens in the
  `ThemeDictionaries` (`AppWindowBackground`, `AppSurfaceBrush`, `AppSurfaceBorderBrush`, `AppInputBackground`,
  `AppTextPrimary`, `AppTextSecondary`). Reference all via `{DynamicResource ...}`.
- Reusable style classes in `Styles/ControlStyles.axaml`: `Button.cta` (flat solid accent), `Button.ghost`,
  `Border.card`, `TextBlock.brand`, `TextBlock.muted`, plus `Button.nav`/`.active` (left-rail entries, shared
  by the sidebar chat log and the Settings category nav) and `TextBlock.navheader` (group headers).
- `ThemeService.Apply` runs at startup and on every change in `SettingsViewModel` (guarded by `_loading`).
  It overrides the themeable brush keys, the appearance variant, and `AppFont` ("Poppins" maps to the
  embedded font; anything else is a system family) + `AppFontSize`. Defaults + the swatch palette + font
  list live in `Models/ThemeDefaults.cs`; `SettingsService` migrates the old purple/Poppins defaults on load.
- `SettingsWindow` uses a **grouped left nav** (section headers + `Button.nav` entries) with the selected
  category's content on the right (toggled by `IsVisible`). The *Models* category nests a small inner
  `TabControl` (Local AI / Web Models).

**Sidebar.** New Chat + Project buttons, then the chat log, the Deep Research toggle, the active-project
card, and the model/connection footer. When a project is active a **Chat Log / Files** tab strip appears:
*Files* shows a lazy-loading `TreeView` of the project directory backed by `FileNode` (children load on
expand; a ⟳ button refreshes). The active-project card shows the name + "N skills loaded".

**Resolving view models from views.** Dialogs are opened imperatively: the VM raises an event
(`SettingsRequested`, `ProjectRequested`, `ToolApprovalRequested`, `ModelConfigRequested`), the
code-behind resolves the dialog's VM from the static `App.Services` and `ShowDialog`s it. Use this
pattern (VM event → code-behind opens window) for any new dialog rather than newing up windows in the VM.

## Conventions specific to this project

- **Compiled bindings are on by default** (`AvaloniaUseCompiledBindingsByDefault`). XAML needs
  `x:DataType` on the relevant scope. A `DataTemplate` that reaches a command via
  `RelativeSource AncestorType=Window` sets `x:CompileBindings="False"`. The file-tree
  `TreeViewItem.IsExpanded` setter uses `{ReflectionBinding}` so it binds the item's `FileNode` rather
  than the window's `x:DataType`.
- **Message bubble styling** uses Avalonia style classes toggled from data: `Classes.user="{Binding
  IsUser}"` plus `Border.bubble` / `Border.bubble.user` selectors. No value converters.
- **Design-time stubs** in `ViewModels/DesignTimeServices.cs` back the parameterless VM constructors so
  the XAML previewer works. If you add a service dependency to a container-resolved VM, add a matching
  stub (and update the VM's design-time constructor).
- The target framework is **net9.0** — the Avalonia template defaults to net10.0, which this SDK
  can't build, so don't let it revert.
