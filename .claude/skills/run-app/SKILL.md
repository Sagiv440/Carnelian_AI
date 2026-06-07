---
name: run-app
description: Build and launch the AI Interface desktop app locally. Use when asked to run, start, launch, or manually test the app. Ensures Ollama is running first, then starts the Avalonia GUI.
---

# Run the AI Interface app

This is an Avalonia **GUI** desktop app that depends on a local Ollama server. Launching it opens a
window and the process stays in the foreground, so run it in the background when driving it from a tool.

## 1. Make sure Ollama is up

The app talks to Ollama at `http://localhost:11434` by default (configurable in the app's top bar,
persisted in `settings.json`). Check it responds:

```bash
curl -s http://localhost:11434/api/tags
```

If that fails, Ollama isn't running. Tell the user to start it and pull a model — the app can't do
anything useful without at least one installed model:

```bash
ollama serve            # start the server
ollama pull llama3      # install a model
```

## 2. Launch the app

```bash
dotnet run --project src/AI_Interface
```

Run it in the background (it blocks while the window is open). On first launch the app auto-connects,
loads the installed models into the top-bar picker, and is ready for Chat / Web Search / Deep Research.

## Notes

- A clean build is expected (`dotnet build AI_Interface.sln` → 0 warnings/errors). If the build fails
  with a net10.0 targeting error, the csproj's `TargetFramework` was reverted — it must stay `net9.0`.
- Web Search and Deep Research additionally require internet access (DuckDuckGo + page fetches).
