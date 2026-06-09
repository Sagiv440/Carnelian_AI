
<h1 style="font-weight: bold;">
  <img src="src/AI_Interface/Assets/app-logo.png" alt="AI Interface logo" width="40" height="40" align="center" />
  AI Interface
</h1>
A cross-platform desktop app for running AI. It runs models <strong>locally</strong> through a local
[Ollama](https://ollama.com) server, and can **optionally** talk to cloud providers — OpenAI (ChatGPT),
Google Gemini, and Anthropic (Claude) — when you add an API key. Every provider's models share one
picker, and all modes work the same regardless of which model you choose.

There are four ways to work, chosen from the sidebar and the composer:

- **Chat** — a normal conversation with the model, streamed token-by-token.
- **Web Search** — a search-scope dropdown in the composer: runs one web search for your question and
  answers with inline citations.
- **Deep Research** — the model plans several search queries, the app searches the web and reads the
  top pages, then the model synthesizes a cited report. With **Use Multiple LLMs** (Settings → Research &
  Thinking) you can assign a separate model to each of its two AI steps — query planning and report
  synthesis — leaving either blank to use your chat model. The synthesis model receives the fetched page
  text, so keep it local (Ollama) if your sources are sensitive.
- **Project** — a tool-using **agent** scoped to a project folder. It can list, read, write and delete
  files, create folders, run terminal commands, and (when permitted) install software — all confined to
  the project directory — to build or fix a project for you.

Cross-cutting extras:

- **🤝 Agents** — a selectable **persona** (top-bar picker) whose voice is layered into *every* mode.
  Each agent bundles a personality, a set of **skills**, and a **tool allow-list** (approval behaviour is
  a single global setting, not per-agent). Five are built in (Assistant, Researcher, Code Buddy, Autopilot, and **Lead** — which
  coordinates a team by delegating subtasks to the others), and you can create your own, globally or
  per-project. See [Agents](#agents) below.
- **🧩 Memory** — persistent facts the assistant recalls across sessions, in two scopes: **global**
  (about you) and **per-project**. Stored as editable Markdown (`memory.md`) so it's portable. Say
  *"remember …"* in chat, let the Project agent's `remember` tool save a note, and manage/forget facts in
  Settings → AI Features → Autonomy & Memory.
- **🧠 Thinking** — a per-prompt toggle that turns on the model's native reasoning (Ollama models). The
  chain-of-thought is shown in a collapsible **"Thinking"** block above each answer (works with reasoning
  models such as `qwen3` / `deepseek-r1`); its depth is set by an *Effort* slider in Settings.
- **🔊 Voice** — read replies aloud with a free, **offline** local TTS engine ([Piper](https://github.com/rhasspy/piper)).
  Install it in one click from **Settings → AI Features → Voice**, then **Browse voices** to download
  voices for the languages you use. Each reply plays in a voice that matches **its language** automatically.
  Use the per-message 🔈 button, or the composer's **🔊 Auto-read** toggle to speak every reply. No key,
  no cloud — the text never leaves your machine.
- **Attachments** — the composer's 📎 menu attaches images (for vision models) or documents; text is
  extracted from PDF, DOCX, ODT and plain-text/code files and folded into the prompt.
- **Model Config** — a hardware-aware recommender (Settings → AI Features → Models) that scans your
  CPU/RAM and GPU/VRAM and ranks Ollama models by fit, with inline download/remove.

Local Ollama models run entirely on your machine; cloud providers are off unless you supply a key. The
only other network traffic is the web searches and page fetches used by Web Search and Deep Research.

Built with [Avalonia UI](https://avaloniaui.net) on .NET 9, so the same code builds and runs on both
**Windows** and **Linux**. The UI is a flat "IDE" theme (light/dark, configurable accent and font).

## Prerequisites

1. **.NET 9 SDK** — https://dotnet.microsoft.com/download
2. **Ollama**, running locally — https://ollama.com
   ```bash
   ollama serve            # starts the local server on http://localhost:11434
   ollama pull llama3      # pull at least one model
   ```
   Don't have it yet? You can install it from inside the app with one click — open **Settings → AI
   Features → Models → Local AI** and press **⬇ Download & install Ollama** (it downloads the official
   installer, runs it, then connects). On Windows the installer may show a permission (UAC) prompt; on
   Linux it uses the official `curl … | sh` script (needs `curl`, may ask for your password). On macOS,
   install manually from https://ollama.com/download.

   For **Project** mode, pull a tool-calling-capable model (e.g. `llama3.1`, `qwen2.5`, `mistral-nemo`).
   For the **Thinking** block to populate, use a reasoning model (e.g. `qwen3`, `deepseek-r1`).
3. **(Optional) Cloud API keys** — to use OpenAI, Gemini or Anthropic models instead of (or alongside)
   Ollama, add the provider's key under **Settings → AI Features → Models → Web Models**. Without a key
   a provider simply contributes nothing to the model picker; the app stays fully usable on local
   models alone.

## Run from source

```bash
dotnet run --project src/AI_Interface
```

The app connects to `http://localhost:11434` by default and loads your installed models into the
**model picker at the top-right** (each entry is tagged with its provider). To point it at a different
Ollama instance, open **Settings → AI Features → Models → Local AI**, set the server URL (or use *Quick
setup*), and click **Connect** (or *Test connection* to just probe it). If Ollama isn't installed yet,
**⬇ Download & install Ollama** on the same tab fetches and sets it up for you, then connects. Cloud
models appear in the same picker once you add a key under **Web Models**.

## Build a distributable app

Self-contained, single-file builds (no .NET install needed on the target machine):

```bash
# Windows  ->  publish/win-x64/AI_Interface.exe
pwsh ./build/publish-windows.ps1

# Linux    ->  publish/linux-x64/AI_Interface
./build/publish-linux.sh
```

Either script also cross-publishes from the other OS — the .NET SDK can target both RIDs.

> On Windows a running instance locks `AI_Interface.exe`, so a rebuild's copy step fails while the app
> is open. Stop it first: `Stop-Process -Name AI_Interface -Force`.

## Using it

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

See [CLAUDE.md](CLAUDE.md) for architecture details.
