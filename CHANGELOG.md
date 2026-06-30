# Changelog

All notable user-facing changes to **Carnelian** (formerly "AI Interface").
Versioning is informal; entries are grouped as Added / Changed / Fixed.

## [Unreleased]

### Added
- **Save a reply to a file.** A per-message **💾** button (any non-user message) opens a menu to save
  it as **PDF**, **Word (.docx)**, or a plain **Text File** — alongside the existing `/saveToDoc` command.
- **Test connection for Web Search.** Settings → Web Search gets a *Test connection* button that probes
  the selected provider and shows a clear ok/error status, mirroring the existing Local AI test.
- **Search the Ollama model library.** The LLM Browser (Model Config) gets a **Search** box to query
  Ollama's online library directly (name, description, pulls, tags, last-updated, sizes, capabilities),
  not just the curated hardware-fit list.
- **Line spacing.** Settings → Appearance → Typography gets a line-spacing slider (tight to spacious).

### Fixed
- **Voice no longer reads Markdown symbols aloud.** Read Aloud (🔈 / Auto-read) previously spoke raw
  `**bold**`/`#`/`` ` `` characters; replies are now stripped to clean text first. Bold text also gets a
  brief comma pause around it (`Hello **world** there` → "Hello, world, there") so emphasis sounds
  natural instead of running straight through.

## [1.0.4] — 2026-06-18 — "Chat that does things"

Headline: local Chat can now reach the web and create documents on its own, a new
`/saveToDoc` command, and a tidier composer.

### Added
- **Web search in Chat.** A composer **🌐** toggle (local Chat only) lets the model decide
  when it needs current or unknown information, search the web on its own, and answer from the
  results — with the sources listed under the reply. Off by default, so plain chat stays fast.
  (Web Search mode is unchanged.)
- **The AI can create documents.** A composer **📄** toggle (local Chat only) lets you ask for a
  file in plain language — *"summarize this in a PDF"*, *"write a resume into a docx"* — and the
  model writes the content; the app shows a **Save dialog** (defaults to your Documents folder)
  and opens the finished PDF/Word file. Renders headings, lists, tables, links and RTL just like
  the export elsewhere.
- **`/saveToDoc` command.** A new slash-command that saves the **last reply** to a document: pick
  **PDF** or **Word** in a popup, then choose where to save it.

### Changed
- **Tidier composer toggles.** The **Thinking 🧠** and **Web 🌐** controls are now compact sliding
  switches (icon only, no text), so the composer toolbar is cleaner.

## [1.0.3] — 2026-06-16 — "Deep Search & Save"

Headline: richer document export (tables, hyperlinks, RTL/non-English), a one-click
local SearXNG, a more resilient Deep Research, and a clipboard that pastes nicely.

### Added
- **Save a research report as a document.** A **Save document ▾** dropdown next to the
  Deep Research sources offers **Save to PDF** / **Save to DOCX** → `~/Documents/research/`,
  opened automatically after saving.
- **Markdown tables.** Tables render as a real bordered grid in the transcript (theme-aware,
  readable in dark mode) and as real tables in PDF/Word export. A per-table **📋 Copy table**
  button copies a *real* table to the clipboard — HTML for Word/Google Docs/LibreOffice, TSV
  for Excel/Sheets.
- **One-click local SearXNG.** Settings → Web Search → **Install** / **Remove** buttons set up
  (or tear down) a local SearXNG search instance via Docker, with the JSON API pre-configured.
- **Right-to-left language support.** Hebrew/Arabic/… text is laid out right-to-left in the
  composer and replies, and exports correctly: PDF embeds a Unicode fallback font (so non-Latin
  scripts render instead of boxes) and right-aligns RTL content; DOCX marks paragraphs/tables RTL.

### Changed
- **Copy gives clean text.** The message **Copy** button now copies plain text with the markdown
  symbols removed (`**`, `` ` ``, `#`, …), matching the document export — instead of raw markup.
