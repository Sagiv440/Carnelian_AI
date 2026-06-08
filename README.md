
<h1>
  <img src="https://raw.githubusercontent.com/Sagiv440/AI_Interface/refs/heads/main/src/AI_Interface/Assets/avalonia-logo.ico?token=GHSAT0AAAAAAD7JXB7BMPR66M7L3L4SEO5O2RGWXHQ" alt="AI Interface logo" width="30" height="30" style="vertical-align: middle;">
  AI Interface
</h1>

A cross-platform desktop app for running AI **locally**. It talks to a local
[Ollama](https://ollama.com) server and gives you three ways to work:

- **Chat** — a normal conversation with the local model, streamed token-by-token.
- **Web Search** — runs one web search for your question and answers with inline citations.
- **Deep Research** — the model plans several search queries, the app searches the web and reads
  the top pages, then the model synthesizes a cited report.

Everything model-related runs on your machine. The only network traffic is the web searches and
page fetches used by the Web Search and Deep Research modes.

Built with [Avalonia UI](https://avaloniaui.net) on .NET 9, so the same code builds and runs on
both **Windows** and **Linux**.

## Prerequisites

1. **.NET 9 SDK** — https://dotnet.microsoft.com/download
2. **Ollama**, running locally — https://ollama.com
   ```bash
   ollama serve            # starts the local server on http://localhost:11434
   ollama pull llama3      # pull at least one model
   ```

## Run from source

```bash
dotnet run --project src/AI_Interface
```

The app connects to `http://localhost:11434` by default. Change the URL in the top bar and click
**Connect** to point it at a different Ollama instance.

## Build a distributable app

Self-contained, single-file builds (no .NET install needed on the target machine):

```bash
# Windows  ->  publish/win-x64/AI_Interface.exe
pwsh ./build/publish-windows.ps1

# Linux    ->  publish/linux-x64/AI_Interface
./build/publish-linux.sh
```

Either script also cross-publishes from the other OS — the .NET SDK can target both RIDs.

## Settings

User settings (Ollama URL, last model, search depth) are stored as JSON at:

- **Windows:** `%APPDATA%\AI_Interface\settings.json`
- **Linux:** `~/.config/AI_Interface/settings.json`

## Project layout

```
src/AI_Interface/
  Models/        Plain data types (chat, Ollama DTOs, search results, settings)
  Services/      Ollama client, web search, deep-research orchestrator, settings store
  ViewModels/    MVVM view models (CommunityToolkit.Mvvm)
  Views/         Avalonia XAML windows
build/           Publish scripts for Windows and Linux
```

See [CLAUDE.md](CLAUDE.md) for architecture details.
