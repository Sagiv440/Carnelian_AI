# AI Interface

A cross-platform desktop app for running AI **locally**. It talks to a local
[Ollama](https://ollama.com) server and gives you four ways to work, chosen from the sidebar and the
composer:

- **Chat** — a normal conversation with the local model, streamed token-by-token.
- **Web Search** — a per-prompt 🌐 toggle: runs one web search for your question and answers with
  inline citations.
- **Deep Research** — the model plans several search queries, the app searches the web and reads the
  top pages, then the model synthesizes a cited report.
- **Project** — a tool-using **agent** scoped to a project folder. It can list, read, write and delete
  files, create folders, and run terminal commands — all confined to the project directory — to build
  or fix a project for you.

Cross-cutting extras:

- **🧠 Thinking** — a per-prompt toggle that turns on the model's native reasoning. The chain-of-thought
  is shown in a collapsible **"Thinking"** block above each answer (works with reasoning models such as
  `qwen3` / `deepseek-r1`); its depth is set by an *Effort* slider in Settings.
- **Attachments** — the composer's 📎 menu attaches images (for vision models) or documents; text is
  extracted from PDF, DOCX, ODT and plain-text/code files and folded into the prompt.
- **Model Config** — a hardware-aware recommender (Settings → AI Model) that scans your CPU/RAM and
  GPU/VRAM and ranks Ollama models by fit, with inline download/remove.

Everything model-related runs on your machine. The only network traffic is the web searches and page
fetches used by the Web Search and Deep Research modes.

Built with [Avalonia UI](https://avaloniaui.net) on .NET 9, so the same code builds and runs on both
**Windows** and **Linux**. The UI is a flat "IDE" theme (light/dark, configurable accent and font).

## Prerequisites

1. **.NET 9 SDK** — https://dotnet.microsoft.com/download
2. **Ollama**, running locally — https://ollama.com
   ```bash
   ollama serve            # starts the local server on http://localhost:11434
   ollama pull llama3      # pull at least one model
   ```
   For **Project** mode, pull a tool-calling-capable model (e.g. `llama3.1`, `qwen2.5`, `mistral-nemo`).
   For the **Thinking** block to populate, use a reasoning model (e.g. `qwen3`, `deepseek-r1`).

## Run from source

```bash
dotnet run --project src/AI_Interface
```

The app connects to `http://localhost:11434` by default and loads your installed models into the
**model picker at the top-right**. To point it at a different Ollama instance, open
**Settings → AI Model → Local AI**, set the server URL (or use *Quick setup*), and click **Connect**
(or *Test connection* to just probe it).

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

- **Pick a model** from the dropdown at the top-right; the dot beside it shows connection status.
- **New Chat / Project** buttons are in the sidebar. Creating or opening a project enters Project mode;
  its chats are saved under `<project>/.AI/chats`, and any skill files in the project are loaded into
  the agent's system prompt.
- **Project agent safety** (Settings → Project): an approval mode (auto-run / confirm destructive /
  confirm everything) plus a three-way **software-install** permission (no permission / ask every time /
  allow). File operations are sandboxed to the project directory.
- **Settings** (⚙, top-right) has a left category rail: *AI Model*, *Theme* (appearance, accent + bubble
  colors, font + size), *General* (research depth, Thinking effort), *Project*, and *Web Search*.

## Settings

User settings (Ollama URL, last model, theme, search depth, agent permissions, …) are stored as JSON at:

- **Windows:** `%APPDATA%\AI_Interface\settings.json`
- **Linux:** `~/.config/AI_Interface/settings.json`

## Project layout

```
src/AI_Interface/
  Models/        Plain data types (chat, Ollama DTOs, agent tools, model catalog, settings)
  Services/      Ollama client, web search, deep-research + project-agent orchestrators,
                 hardware scan, attachments, theme + settings stores
  ViewModels/    MVVM view models (CommunityToolkit.Mvvm)
  Views/         Avalonia XAML windows
build/           Publish scripts for Windows and Linux
```

See [CLAUDE.md](CLAUDE.md) for architecture details.