- **Document export polish.** PDF/Word now render `[label](url)` and bare URLs as clickable
  hyperlinks and strip leftover formatting symbols (`#` headings, `[n]` citation markers).

### Fixed
- **Deep Research no longer fails mid-search.** A single failing or timed-out search query / page
  fetch is now skipped (best-effort) instead of aborting the whole research run.
- **Chat-log delete button.** The list's scrollbar no longer overlaps the ✕ that deletes a saved chat.

## [1.0.2] — 2026-06-13

Everything new since the **1.0.0** build. Headline: the app was renamed to
**Carnelian**, gained the Mistral provider and document-generation tools, a
resizable UI, richer saved-chat history, and Linux packaging.

### Added
- **Mistral AI provider.** Mistral joins the cloud providers (alongside OpenAI,
  Google Gemini, Anthropic, DeepSeek and Nvidia). Add a key in Settings →
  AI Model → Web Models and its models appear in the picker.
- **Office-document generation (Project agent).** The project agent can now create
  and edit **Word `.docx`** (create / append / find-and-replace) and create
  **PDF** files from light-markdown content — confined to the project directory
  like the other file tools.
- **Resizable sidebar.** Drag the divider between the sidebar and the chat to set
  its width (clamped 220–600 px).
- **File-tree right-click actions.** In a project's *Files* tab, right-click a
  file for **Attach to prompt** (stage it as a composer attachment) and
  **Delete** (with a confirmation, since it's permanent).
- **App version in the UI.** Shown next to the app name in the sidebar and in the
  bottom-right corner of the startup launcher.
- **Startup launcher.** The window now opens on a launcher — **Local Chat**,
  recent projects, or *Open a project…* — and switches to the chat UI in place.
- **Named-phase plans + a phase gate.** The agent can organise complex work into
  named phases; a new *Phases* setting auto-advances them, or pauses at each phase
  boundary for a Continue/Stop confirmation when turned off.
- **`create_agent` tool.** The project agent can author project-scoped specialist
  agents (saved under `.AI/agents/`) and immediately delegate to them.
- **`ask_user` clarification tool.** A tabbed multi-question popup lets the agent
  ask 1–3 short clarifying questions when a request is vague.
- **Sidebar Tools menu.** Direct access to the **LLM Browser** (hardware-aware
  model recommender) and **Voice browser**, each gated by its prerequisite with an
  inline Download & Install button.
- **Full Markdown rendering in the transcript.** Headings, bullet/numbered lists,
  fenced code blocks (with copy), dividers, and inline `**bold**` / `*italic*` /
  `` `code` `` / `~~strikethrough~~` / links and auto-linked URLs.
- **Linux packaging.** A `.deb` builder (`build/packaging/deb/build-deb.sh`) and a
  Flatpak manifest + builder (`build/flatpak/`) for the self-contained Linux build
  — no .NET install needed on the target machine.

### Changed
- **Renamed the app to "Carnelian"** throughout the UI (window title, sidebar,
  launcher) and the project itself (solution, projects, and the executable —
  `Carnelian.exe`). The C# namespace and the `%APPDATA%/AI_Interface` settings
  folder were **intentionally kept**, so existing settings, chats, agents and
  memory carry over untouched.
- **Web Models** reorganised around an **Add Provider** / **Active Providers**
  flow with per-provider billing and budget tracking; the *Model Config* tool was
  renamed **LLM Browser**.

### Fixed
- **Saved Project chats keep their plan and subagent work.** Reopening a
  Project-mode conversation now restores the **plan card**, the **activity feed**,
  and each **subagent (delegation) card** as shown live — previously the plan was
  lost entirely and the subagent output/actions were reduced to a flat text dump.

> _This span (since 1.0.0) is feature-focused — there were no other standalone
> bug-fix changes._

## [1.0.0] — 2026-06-10

First packaged build (as "AI Interface"): local + cloud chat, Web Search, Deep
Research, the tool-using Project agent with a Lead orchestrator, MCP server
support, selectable agent personas, persistent memory, and Piper text-to-speech.
