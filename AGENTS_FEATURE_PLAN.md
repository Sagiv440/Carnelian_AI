# Agents & Personalization Plan — unique agents, personality, autonomy, memory

> Goal: make the AI feel **personal and autonomous** by introducing **unique agents** — each with its
> own personality, skill set, tool permissions, autonomy level, and memory — and reorganize **Settings**
> into two segments (**Editor Features** / **AI Features**). Built to the project's existing architecture
> (Models → Services → ViewModels → Views, DI, MVVM). Cross-platform: **Windows + Linux**.
> Companion to `VOICE_FEATURE_PLAN.md`.

---

## 1. Where we are (recap)

A cross-platform Avalonia / .NET 9 AI client with four modes (Chat, Web Search, Deep Research, Project
agent), multi-provider routing (`IChatClient` + `IModelRouter`: Ollama + OpenAI/Gemini/Anthropic), a
🧠 Thinking toggle with a collapsible reasoning block, attachments, hardware-aware Model Config, voice
(TTS via `ISpeechService`), a flat "IDE" theme, and a **single** sandboxed project agent
(`ProjectAgentService`) gated by `AgentApprovalMode` + `SoftwareInstallPermission`.

**Today there is one implicit "assistant".** The system prompt is a fixed `ChatSystemPrompt` constant
(Chat/Web/Deep) or `ProjectAgentService.SystemPrompt(...)` (Project). Project "skills" are loaded from
the project's `SKILL.md` files (`IProjectSkillService`) and appended to that prompt. There is no concept
of a *named agent*, no persona, no memory, and autonomy is fixed by the global approval mode.

## 2. Where we're going

1. **Phase 1 — Settings segmentation + Agent model & picker.** Group Settings under **Editor Features**
   and **AI Features**; introduce the `Agent` model, an `IAgentService` (built-in + custom, global +
   per-project), an **Agents** settings category to list/create/edit, and an **agent picker** in the
   main window. Selecting an agent applies its **personality** to the system prompt across every mode.
2. **Phase 2 — Skills & permissions per agent.** Each agent carries a skill set (built-in skill packs +
   selected project `SKILL.md` files) and a tool allow-list; wire these into `ProjectAgentService`.
3. **Phase 3 — Autonomy.** A per-agent **autonomy level** that maps to approval mode + step budget +
   an optional *plan-then-execute* pass, so a trusted agent can self-run multi-step tasks.
4. **Phase 4 — Persistent memory.** `IMemoryService` (global + per-project) that remembers facts about
   you and your work, injects them into prompts, and exposes a `remember` tool the agent can call.
5. **Phase 5 — Proactive.** Agents end turns with suggested next steps rendered as clickable chips.

Phases ship independently; each builds clean and is usable on its own.

---

## 3. Key decisions — ✅ DECIDED

| Decision | Choice | Notes |
|---|---|---|
| **Editor/AI segments** | ✅ **Settings left-rail groups** | Reorganize the existing `SettingsWindow` left rail under two non-clickable section headers — **EDITOR FEATURES** and **AI FEATURES**. No new panes in the main window (no code editor for now). |
| **What "personal & autonomous" means** | ✅ **All four** | (a) self-running multi-step autonomy, (b) persistent memory of you, (c) proactive suggestions, (d) distinct personality/tone per agent. |
| **Agent sourcing** | ✅ **Built-in + custom** | Ship a small curated roster (each = persona + skills + tools + default model + autonomy) **and** a create/edit/delete UI. |
| **Agent scope/storage** | ✅ **Both (global + per-project)** | Global agents in the app-data folder (always available); optional per-project agents in `<project>/.AI/agents` layered on top. |

> **Design spine (unchanged):** agents follow the same pluggable shape as `IChatClient`/`ISpeechService` —
> one `IAgentService` surface, agents loaded from multiple sources, a single "active agent" selected from
> the UI. An *agent is data* (a profile), not a new code path; it parameterizes the existing prompt-build
> + tool loop rather than forking it.

---

## 4. Architecture

### 4.1 New Models (`Models/`)

