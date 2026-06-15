
<h1 style="font-weight: bold;">
  <img src="src/AI_Interface/Assets/app-logo.png" alt="AI Interface logo" width="40" height="40" align="center" />
  Carnelian
</h1>

<img src="https://github.com/Sagiv440/Carnelian_AI/blob/main/src/AI_Interface/Assets/Screenshot%202026-06-13%20225409.png?raw=true" alt="AI Interface logo" width="800" height="600" align="center" />

A cross-platform AI harness with local and cloud providers, allowing AI to work directly
within the confines of your projects — editing files, configuring projects, and setting up
environments for your development and planning.
<br/>
<br/>
This app started as a [Claude Code](https://claude.ai/) project for learning AI, but I find myself using it in my day-to-day life.
And I think you might like it too.

## About

Carnelian is a privacy-first AI workspace. Point it at a local Ollama server and chat
entirely offline, or add a cloud API key to mix in hosted models. It runs four modes
from one window — direct chat, web-augmented answers, deep research, and a tool-using
agent scoped to a project directory.

## Features

- **Four modes** — **Chat**, **Web Search** (one search injected as context),
  **Deep Research** (plan → read → cited report), and **Project** (a sandboxed,
  tool-using agent that reads/writes files, runs commands, and edits docs).
- **Local or cloud** — Ollama out of the box; OpenAI, Gemini, Claude, DeepSeek,
  Nvidia and Mistral via API key, with per-provider budget tracking.
- **Project agents & a Lead orchestrator** — selectable personas that can delegate
  subtasks to specialist agents, ask clarifying questions, and plan work in phases.
- **MCP support** — connect external Model Context Protocol servers (stdio or HTTP)
  for extra tools, resources, and prompts.
- **Persistent memory** — global + per-project facts injected into every mode.
- **Voice** — read replies aloud with a one-click local Piper TTS engine.
- **Extras** — per-prompt Thinking toggle, hardware-aware model recommender,
  full Markdown rendering, image/document attachments, and Office-doc generation.

## Quick Start
### Setup
1. Download the latest build [here](https://github.com/Sagiv440/Carnelian_AI/releases)
2. Install [Ollama](https://ollama.com) and start it: `ollama serve`
3. Pull a model: `ollama pull llama3` (the Project agent needs a tool-calling model,
   e.g. `ollama pull llama3.1`).
4. Run the app (see below). Cloud providers are optional — add keys in
   **Settings → AI Model → Web Models**.

### Start Using

- **Pick a model** from the dropdown at the top-right; the dot beside it shows connection status. **Pick
  an agent** from the picker next to it — agent and model are independent.
- **New Chat / Project** buttons are in the sidebar. Creating or opening a project enters Project mode;
  its chats are saved under `<project>/.AI/chats`, and any skill files in the project are loaded into
  the agent's system prompt. While a project is active the sidebar gains a **Files** tab (a live tree of
  the project directory).
- **Project agent safety** is governed by a single global **approval mode** (Settings → Autonomy &
  Memory; see below), with a three-way **software-install** permission (no permission / ask every time /
  allow) kept as an independent gate. File operations are sandboxed to the project directory.
- **Settings** (⚙, top-right) has a grouped left rail under two headers:
  - **Editor Features** — *Appearance* (light/dark + accent/bubble colors), *Typography* (font + size),
    *Layout*.
  - **AI Features** — *Models* (Local AI + Web Models, plus Model Config), *Agents* (the agent roster),
    *Autonomy & Memory*, *Web Search*, *Voice*, *Research & Thinking* (research depth, the **Use Multiple
    LLMs** planning/synthesis model pickers, and Thinking effort).

## Build and run

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build Carnelian.sln
dotnet run --project src/AI_Interface
```

Self-contained, single-file binaries (no .NET install needed on the target):

### Linux

```bash
./build/publish-linux.sh        # -> publish/linux-x64/AI_Interface
```

Optional packaging: `build/packaging/deb/build-deb.sh` (.deb) and `build/flatpak/` (Flatpak).

### Windows

```powershell
pwsh ./build/publish-windows.ps1   # -> publish/win-x64/Carnelian.exe
```
## Agents

An **agent** is a reusable profile that shapes how the assistant behaves in every mode — it bundles a
**persona** (personality / system prompt), a set of **skills**, and a **tool allow-list**. Pick the
active agent from the top-bar picker; manage them in **Settings → AI Features → Agents**. (Approval
behaviour — whether an agent asks before acting — is a single global setting, not per-agent; see
*Autonomy* below.)

Five agents are built in:

| Agent | Tools | Notes |
|-------|-------|-------|
| 🤖 **Assistant** | read-only | Neutral general-purpose helper. |
| 🔬 **Researcher** | read-only | Evidence-first, cites sources (`cited-research` skill). |
| 👨‍💻 **Code Buddy** | files + commands | Careful senior engineer (`careful-coding` skill). |
| 🚀 **Autopilot** | full + installs | Drives a goal to completion; proactive (`step-by-step` skill). |
| 🧭 **Lead** | read/write/delete/run (team ceiling, no install) | Coordinates a team: delegates subtasks to the other agents (see below). |

#### Lead — the orchestrator

In **Project** mode the **Lead** agent doesn't do the hands-on work itself. It reads your roster of
specialist agents, breaks your goal into subtasks, and **delegates** each to the best-fit specialist —
which runs with its own persona and model (its tools capped to the Lead's ceiling) — then reviews the results and follows up
(for example, asking a reviewer to check an implementer's work) until the goal is met. Pick **Lead** in
the top-bar agent picker, enter a project, and send a goal. The Lead and the specialists it delegates to
all need a tool-calling-capable model. (A lead can never delegate to another lead.)

Each delegation shows up in the transcript as its own **collapsible card** — the specialist's glyph, name,
task, a running/done indicator, and (when expanded) its activity log and result — while the Lead's own
planning stays in the reply's "Thinking" block. Any custom agent can be turned into a lead with the
**Lead / orchestrator** checkbox in the agent editor (Settings → AI Features → Agents → Behaviour).

**Autonomy** is a single global setting (**Settings → AI Features → Autonomy & Memory**) — there is no
per-agent autonomy. It sets the approval mode and how many tool steps an agent may take before it must
reply, and it governs **every** Project-agent run, including the specialists a Lead delegates to:

| Approval mode | Asks before | Step budget | Planning |
|---------------|-------------|-------------|----------|
| **Confirm every action** | every tool call (including reads) | 8 | — |
| **Confirm destructive actions** | writes, deletes, commands | 24 | — |
| **Auto-run everything** | nothing | 40 | outlines a plan, then executes |

Software installs stay independently gated: even under Auto-run an agent still can't install unless the
software-install permission allows it *and* the agent's tools include installs.

**Skills** are either built-in skill packs (`cited-research`, `concise`, `careful-coding`,
`step-by-step`) or per-project `SKILL.md` files the agent can be pointed at.

**Proactive** agents end each reply with a few **clickable next-step chips**; clicking one drops it into
the composer to edit and send. Toggle it per agent in the Agents editor — the built-in **Autopilot** is
proactive out of the box.

### Project skills

Drop Markdown guidance files in a project's **`.AI/skills/`** folder (any `*.md`) — when you open the
project, the agent reads them as authoritative "how to work here" guidance (conventions, architecture,
do/don't rules). Files named `SKILL.md` / `*.skill.md` and anything under a `skills/` folder
(e.g. `.claude/skills/`) are picked up too.

You can also have the agent **write one for you**: in a project, ask *"create a skill for our API
conventions"* and it uses its `create_skill` tool to author a structured skill file under `.AI/skills/`.
The new skill loads as guidance on the next turn.

### Project handbook (`AI_DOCS.md`)

For a single, authoritative *"how this project works"* brief, drop an **`.AI/AI_DOCS.md`** file in the
project — the app's equivalent of a CLAUDE.md. When you're in **Project mode**, its contents are injected
into the agent's system prompt (the single agent, the **Lead**, and any specialists the Lead delegates to)
and treated as authoritative project instructions. It's loaded only in Project mode — never in Chat, Web
Search, or Deep Research — and the active-project card shows **📄 AI_DOCS loaded** when present.

You can write it by hand, or let the **main agent maintain it**: the top-level agent (the one you pick, or
the **Lead**) has an `update_docs` tool and is told to keep the handbook current — like a developer tending a
CLAUDE.md — when a durable rule or convention changes (not for running notes; those go to memory). Delegated
specialists can't touch it, and the file is locked so only `update_docs` can change it.

**Custom agents** are stored as portable **Markdown files** (Claude-Code-style frontmatter + a persona
body), so you can move them between tools:

- **Global:** `<app-data>/AI_Interface/agents/<id>.md`
- **Per-project:** `<project>/.AI/agents/<id>.md`

Built-in agents are read-only; **＋ New** and **Duplicate** create global customs you can rename, repersonalize, and re-scope.

## Settings

User settings (Ollama URL, cloud API keys, last model + agent, theme, font, search depth, agent
approval mode, software-install permission, …) are stored as JSON at:

- **Windows:** `%APPDATA%\AI_Interface\settings.json`
- **Linux:** `~/.config/AI_Interface/settings.json`

Custom agents and global memory live beside it as portable Markdown (`agents/*.md`, `memory.md`);
per-project agents, chats, skills and memory live under the project's own `.AI/` folder.

## Project layout

```
src/AI_Interface/
  Models/        Plain data types (chat, providers, agent profile/tools, model catalog, settings)
  Services/      Provider clients (Ollama + OpenAI/Gemini/Anthropic) behind IChatClient + a router,
                 web search, deep-research + project-agent orchestrators, agent registry,
                 hardware scan, attachments, theme + settings stores
  ViewModels/    MVVM view models (CommunityToolkit.Mvvm)
  Views/         Avalonia XAML windows
build/           Publish scripts for Windows and Linux
```


## Acknowledgements

Carnelian builds on these excellent projects:

- [Ollama](https://ollama.com) — local model runtime
- [Avalonia](https://avaloniaui.net) — cross-platform .NET UI framework
- [Piper](https://github.com/rhasspy/piper) — local neural text-to-speech
- [Model Context Protocol](https://modelcontextprotocol.io) — external tool/resource standard

## License

Licensed under the [GNU General Public License v3.0](LICENSE).
