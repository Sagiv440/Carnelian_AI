
<h1 style="font-weight: bold;">
  <img src="src/AI_Interface/Assets/app-logo.png" alt="AI Interface logo" width="40" height="40" align="center" />
  Carnelian
</h1>

<img src="src/AI_Interface/Assets/app-logo.png" alt="AI Interface logo" width="600" height="400" align="center" />

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

1. Download the latest [build](https://github.com/Sagiv440/Carnelian_AI/releases)
2. Install [Ollama](https://ollama.com) and start it: `ollama serve`
3. Pull a model: `ollama pull llama3` (the Project agent needs a tool-calling model,
   e.g. `ollama pull llama3.1`).
4. Run the app (see below). Cloud providers are optional — add keys in
   **Settings → AI Model → Web Models**.

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

## Acknowledgements

Carnelian builds on these excellent projects:

- [Ollama](https://ollama.com) — local model runtime
- [Avalonia](https://avaloniaui.net) — cross-platform .NET UI framework
- [Piper](https://github.com/rhasspy/piper) — local neural text-to-speech
- [Model Context Protocol](https://modelcontextprotocol.io) — external tool/resource standard

## License

Licensed under the [GNU General Public License v3.0](LICENSE).