- **`Agent.cs`** — the agent profile (serialized as a Claude-Code-style Markdown file — frontmatter +
  persona body; see §4.2). Also carries an optional `Description`:
  ```csharp
  public sealed class Agent
  {
      public string Id { get; set; } = "";          // stable slug/guid
      public string Name { get; set; } = "";         // "Researcher", "Code Buddy", …
      public string Glyph { get; set; } = "🤖";       // avatar shown in picker/transcript
      public string Persona { get; set; } = "";       // personality/system-prompt text (tone, expertise)
      public string? DefaultModel { get; set; }       // "{provider}:{id}" (optional; falls back to picker)
      public List<string> Skills { get; set; } = new();         // skill-pack ids + project SKILL names
      public AgentTools Tools { get; set; } = new();   // per-tool allow-list (see below)
      public AutonomyLevel Autonomy { get; set; } = AutonomyLevel.Guided;
      public bool MemoryEnabled { get; set; } = true;
      public bool Proactive { get; set; } = false;
      public AgentScope Scope { get; set; } = AgentScope.Global;  // BuiltIn / Global / Project
      public bool IsBuiltIn { get; set; }              // read-only roster entry
  }
  ```
- **`AgentTools.cs`** — bool allow-list mirroring the project-agent tools (`ReadFiles`, `WriteFiles`,
  `DeleteFiles`, `RunCommands`, `InstallSoftware`); the agent only advertises the tools it's allowed.
- **`AutonomyLevel.cs`** — enum `Ask` / `Guided` / `Autonomous` (maps to approval + step budget +
  planning; see §4.6).
- **`AgentScope.cs`** — enum `BuiltIn` / `Global` / `Project`.
- **`MemoryEntry.cs`** — `record MemoryEntry(string Text, string Source, string CreatedAtIso)` for the
  memory store (Phase 4).

### 4.2 New Services (`Services/`)

- **`IAgentService` / `AgentService`** — the agent registry:
  ```csharp
  public interface IAgentService
  {
      IReadOnlyList<Agent> ListAgents(string? projectDir);   // built-in + global + project, de-duped
      Agent? Get(string id, string? projectDir);
      void SaveCustom(Agent agent, string? projectDir);      // global -> app-data; project -> .AI/agents
      void DeleteCustom(string id, string? projectDir);
      Agent Default { get; }                                 // fallback persona ("Assistant")
  }
  ```
  Built-ins are embedded (a static seed list); global customs live in
  `%APPDATA%/AI_Interface/agents/*.md` (`~/.config/AI_Interface/agents` on Linux); project customs in
  `<project>/.AI/agents/*.md`. Best-effort file store (mirrors `SettingsService` / `ChatHistoryService`).

  **Agent file format — Claude-Code-style Markdown** (`AgentMarkdown`, so agents are portable between
  tools): YAML-ish frontmatter between `---` fences (`name`, `description`, `glyph`, `model`, `tools`,
  `skills`, `autonomy`, `memory`, `proactive`) followed by the **persona as the Markdown body**. A plain
  `.md` with no frontmatter loads with its whole text as the persona; unknown keys are ignored; the
  `tools` list maps common Claude-Code tool names (Read/Write/Edit/Bash → read/write/run). Legacy `*.json`
  agents are auto-migrated to `*.md` on load.
- **`IMemoryService` / `MemoryService`** *(Phase 4)* — `Load(scope)`, `Append(scope, entry)`,
  `Clear(scope)`; global memory in app-data, per-project in `<project>/.AI/memory.json`. Produces a
  compact "What you know about the user / this project" block for the system prompt.
- **Prompt assembly** — a small `AgentPromptBuilder` (or methods on `AgentService`) that composes the
  final system prompt from: base instructions → **agent persona** → **memory block** → **skills text** →
  existing `ThinkingDirective()` → (Project) sandbox rules. This replaces the hard-coded `ChatSystemPrompt`
  and feeds `ProjectAgentService.SystemPrompt`.

### 4.3 Settings (`Models/AppSettings.cs`)

```csharp
public string ActiveAgentId { get; set; } = "assistant";  // last-selected agent (like DefaultModel)
// Phase 4:
public bool GlobalMemoryEnabled { get; set; } = true;
```
(Custom agents themselves are separate files, not in settings.json — like chats.)

### 4.4 DI registration (`App.ConfigureServices`)

```csharp
services.AddSingleton<IAgentService, AgentService>();
services.AddSingleton<IMemoryService, MemoryService>();   // Phase 4
```
Add matching no-op stubs in **`ViewModels/DesignTimeServices.cs`** so the XAML previewer still
constructs the VMs.

### 4.5 Personality (Phase 1)

