# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A cross-platform (Windows + Linux) Avalonia desktop app that runs AI locally through a local
[Ollama](https://ollama.com) server. It has three operating modes — Chat, Web Search, and Deep
Research — selectable in the top bar. .NET 9, MVVM via CommunityToolkit.Mvvm.

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
(`ollama serve`) with at least one model pulled (`ollama pull llama3`).

## Project skills (`.claude/skills/`)

- **run-app** — launch the app (checks Ollama, then `dotnet run`).
- **publish-app** — build self-contained Windows/Linux distributables.
- **add-mode** — add a new prompt-handling mode end-to-end (the exact files to touch and the
  threading rules). Read this before adding a fourth mode.

## Architecture (the parts worth knowing before editing)

**Layering.** `Models` (plain DTOs / domain types) → `Services` (all I/O and orchestration, behind
interfaces) → `ViewModels` (MVVM, no Avalonia UI types) → `Views` (XAML + thin code-behind).
Services are registered in `App.ConfigureServices()` and constructor-injected; `MainWindowViewModel`
is the only view model resolved from the container (set as the window's `DataContext`).

**The three modes** all live in `MainWindowViewModel.SendAsync`, which switches on `AppMode`:
- *Chat* streams `IOllamaClient.ChatStreamAsync` directly, with full conversation history rebuilt
  from `Messages` each send.
- *Web Search* does one `IWebSearchService.SearchAsync`, injects the snippets as context, then streams.
- *Deep Research* delegates to `IDeepResearchService.RunAsync`, which **plans queries with the model →
  searches each → reads top pages → synthesizes a cited answer**. It reports progress via
  `IProgress<string>` and streams the answer via an `Action<string>` callback.

**Ollama integration** (`OllamaClient`): the Ollama HTTP API streams NDJSON — one JSON object per
line from `POST /api/chat`. `ChatStreamAsync` reads the response with `HttpCompletionOption.
ResponseHeadersRead` and yields each line's content delta. Because of `ResponseHeadersRead`, the
`HttpClient.Timeout` (set to 10 min in DI) only bounds time-to-first-byte, not total generation
time. The base URL is read from `ISettingsService` on **every** call, so changing it in the UI takes
effect without a restart.

**Threading model — important.** Avalonia UI may only be touched on the UI thread.
- In the view model's own `await foreach` streaming loops, do **not** use `ConfigureAwait(false)` —
  continuations must resume on the UI thread so `MessageViewModel.Append` is safe.
- `DeepResearchService` runs on background threads (it uses `ConfigureAwait(false)` internally), so
  its answer-delta callback is marshalled back with `Dispatcher.UIThread.Post` in the VM.
- `IProgress<string>` (built on the UI thread) auto-marshals its callbacks, so status updates are safe.
- Scroll-to-bottom is signalled from the VM via the `ScrollToEndRequested` event and handled in
  `MainWindow.axaml.cs` (the VM never references controls).

**Web scraping** (`WebSearchService`): uses the keyless DuckDuckGo HTML endpoint and HtmlAgilityPack.
DuckDuckGo wraps result links as `//duckduckgo.com/l/?uddg=<encoded-target>` — `NormalizeUrl`
unwraps these. The injected `HttpClient` has a desktop User-Agent (set in DI) so pages serve normal
markup. Page text extraction strips script/style/nav/etc. and collapses whitespace.

**Settings** (`SettingsService`): JSON file under the per-user app-data folder
(`%APPDATA%\AI_Interface` / `~/.config/AI_Interface`). All reads/writes are best-effort and never throw.

**Theming & design system** (`ThemeService` + `SettingsWindow` + `Styles/ControlStyles.axaml`): the
app's visual language is adapted from sagiv440.github.io/sagiv-reuben — Poppins type (embedded in
`Assets/Fonts`), a teal→violet signature gradient, purple accent, deep-navy dark base, glassy rounded
surfaces. **Before doing any UI work, read the `app-style` skill** — it documents the tokens, palette,
and style classes to reuse instead of hard-coding colors.
- Design tokens live in `App.axaml`: shared brushes (`AppAccentBrush`, `AccentGradientBrush`,
  `UserBubbleBrush`, `AssistantBubbleBrush`, `AppFont`) plus **theme-variant-aware** structural tokens
  in `ResourceDictionary.ThemeDictionaries` (Dark/Light): `AppWindowBackground`, `AppSurfaceBrush`,
  `AppSurfaceBorderBrush`, `AppInputBackground`, `AppTextPrimary`, `AppTextSecondary`. Reference all via
  `{DynamicResource ...}` so they swap with night mode.
- Reusable style classes are in `Styles/ControlStyles.axaml`: `Button.cta` (gradient primary),
  `Button.ghost`, `Border.card`, `TextBlock.brand`, `TextBlock.muted`.
- Light/dark mode and custom colors (accent, user/assistant bubbles) are user-editable in Settings →
  Theme. Night mode is `ThemeMode.Dark` via `Application.RequestedThemeVariant` (default is now Dark).
  Custom colors override the themeable brush keys at runtime. Defaults + preset swatch palette live in
  `Models/ThemeDefaults.cs`.
- `ThemeService.Apply` runs once at startup and again on every change in `SettingsViewModel` (guarded by
  `_loading` so constructor field-init doesn't trigger it). It overrides the three themeable brush keys
  flat; structural tokens come from the ThemeDictionaries.

**Resolving view models from views.** Most views are data-templated, but the Settings dialog is opened
imperatively: `App.Services` (a static `IServiceProvider`) lets `MainWindow` code-behind resolve a
`SettingsViewModel` when the VM raises `SettingsRequested`. Use this pattern (VM event → code-behind
opens window) for any new dialog rather than newing up windows or services in the VM.

## Conventions specific to this project

- **Compiled bindings are on by default** (`AvaloniaUseCompiledBindingsByDefault`). XAML needs
  `x:DataType` on the relevant scope. Where a binding can't be compiled (the Sources list reaches the
  window's `OpenUrlCommand` via `RelativeSource AncestorType=Window`), that one `DataTemplate` sets
  `x:CompileBindings="False"`.
- **Message bubble styling** uses Avalonia style classes toggled from data: `Classes.user="{Binding
  IsUser}"` plus `Border.bubble` / `Border.bubble.user` selectors. No value converters.
- **Design-time stubs** in `ViewModels/DesignTimeServices.cs` back `MainWindowViewModel`'s
  parameterless constructor so the XAML previewer's `Design.DataContext` works. They never run at
  runtime. If you add a service dependency to `MainWindowViewModel`, add a matching design stub.
- The target framework is **net9.0** — the Avalonia template defaults to net10.0, which this SDK
  can't build, so don't let it revert.
