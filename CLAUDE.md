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
slider in Settings), a hardware-aware **Model Config** tool for choosing/downloading Ollama models,
**Agents** — a selectable persona (top-bar picker) whose voice is layered into every mode's system prompt
(including a built-in **Lead** orchestrator that, in Project mode, delegates subtasks to specialist agents),
**Memory** — persistent facts (global + per-project, stored as Markdown) injected into every mode, and
**Voice** — read replies aloud with a local **Piper** TTS engine (one-click install, a voice-catalog
browser, and automatic language-matched voice selection; composer 🔊 *Auto-read* toggle).

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
  **Use Multiple LLMs (optional):** when `AppSettings.DeepResearchUseMultipleModels` is on, the VM resolves
  per-step model overrides and passes them as `RunAsync`'s optional `planner`/`synthesizer`
  (`ModelEndpoint(IChatClient Client, string Model)`); each blank/unreachable override falls back to the chat
  model. Search + page-reading are pure I/O and never change. **Note:** the synthesis step receives page
  contents — point it at a local Ollama model for sensitive sources.
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
- *One-click install* — `IOllamaInstaller`/`OllamaInstaller` mirrors `PiperInstaller`: one confirmed click
  in Settings → Models → Local AI downloads and runs the official installer for this OS, then best-effort
  starts the server. **Windows:** downloads `OllamaSetup.exe` via `HttpDownloads.ToFileAsync` and runs it
  silently (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`; an Inno Setup installer — it may show a UAC prompt
  itself, which is expected); binary lands at `%LOCALAPPDATA%\Programs\Ollama\ollama.exe`. **Linux:** runs
  `curl -fsSL https://ollama.com/install.sh | sh` via `/bin/bash -c` (needs `curl`, maybe sudo). **macOS:**
  not auto-installed — throws a graceful "install from ollama.com" message. Per-OS asset/path/URL selection
  is in small `internal static` pure helpers (`CandidateExecutablePaths`, `InstallSourceUrl`) for testability;
  the download URLs are fixed `https` constants (no shell interpolation of untrusted input). Registered via
  `AddHttpClient<IOllamaInstaller, OllamaInstaller>` (20-min timeout). The button is on the Local AI tab.

**Project mode — the agent** (`ProjectAgentService`). Loop: advertise tools → run the tools the model
requests → feed each result back as a `ChatRole.Tool` message → repeat until the model replies in plain
text (step-budget cap — a `maxSteps` arg to `RunAsync`, derived from the global approval setting via
`AutonomyMap.ForApprovalMode`; `≤0` falls back to `DefaultMaxSteps`=24). Tools: `list_directory`,
`read_file`, `write_file`, `create_folder`,
`delete_file`, `delete_folder`, `run_command`, `install_software` (offered only when permitted),
`remember` (offered only when memory is on — see **Memory**), `create_skill` (always offered —
see **Project skills**), and `update_docs` (offered only to the top-level/main agent when
`allowDocsUpdate` — see **`update_docs` tool**).
- **Per-agent tool allow-list (Phase 2).** `RunAsync` takes the active agent's `AgentTools`; `BuildTools`
  advertises **only the permitted groups** (ReadFiles→list/read, WriteFiles→write/create, DeleteFiles→
  delete, RunCommands→run_command, InstallSoftware→install_software). `ExecuteAsync` refuses a disallowed
  tool if the model calls it anyway (defense in depth). `install_software` needs **both** the agent's
  `InstallSoftware=true` **and** `SoftwareInstallPermission != Never`. An **unrestricted** `AgentTools`
  (`AllowAll=true`, the default for an un-customized agent) offers the full set, so behaviour is unchanged.
- **Sandbox.** File ops are confined to the project directory (`TryResolve` rejects paths outside it);
  commands run with the project root as the working directory.
- **Approval.** `AgentApprovalMode` (`AutoRun` / `ConfirmDestructive` / `ConfirmEverything`) is passed to
  `RunAsync` per turn. The service awaits an `approve` callback; the VM raises `ToolApprovalRequested`,
  the code-behind shows `ToolApprovalWindow`, and the decision returns via a `TaskCompletionSource<bool>`.
  The mode is the **single global setting** `AppSettings.AgentApproval` (Settings → Autonomy & Memory) — see
  *Autonomy* below.
- **Autonomy (global).** The global `AppSettings.AgentApproval` (Settings → Autonomy & Memory) is the
  **single authoritative** approval policy for **every** project-agent run — both the single-agent path and
  Lead-delegated runs. There is **no per-agent autonomy** (the `AutonomyLevel` enum and `Agent.Autonomy` were
  removed; only per-agent *Tools* remain). `MainWindowViewModel.RunProjectAgentAsync` derives
  `(approval, maxSteps)` via `AutonomyMap.ForApprovalMode(AppSettings.AgentApproval)`: `ConfirmEverything`→
  (`ConfirmEverything`, 8), `ConfirmDestructive`→(`ConfirmDestructive`, 24 = default), `AutoRun`→
  (`AutoRun`, 40). For `AutoRun` only, `AgentPromptBuilder.PlanningDirective(AgentApprovalMode)` adds a
  plan-then-execute directive (folded into the `thinkingDirective` arg, so it's a prompt directive — not a
  separate planning round; applied on the single-agent path only). `SoftwareInstallPermission` remains an
  **independent** gate (both auto-run **and** the install permission must allow).
- **Software install permission.** `AppSettings.SoftwareInstall` (`SoftwareInstallPermission`:
  `Never` / `Ask` / `Allow`). Under `Never`, `install_software` is withheld and `run_command` refuses
  machine-wide package-manager installs (winget/apt/brew/`npm -g`/…) while still allowing project-local
  deps. `Ask` permits installs but confirms each one even under `AutoRun`; `Allow` follows the approval mode.
- **`remember` tool (Phase 4).** When memory is active for the run (the VM passes `memoryEnabled`), the
  agent is offered a `remember` tool; `scope:"user"` writes a fact to global memory, anything else (default)
  to the project's memory. It isn't gated by the `AgentTools` allow-list (it's not a file/command tool).
- **Active project** is single and in-memory (`Project` = Name + Directory). Entered via the sidebar
  **Project** button → `ProjectWindow` (New tab creates `<location>/<name>/` + a `.AI` folder; Open tab
  uses an existing folder, name = folder name). The code-behind calls `vm.ActivateProjectAsync`.
- **Per-project chats.** While a project is active the chat log persists to `<project>/.AI/chats`
  (one JSON per session) via `IChatHistoryService.LoadFrom/SaveTo`; opening a project loads them,
  exiting restores the global log (`%APPDATA%/AI_Interface/chats.json`). The VM routes every save/load
  through `SaveLog()` / `LoadLog()`.
- **`AI_DOCS.md` project handbook.** `IProjectDocsService`/`ProjectDocsService` reads `<project>/.AI/AI_DOCS.md`
  — the app's equivalent of how Claude Code reads CLAUDE.md: a hand-authored, authoritative per-project
  handbook the **Project-mode agent follows**. Loaded off the UI thread in `LoadProjectSkillsAsync` (so an
  edit is picked up on the next turn) and reset on project exit; best-effort (missing/unreadable ⇒ `""`),
  capped at 24 000 chars (it's injected on every turn). The VM folds it into the existing `projectSkills`
  system-prompt channel via `ProjectContext()` = `ProjectDocsContext()` (docs first) + `ProjectSkillsContext()`,
  passed at **both** project-agent call sites in `RunProjectAgentAsync` — so it reaches the single agent, the
  Lead, **and** delegated specialists (the orchestrator threads `projectSkills` into each delegated run) with
  no `ProjectAgentService`/`AgentOrchestrator` signature change. **Project mode only** — it is *not* added to
  the Chat / Web Search / Deep Research prompts (those use `PersonaPrefix()`, left untouched). The active-project
  card shows a `📄 AI_DOCS loaded` indicator (`HasProjectDocs`).
- **`update_docs` tool — the agent can maintain the handbook.** The **main (top-level)** Project-mode agent
  gets an `update_docs` tool (`IProjectDocsService.Save` writes the full new handbook), offered like
  `create_skill` (ungated by the `AgentTools` allow-list, approval-gated **destructive** so the global
  ConfirmDestructive/ConfirmEverything mode asks first). Its tool description carries the CLAUDE.md discipline
  (durable rules only, *not* a log — transient facts go to memory; revise surgically; keep it accurate). **Only
  top-level agents get it:** `ProjectAgentService.RunAsync` takes `bool allowDocsUpdate` — the VM's single-agent
  path passes `true`, the Lead gets it via `BuildLeadTools`, and `AgentOrchestrator.DelegateAsync` passes
  **`false`** so **delegated specialists can't**. Defense in depth: `ExecuteAsync` *refuses* `update_docs` when
  `allowDocsUpdate` is false (not merely "not advertised"). The handbook path is **locked** — `write_file`/
  `delete_file` refuse `.AI/AI_DOCS.md` (`IsHandbookPath`) and `delete_folder` refuses any folder that contains
  it, i.e. `.AI` itself (`ContainsHandbook`) — so `update_docs` is the *sole* writer. (`IsHandbookPath`/
  `ContainsHandbook` are `internal static`, unit-tested.)
- **Project skills.** On activation `IProjectSkillService` scans the project for skill files (`SKILL.md`,
  `*.skill.md`, or any markdown under a `skills` folder; bounded, skipping `.AI`/`.git`/`node_modules`/…)
  and their text is appended to the agent's system prompt. The project's own **`.AI/skills/`** folder is
  scanned **explicitly** (the general walk skips `.AI`), so any `*.md` dropped there loads as guidance.
  **Phase 2:** if the active agent's `Skills` list names specific project skills
  (`MainWindowViewModel.ProjectSkillsContext`), only those are included; if it names none, **all** discovered
  project skills are included (back-compat). After every project-agent turn the VM re-scans
  (`LoadProjectSkillsAsync`) so a freshly created skill loads on the next turn.
- **`create_skill` tool.** Always offered in Project mode (writes only under `.AI/skills/`, so it isn't
  gated by the `AgentTools` allow-list). `ProjectAgentService.CreateSkill` writes
  `.AI/skills/<slug>.skill.md` (frontmatter `name`/`description` + the model-authored Markdown body); the
  path is computed from a slugified name so the model can't write outside the skills folder. Intended for
  "create a skill for &lt;subject&gt;" — the model authors thorough, structured guidance and the tool persists it.

**Project mode — the orchestrator / lead agent** (`IAgentOrchestrator`/`AgentOrchestrator`). A built-in
**Lead** agent (`Id="lead"`, glyph 🧭, `IsOrchestrator=true`, and a build-capable
`Tools` ceiling — read/write/delete/run, **no install**) doesn't
do the work in a single tool loop — it **delegates**. This is the "agents as tools" pattern: the lead is a
tool-calling loop (modelled on `ProjectAgentService.RunAsync`) whose main tool runs a *nested* specialist.
`MainWindowViewModel.RunProjectAgentAsync` routes to `_orchestrator.RunAsync(...)` when
`SelectedAgent?.IsOrchestrator == true`; otherwise the single-agent `_agent.RunAsync` path is unchanged
(the activity/answer marshalling lambdas are shared by both branches).
- **`Agent.IsOrchestrator`** (`Models/Agent.cs`) marks an agent as a lead; it round-trips as the
  `orchestrator:` frontmatter key in `AgentMarkdown` (mirroring `proactive`/`memory`). The Agents editor
  exposes it as a **"Lead / orchestrator (delegates to other agents)"** checkbox under *Behaviour*
  (`AgentsViewModel.EditIsOrchestrator`, loaded/persisted exactly like `EditProactive`; copied by Duplicate),
  so any custom agent can be made a lead — not just the built-in Lead.
- **Lead tools.** `delegate_task(agent_id, task)` (always offered) + read-only `list_directory`/`read_file`
  (advertised by `BuildLeadTools` when the lead's own `Tools` allow `ReadFiles`). The lead never gets
  write/delete/run/install tools **directly** — its broader allow-list is the team's *ceiling* (see below),
  not its own hands. **Finish convention is reused:** a lead reply with **no** tool calls is its final
  plain-text summary (no separate `finish` tool).
- **Roster.** `_agents.ListAgents(project.Directory)` filtered to `!IsOrchestrator && Id != lead.Id` — an
  orchestrator can **never** delegate to another orchestrator (hard rule → no nested orchestration). The
  roster is injected into the lead's service-owned system prompt via `BuildRosterCatalog` (one line per
  agent: id, name, description-or-first-persona-sentence, tools summary, autonomy).
- **`delegate_task` execution.** Resolves the specialist by `agent_id` (missing ⇒ a `"No agent 'X'.
  Available: …"` tool result), builds its model from `DefaultModel` ("{provider}:{id}" via `_router.For`;
  unset/unparseable ⇒ the lead's client+model), then runs the **existing** `IProjectAgentService.RunAsync`
  with a one-message conversation (the `task` brief as a `ChatMessage.User`),
  `PersonaPrefix(specialist, memoryBlock)`, and `specialist.MemoryEnabled && memoryEnabled`. The specialist's
  final answer is **captured via the `onAnswer` callback** (a capturing lambda — `ProjectAgentService`'s
  signature is unchanged), truncated ~6000 chars, and returned to the lead as the tool result. The
  specialist's activity flows into the delegation's structured card (see *Structured delegation UI* below),
  not the lead's work log.
- **Tools are a ceiling; autonomy is the global setting.** `DelegateAsync` resolves a delegated run two
  ways. **Tools (ceiling):** `CapTools(lead.Tools, specialist.Tools)` — a NEW explicit allow-list whose every
  group flag is `lead.Allows(g) && specialist.Allows(g)` (resolved via `AgentTools.Allows`, so `AllowAll`
  works on either side); a specialist can do **at most** what the lead is allowed. **Autonomy (global):**
  the run uses `AutonomyMap.ForApprovalMode(approval)`, where `approval` is the global
  `AppSettings.AgentApproval` threaded into `IAgentOrchestrator.RunAsync` by the VM (added after
  `installPermission`). So the single Settings → Autonomy & Memory mode governs the lead loop **and** every
  delegated specialist run — neither the lead nor the specialist carries its own autonomy anymore.
  `install_software` stays **double-gated**: it needs the capped `InstallSoftware` (both lead and specialist
  allow it) **and** the global `SoftwareInstallPermission` (passed through unchanged). The built-in Lead's tool
  ceiling is read/write/delete/run with **no install**, so its team can build but installs require raising the
  ceiling. `BuildLeadTools` is unchanged — the lead still only wields delegate + read-only itself.
- **Guards.** A `MaxDelegations` cap (12) on the lead loop; exceeding it forces a wrap-up
  (`onAnswer("_(stopped after N delegations …)_")`). A **repeat guard** (`DelegationKey` = lowercased,
  trimmed `agent_id` + `task`) returns the prior result instead of re-running an identical subtask, to break
  trivial loops. `OperationCanceledException` propagates.
- **Structured delegation UI (Phase 3B — structured feed everywhere).** `RunAsync` takes an
  `Action<DelegationUpdate> onDelegation` (right before `approve`) **and** an `Action<ActivityUpdate>
  onActivityStep` (replacing the old monospace `onActivity` — the orchestrator no longer has a monospace
  channel). The lead's **own** steps now feed the **structured activity feed** on the message (the same
  single-agent feed): interim narration is emitted as an `ActivityPhase.Note`, and each of its own
  read/scan/`update_docs` tools as a `Started`/`Finished` pair, via `onActivityStep` on a `leadActivityIndex`
  counter **independent** of the delegation counter. Each **delegation** emits structured `DelegationUpdate`s
  (`Models/DelegationUpdate.cs`: `DelegationPhase Started/Activity/Finished`, a 0-based `Index`, agent
  name/glyph, task, text, **plus an optional `ActivityUpdate? Step`** carrying the specialist's structured
  step on the `Activity` phase). The orchestrator owns the delegation counter at the `RunAsync` level (a
  single-element `int[]` holder — a `ref` can't flow into the async `DelegateAsync`); `DelegateAsync` consumes
  `nextIndex[0]++` **only past** the missing-agent / repeat-guard early returns, so every
  Started/Activity/Finished of one delegation shares the same `Index`. `DelegateAsync` passes the specialist
  a real `onActivityStep` (`SpecialistStep`, which wraps each `ActivityUpdate` into a `DelegationUpdate` with
  `Step` set) and a **discarding** `onActivity` (`_ => { }`) — the specialist's own per-run index space lands
  in that delegation card's **own** feed. The VM marshals `onDelegation` via `Dispatcher.UIThread.Post` and
  dispatches by phase to the assistant `MessageViewModel`'s
  `StartDelegation`/`ApplyDelegationActivity`(routes `u.Step` into the card's feed)/`FinishDelegation` (keyed
  by `Index`, robust if not found), which back an `ObservableCollection<DelegationStepViewModel>`
  (`Delegations`, plus `HasDelegations`). Each step renders as a **collapsible per-delegation card**
  (`MainWindow.axaml`, between the work block and the answer `Segments`): header (glyph + name + task +
  running/done indicator) toggling a body that shows the specialist's **structured `Activities` feed** (the
  shared `ActivityRowTemplate`, identical to a single-agent run) + `Result`. A failed specialist emits a
  `Finished` update with the error text. The lead's plain-text summary still goes to the answer bubble.
  `ActivityFeed.Apply(feed, update)` (`ViewModels/ActivityFeed.cs`, `internal static`) is the **shared** feed
  logic behind both `MessageViewModel.ApplyActivity` and `DelegationStepViewModel.ApplyActivity`.
- **Testable helpers** (`internal static`, via `InternalsVisibleTo`): `BuildRosterCatalog`,
  `ShortDescription`, `ToolsSummary`, `DelegationKey`, and `CapTools` (the tool-ceiling intersection) —
  see `AgentOrchestratorTests`.
- **Triggering it:** pick **Lead** in the top-bar agent picker, enter a project, send a goal. Needs a
  tool-calling-capable model for both the lead and the specialists.
- **DI:** `AddSingleton<IAgentOrchestrator, AgentOrchestrator>()` (next to `IProjectAgentService`); the
  orchestrator injects `IProjectAgentService` + `IModelRouter` + `IAgentService`.

**Agents — selectable persona + skills + tools** (`IAgentService`/`AgentService`, `AgentPromptBuilder`).
An *agent is data* (`Models/Agent.cs`): Id/Name/Glyph/**Persona** + **Skills** + **Tools** (Phase 2;
autonomy is **not** per-agent — it's the single global `AppSettings.AgentApproval`, see *Autonomy* above) +
**MemoryEnabled**
(Phase 4 — per-agent opt-out for persistent memory; see *Memory* below) + **Proactive** (Phase 5 —
next-step suggestion chips; see *Proactive* below) + **IsOrchestrator** (the lead/delegation flag; see
*Project mode — the orchestrator / lead agent* above). The registry
de-dupes three sources by id with **project overriding global overriding built-in**: an embedded read-only
seed (`assistant`/`researcher`/`code-buddy`/`autopilot`/`lead`, `IsBuiltIn=true`), global customs in
`<app-data>/AI_Interface/agents/*.md`, and per-project customs in `<project>/.AI/agents/*.md` (portable
Claude-Code-style Markdown via `AgentMarkdown`; legacy `*.json` is auto-migrated to `*.md` on load;
`SaveCustom`/`DeleteCustom` refuse built-in ids). The active agent's id persists in
`AppSettings.ActiveAgentId`. `MainWindowViewModel` holds the `Agents` collection + `SelectedAgent` (top-bar
picker beside the model dropdown — agent and model are independent) and reloads on project enter/exit.
- **Project mode prefers "team" (orchestrator) agents.** In a project the picker leads with the coordinated
  team experience (`ViewModels/ProjectAgentPicker.cs`, pure `internal static` helpers, unit-tested):
  `LoadAgents` runs the roster through `Arrange(roster, ProjectTeamAgentsOnly)` **only when a project is
  active** — orchestrators sorted first (stable), and (opt-in) single agents hidden; the global/non-project
  order is untouched. `ActivateProjectAsync` auto-selects `PreferredOrchestrator(Agents)` (built-in **Lead**,
  else first orchestrator) when the current pick isn't an orchestrator, stashing the prior id in
  `_preProjectAgentId`; `ExitProject` restores it. The in-project selection is **transient** —
  `OnSelectedAgentChanged` persists `ActiveAgentId` **only outside a project** (`ActiveProject is null`), so
  entering a project (and the auto-switch) never permanently changes the global preference, even if the app
  closes mid-project. The picker badges orchestrators with a muted **"team"** pill (`IsOrchestrator`).
  `AppSettings.ProjectTeamAgentsOnly` (default off; Settings → Autonomy & Memory → *Project agents*) is the
  opt-in strict filter. **Picker ↔ roster are decoupled:** the Lead's delegation roster comes from
  `IAgentService.ListAgents` (`AgentOrchestrator.BuildRoster`), never the picker `Agents`, so hiding single
  agents from the picker never starves the Lead of specialists.
`AgentPromptBuilder.Compose(agent, baseInstructions, thinkingDirective)` builds the streaming modes' system
prompt (persona → base → **built-in skill packs** → Thinking), and `PersonaPrefix(agent)` (now persona +
skills) is threaded into `DeepResearchService.RunAsync` and `ProjectAgentService.RunAsync` so persona +
skills apply in **all four modes**. The assistant `MessageViewModel` carries `AgentGlyph`/`AgentName`; the
transcript header shows the agent's glyph + name (model id moves to a tooltip). Settings → AI Features →
**Agents** is a master/detail panel (`AgentsViewModel`): list with a built-in badge, **＋ New** / **Duplicate**
(always a global custom) / **Delete** (disabled for built-ins), editing Name / Glyph / Persona / Default model
/ **Tool permissions** (checkboxes) / **Proactive**
(checkbox, Phase 5) / **Skills** (checklist; built-in + project) — autonomy is no longer edited per agent
(it's the global Settings → Autonomy & Memory approval mode). The main window calls
`AgentsPanel.Initialize(projectDir)` before opening
Settings and `vm.LoadAgents()` after it closes.

**Skills (Phase 2).** Two kinds: **built-in skill packs** (`Models/SkillCatalog.cs` — `cited-research`,
`concise`, `careful-coding`, `step-by-step`; `SkillPack(Id, Name, Content)`) and **project `SKILL.md`**
files. `Agent.Skills` stores a mix of built-in pack **ids** and project-skill **names**. `AgentPromptBuilder`
appends selected packs' content in every mode; project skills are resolved (and filtered by selection) only
in Project mode. **`AgentTools` "unrestricted vs explicit":** `AllowAll` defaults to `true` (un-customized
agents are unrestricted — full toolset, unchanged behaviour); the Agents editor calls `Restrict()` on the
first checkbox toggle to switch to explicit per-tool flags. Built-in seed agents set explicit allow-lists
(Assistant/Researcher = read-only, Code Buddy = file+command, Autopilot = +install) so they differ.

**Memory (Phase 4).** Persistent facts the assistant recalls across sessions, in **two scopes**:
**global** (about the user, `<app-data>/AI_Interface/memory.md`) and **project** (`<project>/.AI/memory.md`).
Stored as **portable Markdown** (`MemoryMarkdown` — a `# Memory` heading + one `- ` bullet per fact, with
optional `<!-- source · date -->` metadata; mirrors `AgentMarkdown`'s portability goal). `IMemoryService`/
`MemoryService` exposes `Load`/`Add`(dedups exact text)/`Remove`/`Clear` and `BuildContextBlock(projectDir)`
(a compact "About the user / About this project" block). The VM gates injection on
`AppSettings.GlobalMemoryEnabled` (master switch) **and** the active agent's `Agent.MemoryEnabled`
(per-agent opt-out) via `MemoryActive()`; when active, the block is threaded into **all four modes**
through `AgentPromptBuilder.Compose`/`PersonaPrefix` (new `memoryBlock` arg). Two write paths:
the project agent's `remember` **tool** (see Project mode), and an explicit chat trigger —
`MainWindowViewModel.MaybeRememberFromPrompt` captures a prompt starting with "remember …" and stores it
(project scope when a project is active, else global). The **Autonomy & Memory** settings panel manages it:
an *Enable persistent memory* toggle plus per-scope lists with per-item ✕ and *Clear all*
(`SettingsViewModel.InitializeMemory(projectDir)` is called by the main window before the dialog opens,
like `AgentsPanel.Initialize`).

**Proactive (Phase 5).** When the active agent's `Agent.Proactive` is set, `MainWindowViewModel.SendAsync`
follows a completed turn with a small extra `IChatClient.CompleteAsync` call (`GenerateSuggestionsAsync`,
best-effort/gated) that asks for 2–4 short next-step phrases; `ParseSuggestions` cleans them (strips
bullet/number markers, dedups, caps at 4) and `MessageViewModel.SetSuggestions` attaches them. The
transcript renders them as `Button.suggestion` chips below the answer; clicking one fires
`UseSuggestionCommand`, which **drops the text into the composer** (`InputText`) to edit and send (it does
not auto-send). The Agents editor has a **Proactive** checkbox (`AgentsViewModel.EditProactive`); the
built-in **Autopilot** ships proactive so the feature works out of the box. This completes the 5-phase
Agents roadmap — `Agent` has no persisted-but-unwired fields left.

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

**Voice — text-to-speech.** Reads replies aloud, behind a provider-agnostic surface that mirrors chat
routing. `ISpeechService` (`SpeakAsync`/`StopAsync`/`IsConfigured`) is implemented by `SpeechRouter`,
which picks the engine from `AppSettings.SpeechProvider` (`SpeechProvider.None`/`Piper`), synthesizes via
the chosen `ITtsEngine`, and plays through one shared `IAudioPlayer` (so a single cancellation/stop path).
- *Engine* — `PiperSpeechService` (`IPiperSpeechService : ITtsEngine`) runs the local **Piper** binary
  (`piper --model <voice.onnx> --output_file <tmp.wav>`, text on stdin) from the executable's own folder
  (so it finds `espeak-ng-data`; otherwise it crashes, e.g. `0xC0000409`) and sets `LD_LIBRARY_PATH` on Linux.
- *Playback* — `AudioPlayer` shells to the OS player (Windows `System.Media.SoundPlayer` via PowerShell;
  Linux `paplay`→`aplay`→`ffplay`; macOS `afplay`); `Stop` kills the player process. Zero NuGet deps.
- *Install* — `IPiperInstaller`/`PiperInstaller` downloads the per-OS/arch Piper release into
  `%LOCALAPPDATA%/AI_Interface/piper`, extracts it (zip on Windows, `.tar.gz` via `System.Formats.Tar`),
  chmod+x on Unix, and writes the resolved path to settings. Pinned to release tag `2023.11.14-2`.
- *Voices* — `IPiperVoiceCatalog`/`PiperVoiceCatalog` reads the published `voices.json`, downloads a
  voice's `.onnx` + `.onnx.json` into `…/piper/voices`, and resolves a downloaded voice **by language
  family** for the speech path. `VoiceBrowserWindow`/`VoiceBrowserViewModel` (a Model-Config-style window)
  lists the catalog with a language dropdown, a Downloaded-only toggle, and inline download/remove.
- *Language-aware* — `PiperSpeechService` detects each reply's language (`ILanguageDetector` —
  script ranges + stop-word scoring) and picks the matching downloaded voice, falling back to the default
  (`AppSettings.PiperModelPath`) / any installed voice.
- *UI* — a per-message 🔈 button (`MessageViewModel.IsSpeaking`/`SpeakGlyph`, `SpeakMessageCommand`) and a
  composer **🔊 Auto-read** `ToggleButton` (`AutoSpeakEnabled`, persisted to `AppSettings.AutoSpeakReplies`,
  shown only when `IsVoiceConfigured`) that speaks each completed reply. Settings → AI Features → **Voice**
  has the *Download & install Piper* + *Browse voices* buttons and a manual-paths Advanced expander.
  `HttpDownloads` is the shared streamed-download-with-progress helper for the installer + catalog.

**Settings** (`SettingsService`): JSON under the per-user app-data folder; all reads/writes best-effort.
`SettingsWindow` is a **grouped left nav + content host** (not a flat `TabControl` — a flat one can't show
non-clickable group headers): a left rail with two muted headers (`TextBlock.navheader`) and `Button.nav`
entries, and a right `Panel` whose category panels toggle by `IsVisible` bound to per-category bools on
`SettingsViewModel` (`SelectCategoryCommand` + `SettingsCategory` enum). Panels are moved **verbatim** under:
- **EDITOR FEATURES** — *Appearance* (light/dark + accent/bubble colors), *Typography* (font + size),
  *Layout* (placeholder).
- **AI FEATURES** — *Models* (Local AI: Ollama URL, *Quick setup*, **Download & install Ollama** — the
  one-click `IOllamaInstaller` flow, confirmed via `ConfirmWindow` then auto-connects, *Test connection*,
  *Model_Config*; and Web Models: per-provider API key + Connect, which persists the key, probes, and raises
  `ConnectRequested` so the main window reloads the dropdown), **Agents** (the agent roster master/detail), *Autonomy & Memory*
  (agent approval mode + software-install permission + **persistent-memory** toggle and per-scope fact
  lists, Phase 4), *Web Search* (provider + keys), *Voice* (Piper: *Download & install Piper* + *Browse
  voices* + a manual-paths Advanced expander; raises `VoiceBrowserRequested` to open `VoiceBrowserWindow`),
  *Research & Thinking* (research depth + a **Use Multiple LLMs** Deep Research toggle that reveals
  **Planning Model** / **Synthesize Model** pickers, blank = use the chat model; the Synthesize Model carries
  a privacy note since page contents are sent to it + Thinking *Effort*). The two research pickers are loaded
  by `SettingsViewModel.LoadResearchModelsAsync()` (called from `SettingsWindow.OnLoaded`), which uses the
  injected `IModelRouter` and restores the saved picks under a sync guard so it doesn't re-persist on restore.

**Theming & design system** (`ThemeService` + `SettingsWindow` + `Styles/ControlStyles.axaml`): a flat
"IDE" look modelled on VS Code / Photoshop — the system UI font (Poppins still embedded in `Assets/Fonts`
and selectable by name), a red-orange accent (`#F2542D`), neutral dark-gray surfaces, sharp 3–5px corners,
hairline borders, flat (no gradients/glass/shadows). **Read the `app-style` skill before UI work.**
The app/window icon is `Assets/app-logo.ico` (a multi-size icon generated from `Assets/app-logo.png`),
referenced by `<ApplicationIcon>` in the csproj and `Window.Icon` in `MainWindow.axaml`.
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
- **Code/command bubbles.** The transcript renders an assistant reply from `MessageViewModel.Segments`
  (not the raw `Text`): `MarkdownSegmenter` (in `ViewModels/MessageSegment.cs`) splits the text into prose
  vs. fenced ```` ``` ```` code parts, and `RebuildSegments` reconciles them **in place** on every streamed
  delta (so a streaming code block grows without recreating its container). The answer `ItemsControl`
  spans the **full transcript width** (`HorizontalAlignment="Stretch"`, no `MaxWidth`). Prose renders as
  wrapped text; code renders as a monospace `Border.codeBubble` with a language header + per-block 📋 copy
  (`OnCopyCode`). The code body is a wrapping `SelectableTextBlock` that **sizes to its content** — no inner
  `ScrollViewer` (one under-measured the text height and clipped the last lines); long code just makes a
  taller message and the transcript scrolls. The raw `Text` is still the source of truth for
  copy/persist/speak. Inline single-backtick spans stay literal.
- **Transcript scrolling / last-line clipping.** The transcript `ScrollViewer` (`TranscriptScroll` in
  `MainWindow.axaml`) sets `HorizontalScrollBarVisibility="Disabled"` (so wrapped text is measured at the
  real finite width, never infinite) and gets its bottom breathing room from a **real measured spacer
  `Border` child** at the end of the messages `StackPanel` — **not** `ScrollViewer.Padding`, because
  Avalonia doesn't reliably include bottom padding in the scroll *extent*, so a wrapping
  `SelectableTextBlock` that under-measures its height by ~a line would clip the last reply line even when
  fully scrolled. Auto-scroll (`MainWindow.axaml.cs` `OnScrollToEndRequested`) scrolls immediately for
  streaming responsiveness, then re-scrolls once on the next settled `LayoutUpdated` (one-shot, guarded by
  `IsNearBottom()` so a manual scroll-up isn't overridden) so the end-of-turn offset lands against the
  final extent. Both pieces are needed: the spacer fixes *measurement* clipping; the re-scroll fixes
  *timing* undershoot.
- **Live activity feed (Project mode).** A project run shows what the agent is doing *live*.
  Phase 1: the "work" disclosure auto-expands while `MessageViewModel.IsStreaming` (label `WorkLabel`
  "Working…"/"Activity"), and the `Behaviors/AutoScrollToEnd` attached behavior keeps the box pinned to the
  newest line (releasing on manual scroll-up). Phase 2 replaces the monospace log with a **structured feed**:
  `ProjectAgentService.RunAsync` takes an optional `Action<ActivityUpdate>? onActivityStep` and emits, from
  its loop (not `ExecuteAsync`, which is unchanged), a `Note` for the model's interim narration, a `Started`
  (icon via `IconFor` + the pure `Describe`) before each tool, and a `Finished` (status via `IsFailure`) after.
  The VM marshals these to `MessageViewModel.ApplyActivity`, backing
  `ObservableCollection<ActivityStepViewModel>` (`Activities`), rendered as per-tool rows (icon · title ·
  target · ⏳/✓/✗ status · expandable result) + italic note lines via the shared `ActivityRowTemplate`
  (`MainWindow.axaml` `Window.Resources`). `ShowWorkBlock = HasWork && !HasActivities`
  hides the old monospace block for project runs (chat-with-thinking still uses it). Status ✓ uses the
  themeable `AppSuccessBrush` (added to `App.axaml` ThemeDictionaries). `IconFor`/`IsFailure` are
  `internal static` (unit-tested).
  - **Phase 3A — always-on current action.** The busy status bar (bottom of `MainWindow`, `IsBusy`) shows the
    live step (icon · summary · target) instead of just "Working…": `ExecuteAsync` (and the lead's own-tool
    loop) report `ProjectAgentService.CurrentActionLabel(tool, summary, detail)` (`internal static`, unit-tested
    — collapses newlines, caps the target at 80 chars) to `IProgress<string> status`. The status `TextBlock`
    is in a 2-col `Grid` with `TextTrimming` so a long label ellipsizes.
  - **Phase 3B — structured feed for the Lead + specialists.** The orchestrator path is no longer monospace:
    the Lead's own steps feed the message's structured `Activities`, and each delegation card renders the
    specialist's structured feed (same `ActivityRowTemplate`). See *Structured delegation UI* under
    *Project mode — the orchestrator / lead agent* for the `onActivityStep` / `DelegationUpdate.Step` /
    `ActivityFeed.Apply` wiring.
- **Design-time stubs** in `ViewModels/DesignTimeServices.cs` back the parameterless VM constructors so
  the XAML previewer works. If you add a service dependency to a container-resolved VM, add a matching
  stub (and update the VM's design-time constructor).
- The target framework is **net9.0** — the Avalonia template defaults to net10.0, which this SDK
  can't build, so don't let it revert.