The selected agent's `Persona` is injected at the top of the system prompt for **every** mode (not just
Project). `MainWindowViewModel` resolves the active `Agent` and builds the prompt via `AgentPromptBuilder`;
`ProjectAgentService.SystemPrompt(...)` takes the persona too. The agent's `Glyph`+`Name` show in the
transcript header (replacing the bare model name) so replies feel like they come from *that* agent.

### 4.6 Autonomy (Phase 3)

`AutonomyLevel` maps onto the existing loop knobs — no new agent loop:
| Level | Approval | Step budget | Planning |
|---|---|---|---|
| `Ask` | confirm everything | small | none |
| `Guided` | confirm destructive (today's default) | `MaxSteps` (24) | none |
| `Autonomous` | auto-run (within the agent's tool allow-list + sandbox) | larger | optional plan-then-execute pass |

The active agent's level overrides the global `AgentApproval` for that run; `SoftwareInstallPermission`
still gates installs independently (defense in depth). "Autonomous" may add a first **planning turn**
(decompose the task into steps) before executing — reuses the Thinking/`<think>` plumbing.

### 4.7 Memory (Phase 4)

- On each turn, prepend a memory block (global + project) to the system prompt.
- Add a `remember` **tool** (Project agent) so an autonomous agent can persist a fact; in chat modes, a
  lightweight "📌 Remember this" affordance on a message writes to memory.
- Memory is viewable/clearable from the Agents (or a Memory) settings category.

### 4.8 Proactive (Phase 5)

Proactive agents are instructed to end with up to 3 suggested next steps in a parseable form; the VM
extracts them into **suggestion chips** under the reply that send the chosen text when clicked.

---

## 5. Settings reorganization — Editor Features / AI Features (Phase 1)

> **Read the `app-style` skill before any XAML work** — the left rail uses `TabControl Classes="settings"`
> `TabStripPlacement="Left"` today. A flat `TabControl` can't show non-selectable group headers, so:

**Approach:** replace the flat settings `TabControl` with a **grouped left nav + content host**:
- Left rail = an `ItemsControl` of two sections, each a muted **header** (`TextBlock`, "EDITOR FEATURES" /
  "AI FEATURES") followed by category entries styled with the existing `Button.nav` / `.active` classes
  (already defined in `MainWindow.axaml`; promote them to `ControlStyles.axaml` for reuse).
- Right side = a `ContentControl` whose content is selected by a `SettingsCategory` enum (data-templated,
  or a set of `Border`s toggled by `IsVisible`) — reuses the current tab bodies verbatim.
- `SettingsViewModel` gains `SelectedCategory` + the grouped category list.

**Proposed mapping:**
- **EDITOR FEATURES:** *Appearance* (light/dark + colors), *Typography* (font + size), *Layout* (composer
  width / transcript density — placeholder for future editor-pane settings).
- **AI FEATURES:** *Models* (the current AI Model → Local AI / Web Models), *Agents* (**NEW** — roster +
  create/edit), *Autonomy & Memory* (the current Project approval + software-install + memory toggles),
  *Web Search*, *Voice*, *Research & Thinking* (research depth + Thinking effort, moved out of *General*).

No behavior changes to the individual panels in this phase — only their grouping/navigation.

---

## 6. UI integration

### 6.1 Agents settings category (Phase 1–2)
A master/detail panel under **AI FEATURES → Agents**: a list of agents (built-in badge vs custom),
**+ New** / duplicate / delete, and an editor for Name, Glyph, Persona (multiline), Default model
(the provider-aware picker), Skills (checklist of built-in packs + project `SKILL.md` names — Phase 2),
Tool permissions (checkboxes — Phase 2), Autonomy level (radios — Phase 3), Memory + Proactive toggles.
Built-ins are read-only but **Duplicate**-able into an editable custom agent.

### 6.2 Agent picker (Phase 1)
A compact picker in the **top bar** (left of, or beside, the model dropdown) showing the active agent's
`Glyph + Name`; opening it lists available agents (grouped built-in / global / project) with a "Manage…"
entry that opens Settings → Agents. Selecting an agent updates `ActiveAgentId` and re-derives the system
prompt. (Sits next to the existing model picker so model and persona are chosen independently.)

### 6.3 Transcript (Phase 1)
Assistant rows show the active agent's `Glyph + Name` in the header instead of the bare model id (the
model id moves to a tooltip). Suggestion chips render under proactive replies (Phase 5).

---

## 7. Threading & cancellation
- `AgentService` / `MemoryService` file I/O is best-effort and synchronous-light; do heavier scans on a
  background thread and **marshal VM/UI updates with `Dispatcher.UIThread.Post`** (project threading rule).
- The autonomy/planning pass reuses the existing `ProjectAgentService` loop (already `ConfigureAwait(false)`
  + `onActivity`/`onAnswer` callbacks marshalled by the VM). No new threading surface.

---

## 8. File-by-file change list

**New (Phase 1):**
- `Models/Agent.cs`, `Models/AgentTools.cs`, `Models/AutonomyLevel.cs`, `Models/AgentScope.cs`
- `Services/IAgentService.cs`, `Services/AgentService.cs` (built-in seed + global/project Markdown stores),
  `Services/AgentMarkdown.cs` (Claude-Code frontmatter + persona-body (de)serializer)
- `Services/AgentPromptBuilder.cs` (compose system prompt from persona + skills + memory)
- `ViewModels/AgentsViewModel.cs` (Agents settings panel) + agent-picker state on `MainWindowViewModel`

**New (later phases):**
- `Services/IMemoryService.cs` + `MemoryService.cs`, `Models/MemoryEntry.cs` (Phase 4)

**Edited:**
- `App.axaml.cs` (DI), `Models/AppSettings.cs` (`ActiveAgentId`, memory flags)
- `ViewModels/MainWindowViewModel.cs` (active agent, prompt assembly, picker, header glyph; Phase 5 chips)
- `ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.axaml(.cs)` (grouped left nav + Agents panel +
  re-homed panels)
- `Services/ProjectAgentService.cs` (+ interface): take the `Agent` (persona, tool allow-list, autonomy) so
  the tool loop reflects the active agent; replace hard-coded approval/steps with the autonomy mapping
- `Views/MainWindow.axaml(.cs)` (agent picker in the top bar, transcript header)
- `ViewModels/DesignTimeServices.cs` (stubs: `DesignAgentService`, `DesignMemoryService`)
- `Styles/ControlStyles.axaml` (promote `Button.nav` + section-header styles for the settings rail)
- `CLAUDE.md`, `.claude/skills/add-mode` + `app-style` (document agents + the settings segmentation)

---

## 9. Verification (no test project yet → manual)
1. `dotnet build AI_Interface.sln` clean (0/0). Stop the app first on Windows (`Stop-Process -Name AI_Interface -Force`).
2. Settings opens with **Editor Features** / **AI Features** groups; every existing panel still works under its new home.
3. Pick a built-in agent in the top-bar picker → its persona visibly changes the reply's tone; the transcript header shows its glyph/name.
4. Create a custom agent (global) → it appears in the picker and persists across restarts. Create a per-project agent → it appears only with that project open.
5. (Phase 2) An agent with file-write disabled cannot write in Project mode; one with it enabled can.
6. (Phase 3) An `Autonomous` agent runs a multi-step project task with fewer prompts; install still gated by `SoftwareInstallPermission`.
7. (Phase 4) Tell the AI a fact, start a new session → it recalls it; clearing memory forgets it.
8. (Phase 5) A proactive agent shows clickable next-step chips that send when clicked.
9. Re-verify on Linux; confirm no UI-thread exceptions and JSON stores land in `~/.config/AI_Interface`.

---

## 10. Build order
1. ✅ `Agent` + enums + `IAgentService`/`AgentService` (built-in seed + global/project JSON) + DI + design stub.
2. ✅ `AgentPromptBuilder`; route Chat/Web/Deep/Project system prompts through it (persona only).
3. ✅ Settings: grouped **Editor/AI** left nav + **Agents** panel (list/create/edit/delete); top-bar agent picker; transcript header glyph.
4. ✅ Phase 2: per-agent skills (built-in packs + project `SKILL.md`) + tool allow-list → `ProjectAgentService`.
5. ✅ Phase 3: `AutonomyLevel` → approval/steps/planning mapping.
6. ✅ Phase 4: `IMemoryService` (Markdown store) + prompt injection + `remember` tool + memory management UI.
7. ✅ Phase 5: proactive next-step suggestion chips.

---

## 11. Open questions (to confirm before/while building)
- **Built-in roster:** which starter agents? Proposed: *Assistant* (neutral, no tools), *Researcher*
  (web/deep search bias), *Code Buddy* (project tools, Guided), *Autopilot* (project tools, Autonomous).
- **Agent vs model:** keep them **independent** (pick a persona *and* a model) — assumed yes; an agent's
  `DefaultModel` just pre-selects one.
- **Memory granularity (Phase 4):** auto-extract facts vs only explicit "remember" — start explicit, add
  auto-extract later.

---

## ✅ Phase 1 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors)

**New:** `Models/Agent.cs`, `Models/AgentTools.cs`, `Models/AutonomyLevel.cs`, `Models/AgentScope.cs`,
`Services/IAgentService.cs` + `AgentService.cs` (embedded seed `assistant`/`researcher`/`code-buddy`/`autopilot`;
global customs in `<app-data>/AI_Interface/agents/*.md`, project customs in `<project>/.AI/agents/*.md`,
Claude-Code-style frontmatter + persona body via `AgentMarkdown`),
`Services/AgentPromptBuilder.cs`, `ViewModels/AgentsViewModel.cs`, `ViewModels/SettingsCategory.cs`.

**Edited:** `Models/AppSettings.cs` (`ActiveAgentId`), `App.axaml.cs` (DI), `ViewModels/DesignTimeServices.cs`
(stub + persona-param updates), `Services/IDeepResearchService.cs`/`DeepResearchService.cs` and
`Services/IProjectAgentService.cs`/`ProjectAgentService.cs` (added a `personaPrefix` param),
`ViewModels/MainWindowViewModel.cs` (active agent + `AgentPromptBuilder` routing + transcript identity),
`ViewModels/MessageViewModel.cs` (agent glyph/name header), `ViewModels/SettingsViewModel.cs`
(grouped categories + Agents panel), `Views/SettingsWindow.axaml(.cs)` (Editor/AI grouped nav + content host
+ Agents master/detail), `Views/MainWindow.axaml(.cs)` (top-bar agent picker, header tooltip),
`Styles/ControlStyles.axaml` (promoted `Button.nav` + `TextBlock.navheader`), `CLAUDE.md`.

**Prompt assembly:** `AgentPromptBuilder.Compose(agent, baseInstructions, thinkingDirective)` for Chat;
`PersonaPrefix(agent)` prepended to the Web/Deep/Project system prompts. Agent and model are independent
(separate top-bar pickers). Built-ins are read-only; **Duplicate** makes an editable global custom.

**Known Phase-1 limitations (by design):** Skills/Tools/Autonomy/Memory/Proactive fields persist but are
not yet enforced or surfaced (Phases 2–5). Reopened saved chats show the model id (agent identity isn't
persisted into a session turn yet). The master list refreshes agent name/glyph on panel reload.

**To try it:** pick an agent in the top-bar picker (left of the model dropdown) → reply tone shifts and the
transcript header shows its glyph/name. Settings → AI Features → **Agents**: Duplicate a built-in, edit its
persona, restart → it persists. Open a project, add a per-project agent → it shows only with that project open.

---

## ✅ Phase 2 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors)

**New:** `Models/SkillCatalog.cs` (`SkillPack(Id, Name, Content)` + the built-in packs `cited-research` /
`concise` / `careful-coding` / `step-by-step`); `AgentToolGroup` enum + `Allows`/`Restrict` on
`Models/AgentTools.cs`; `ViewModels/AgentsViewModel.SkillChoice` (skills-checklist row).

**Edited:** `Services/ProjectAgentService.cs` (+ `IProjectAgentService` + `DesignProjectAgentService`):
`RunAsync` takes `AgentTools allowedTools`; `BuildTools` advertises only permitted groups; `ExecuteAsync`
refuses a disallowed tool (defense in depth). `Services/AgentPromptBuilder.cs` (built-in skill packs appended
in every mode via `SkillsBlock`/`PersonaPrefix`/`Compose`). `Services/AgentService.cs` (seed agents now set
explicit `Tools` + default `Skills`). `ViewModels/MainWindowViewModel.cs` (passes `SelectedAgent.Tools` to
the agent; `ProjectSkillsContext` filters project skills by the agent's selection). `ViewModels/AgentsViewModel.cs`
+ `Views/SettingsWindow.axaml` (tool-permission checkbox row + skills checklist). `CLAUDE.md`.

**`AgentTools` unrestricted-vs-explicit representation:** `AllowAll` (defaults `true`). An un-customized
agent — including the built-in Assistant if its tools weren't explicitly set — is unrestricted (full toolset,
behaviour unchanged). The Agents editor calls `Restrict()` on the first permission toggle, snapshotting today's
all-on state, then the per-tool booleans are authoritative. `install_software` is double-gated: agent
`InstallSoftware=true` AND global `SoftwareInstallPermission != Never`.

**Where the wiring lives:** tool filter — `ProjectAgentService.BuildTools` (advertise) + `ExecuteAsync`
(refuse) keyed by `ToolGroupOf`/`AgentTools.Allows`. Built-in skills — `AgentPromptBuilder.SkillsBlock`
(all modes). Project skills filter — `MainWindowViewModel.ProjectSkillsContext`.

**Known Phase-2 limitations (by design):** Autonomy/Memory/Proactive still persist-only (Phases 3–5).

**To try it:** Duplicate Code Buddy, uncheck **Write** under Tool permissions → in Project mode it can
list/read but not write/create (the tool is absent and refused if forced). Pick an agent with the `concise`
skill pack → replies get terser. With a project open, the agent's **Skills** checklist also lists the
project's `SKILL.md` names; checking specific ones narrows what's loaded in Project mode.

---

## ✅ Phase 3 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors)

**The active agent's `Autonomy` is authoritative for a project-agent run** (overrides the global approval
mode), mapping to approval + step budget + planning:

