---
name: add-mode
description: Add a new operating mode to the AI Interface app (alongside Chat, Web Search, Deep Research). Use when extending the app with a new way of handling a prompt, e.g. a RAG/document mode, a summarize mode, or an agent mode.
---

# Add a new mode

A "mode" is how a sent prompt is handled. The three existing modes (`Chat`, `WebSearch`,
`DeepResearch`) show the full pattern. Adding one touches these files, in order:

## 1. Declare the mode — `Models/AppMode.cs`
Add a value to the `AppMode` enum with an XML-doc comment describing the behaviour.

## 2. Register it in the picker — `ViewModels/MainWindowViewModel.cs`
In the constructor's `Modes` array, add a `new ModeOption(AppMode.Yours, "Label", "Tooltip description")`.
The mode selector ComboBox and its tooltip are driven entirely by this list.

## 3. Handle it — `MainWindowViewModel.SendAsync`
Add a `case AppMode.Yours:` to the `switch`, calling a private `RunYourModeAsync(...)` method modelled
on the existing `RunChatAsync` / `RunWebSearchAsync` / `RunDeepResearchAsync`.

## 4. (If it needs new I/O) add a service — `Services/`
Create `IYourService` + `YourService`, then register it in `App.ConfigureServices()`:
- plain logic → `services.AddTransient<IYourService, YourService>();`
- needs HTTP → `services.AddHttpClient<IYourService, YourService>(...)` (see the Ollama/web-search examples).
Inject it into `MainWindowViewModel`'s constructor **and** add a matching stub in
`ViewModels/DesignTimeServices.cs` (the parameterless ctor / XAML previewer depends on it, or the build
breaks).

## Threading rules (do not skip)
UI-bound state may only change on the UI thread.
- In the VM's own `await foreach` over `IOllamaClient.ChatStreamAsync`, **do not** add
  `ConfigureAwait(false)` — the loop body must resume on the UI thread to call `MessageViewModel.Append`.
- If work runs on a background thread (a service using `ConfigureAwait(false)` internally), marshal its
  callbacks with `Dispatcher.UIThread.Post(...)`, as `RunDeepResearchAsync` does.
- Report step-by-step progress with `IProgress<string>` constructed on the UI thread (auto-marshals) and
  set `StatusText` from it.
- Call `RequestScroll()` after appending streamed text so the transcript follows along.

## Verify
`dotnet build AI_Interface.sln` must stay at 0 warnings / 0 errors, then run the app and exercise the
new mode (see the `run-app` skill).