| Level | Effective approval | Step budget | Planning |
|---|---|---|---|
| `Ask` | `ConfirmEverything` | 8 | none |
| `Guided` | `ConfirmDestructive` | 24 (= today's default) | none |
| `Autonomous` | `AutoRun` | 40 | plan-then-execute directive |

**New mapping (one place):** `Models/AutonomyLevel.cs` — `AutonomyMap.ForRun(level) → (approval, maxSteps)`
and `AutonomyMap.FromApprovalMode(mode) → level` (global-setting ↔ autonomy). `AgentPromptBuilder.PlanningDirective(autonomy)`
returns the plan-then-execute text for `Autonomous` (empty otherwise) — a prompt directive, not a separate round.

**Edited:** `Services/ProjectAgentService.cs` (+ `IProjectAgentService` + `DesignProjectAgentService` stub):
`RunAsync` gained an `int maxSteps` param; the const `MaxSteps` became `DefaultMaxSteps` (=24) fallback.
`ViewModels/MainWindowViewModel.cs` (`RunProjectAgentAsync` derives `(approval, maxSteps)` from
`SelectedAgent.Autonomy`, appends `PlanningDirective` to the directives, passes both to the service —
`SoftwareInstall` still passed independently). `ViewModels/AgentsViewModel.cs` (now injects `ISettingsService`;
`New()` seeds `Autonomy` from the global `AgentApproval`; an `Autonomy` observable + three radio bools +
load/persist). `Views/SettingsWindow.axaml` (Autonomy radios in the Agents editor + a muted note in
Autonomy & Memory that the active agent governs each run). `Models/Agent.cs` / `AutonomyLevel.cs` doc updates.
`App.ConfigureServices()` unchanged (`AgentsViewModel` is transient; `ISettingsService` already registered).

**Where the wiring lives:** autonomy→(approval, steps) — `AutonomyMap.ForRun` consumed in
`MainWindowViewModel.RunProjectAgentAsync`. Planning — `AgentPromptBuilder.PlanningDirective`. Global-setting
→ new-agent default — `AutonomyMap.FromApprovalMode` in `AgentsViewModel.New()`. `SoftwareInstallPermission`
stays a separate gate (`ProjectAgentService.ExecuteAsync`/`BuildTools`, untouched).

**Known Phase-3 limitations (by design):** Memory/Proactive still persist-only (Phases 4–5). The
plan-then-execute pass is a prompt directive (no forced separate planning round), reusing the tool loop.

**To try it:** in Settings → AI Features → Agents, set a custom agent's **Autonomy** to *Autonomous* → in
Project mode it auto-runs multi-step (within its tool allow-list + sandbox) and its reply opens with a short
numbered plan. *Ask* prompts before every tool call; *Guided* (the default) is unchanged (confirm-destructive,
24 steps). With `SoftwareInstall = No permission`, an Autonomous agent allowed Install software still can't
install (the tool is withheld and system-install commands refused).

---

## ✅ Phase 4 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors)

**Persistent memory in two scopes, stored as portable Markdown (changed from the planned JSON):**
**global** facts about the user in `<app-data>/AI_Interface/memory.md` and **project** facts in
`<project>/.AI/memory.md`. Format = a `# Memory` heading + one `- ` bullet per fact, with optional
`<!-- source · date -->` metadata — safe to hand-edit and move between tools (mirrors `AgentMarkdown`).

**New:** `Models/MemoryEntry.cs` (`record MemoryEntry(Text, Source, CreatedAtIso)` + `MemoryScope` enum),
`Services/MemoryMarkdown.cs` (Serialize/Parse), `Services/IMemoryService.cs` + `MemoryService.cs`
(`Load`/`Add`(dedups exact text)/`Remove`/`Clear`/`BuildContextBlock`), DI registration, `DesignMemoryService` stub.

**Injection (all four modes):** `AgentPromptBuilder.Compose`/`PersonaPrefix` gained a `memoryBlock` arg;
`MainWindowViewModel.MemoryBlock()` builds it when `MemoryActive()` (global `GlobalMemoryEnabled` **and** the
active agent's `Agent.MemoryEnabled`). Two write paths: the project agent's **`remember` tool**
(`ProjectAgentService` now ctor-injects `IMemoryService`; offered only when `memoryEnabled`; `scope:"user"`→
global, else project) and an **explicit chat trigger** (`MaybeRememberFromPrompt` stores a prompt starting
with "remember …" — project scope when a project is open, else global).

**Settings UI:** Autonomy & Memory panel gained an *Enable persistent memory* toggle
(`AppSettings.GlobalMemoryEnabled`) + per-scope fact lists with per-item ✕ and *Clear all*
(`SettingsViewModel.InitializeMemory(projectDir)`, called by the main window before the dialog opens).

**Known Phase-4 limitations (by design):** only *explicit* capture (the `remember` tool + the "remember …"
chat trigger) — no silent auto-extraction of facts from every message (deferred, per §11). `Proactive`
remains the only persist-only field (Phase 5).

**To try it:** type `remember I prefer tabs over spaces` in chat → it's saved to *About you*; start a **new**
chat and ask "what do you know about me?" → the fact is in the prompt. In a project, ask the agent to note
something and it can call `remember` (scope project). Manage/forget facts in Settings → AI Features →
Autonomy & Memory. Turn off *Enable persistent memory* → nothing is injected and the tool is withheld.

---

## ✅ Phase 5 status — IMPLEMENTED (builds clean: 0 warnings, 0 errors) — roadmap complete

**A proactive agent ends its turn with clickable next-step chips.** When the active agent's
`Agent.Proactive` is on, `MainWindowViewModel.SendAsync` follows the completed turn with a small extra
`IChatClient.CompleteAsync` call (`GenerateSuggestionsAsync`) asking for 2–4 short follow-up phrases;
`ParseSuggestions` cleans/dedups/caps them and `MessageViewModel.SetSuggestions` attaches them. They render
as `Button.suggestion` pills below the answer; clicking one fires `UseSuggestionCommand`, which **drops the
text into the composer** (`InputText`) to review/edit/send — it does **not** auto-send (chosen default).

**Generation = a separate cheap completion** (chosen over an inline structured block), so it's
model-agnostic and never corrupts the visible answer. It's **best-effort and gated**: skipped unless the
agent is proactive, the turn produced a real answer, and the run wasn't cancelled; any failure or a `NONE`
reply yields no chips and never disturbs the completed turn.

**New/edited:** `MessageViewModel` (`Suggestions` + `HasSuggestions` + `SetSuggestions`);
`MainWindowViewModel` (`GenerateSuggestionsAsync` + `ParseSuggestions` + `UseSuggestionCommand`, plus the
gated call after the mode switch); `AgentsViewModel` (`EditProactive` + load/persist); `Views/MainWindow.axaml`
(`Button.suggestion` style + a chip `ItemsControl` in the transcript template); `Views/SettingsWindow.axaml`
(a **Proactive** checkbox in the Agents editor); `Services/AgentService.cs` (built-in **Autopilot** ships
`Proactive = true`).

**To try it:** pick **Autopilot** (or check **Proactive** on a custom agent in Settings → AI Features →
Agents), send a prompt → after the answer, next-step chips appear; click one and it lands in the composer.
Non-proactive agents behave exactly as before (no extra call).

**Roadmap complete:** all five phases (segmentation → skills/tools → autonomy → memory → proactive) are
implemented; `Agent` has no persisted-but-unwired fields left.
