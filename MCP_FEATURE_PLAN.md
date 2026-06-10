# MCP Support — Implementation Plan

> Goal: let the app's models call **external services** through the **Model Context Protocol (MCP)** —
> connect to MCP servers, discover their tools, advertise those tools to the model, and route the model's
> tool calls to the right server. MCP tools become "just more tools" in the existing agent loop.

Status: **Phases 1–3 IMPLEMENTED — feature complete.**

**Post-plan enhancements:** the Settings panel now shows a server's **discovered tools** (name + description)
after **Test connection** (`McpConnection.ListToolSummariesAsync` → `McpProbe.Tools` → `McpViewModel.DiscoveredTools`),
and per-project `.AI/mcp.json` became **writable with a per-server scope**: each server is **Global** (saved to
`AppSettings.McpServers`) or **Local** (saved to the project's `.AI/mcp.json`, so it travels with the repo and
loads on open), chosen via a **Scope** toggle in the editor (Local is the default when a project is open).
`McpConfigStore` gained `Serialize`/`Save`; `McpServerConfig.IsProjectScoped` is a transient `[JsonIgnore]`
marker and `McpViewModel.SaveAll` rewrites both stores from the in-memory list. A "don't commit secrets" note
shows for Local servers (env/headers can carry tokens). Round-tripped by `Serialize_RoundTripsThroughParse`.

**Phase 3 — what shipped:** MCP **resources** (browse & attach as context) — `IMcpService.ListResourcesAsync`/
`ReadResourceAsync`, the `McpResourceBrowserWindow`/`McpResourceBrowserViewModel` dialog (composer 📎 →
"From MCP server…"), staged as composer chips and folded into the prompt's `[Attached documents]` channel;
MCP **prompts as slash-commands** — `ListPromptsAsync`/`GetPromptTextAsync`, discovered per project into the
composer's `/` palette (`RefreshMcpPromptCommandsAsync`), picking one drops the expanded text into the
composer; **richer content blocks** (shared `FlattenBlocks` keeps text + embedded text resources, notes
links, placeholders image/audio). **Deliberately out of scope (documented future work):** MCP tools in **Chat**
mode (needs a Chat tool loop) and **OAuth** for remote servers (static header/bearer auth already covers the
common case); arg-taking prompts (no-arg prompts only).

**Phase 2 — what shipped:** **HTTP transport** for remote servers (`McpConnection.BuildHttpTransport` →
`HttpClientTransport`, Endpoint + headers, Streamable-HTTP/SSE auto-detect); **per-project `.AI/mcp.json`**
in Claude Code's shape (`Services/McpConfigStore.cs`, pure `Parse` + best-effort `Load`, merged by
`McpService.EnabledServers(projectDir)` with project overriding global by id); **richer tool-result content**
(embedded text resources included, resource links noted); the Settings editor gained a **Transport** selector
that swaps Command/Args/Env ⇆ URL/Headers; unit tests `McpConfigStoreTests`. **Deferred to Phase 3:** the MCP
**resource browser** (browse + attach a server's resources as prompt context) and image/audio result blocks —
these are a self-contained UI feature, kept separate so Phase 2 stayed a clean "more transports" slice.

**Phase 1 — what shipped:** `ModelContextProtocol.Core` SDK (stdio); `Models/McpServerConfig.cs`;
`AppSettings.McpServers`; `AgentToolGroup.Mcp` + `AgentTools.Mcp`; the pure `Services/McpToolName.cs`
namespacing helper; `Services/IMcpService.cs` + `McpService.cs` + `McpConnection.cs` (connect / list / route /
test / disconnect, connection cache reconciled per turn, child-process kill on dispose); merged into
`ProjectAgentService` (advertise + route + gate + approve + 🔌 icon) and inherited by the orchestrator via
`CapTools`; Settings **MCP Servers** panel (`McpViewModel`, add/edit/remove/test) + an Agents **"MCP tools"**
checkbox; `MainWindow.OnClosed` tears down connections; unit tests `McpToolNameTests` + `McpToolGroupTests`.
The decision gate passed: the SDK restores, builds, and **single-file-publishes** clean on net9.0.

---

## 1. What MCP is (and why it fits this app well)

**MCP** is an open standard (Anthropic) for connecting an LLM app to external tools and data. The app is the
**host**; it runs one **client** per configured **server**; each server exposes:

- **Tools** — model-callable functions (the priority for us).
- **Resources** — readable data/context (files, rows, docs). *(Phase 2)*
- **Prompts** — reusable prompt templates. *(Phase 3)*

**Transports:**
- **stdio** — the host launches the server as a **child process** and speaks JSON-RPC over stdin/stdout.
  This is how most local servers run (e.g. `npx -y @modelcontextprotocol/server-filesystem`,
  `uvx mcp-server-git`, `docker run …`). **Phase 1 target.**
- **Streamable HTTP / SSE** — remote servers over HTTP(S), optionally with auth headers / OAuth. **Phase 2.**

**Why it fits:** the app already has a clean, provider-agnostic tool seam:
- `AgentTool(Name, Description, JsonElement Parameters)`, `AgentToolCall(Name, JsonElement Arguments)`,
  `AgentTurn` — `Models/AgentModels.cs`.
- A tool-use loop — `ProjectAgentService.RunAsync` → `BuildTools` advertises tools, `ExecuteAsync` switches on
  the tool name to run them, results feed back as `ChatRole.Tool` messages.
- Wire-mapping already passes a tool's JSON-Schema `Parameters` straight through to every provider
  (`OllamaClient.ChatWithToolsAsync` → `OllamaTool.Function.Parameters`; the cloud clients do the same).

So MCP integration is mostly: **(a)** connect + list tools, **(b)** map MCP tool → `AgentTool`, **(c)** when the
model calls a namespaced MCP tool, route it to the server instead of the local `switch`. **No change to the
provider clients or the wire format is required** — MCP tools ride the existing `AgentTool` rail.

---

## 2. Where MCP tools plug in (the seam)

```
MainWindowViewModel.RunProjectAgentAsync
        │  (resolves model client, persona, approval, etc.)
        ▼
IProjectAgentService.RunAsync / IAgentOrchestrator.RunAsync   ← inject IMcpService
        │
        ├─ BuildTools(...)  ──────────────►  built-in tools  +  await _mcp.ListToolsAsync(project)   ← NEW
        │                                     (namespaced: mcp__<server>__<tool>)
        ▼
   loop: client.ChatWithToolsAsync(model, messages, tools)
        │
        ▼
   ExecuteAsync(call):
        switch(call.Name) { built-ins … }
        default → if McpToolName.IsMcp(call.Name)  ──►  _mcp.CallToolAsync(call.Name, call.Arguments)  ← NEW
```

**Scope decision:** Phase 1 wires MCP into **Project (agent) mode only** — that's where the tool-calling loop
lives. Chat mode is streaming-only today; giving Chat a tool loop is a larger change → Phase 3.

---

## 3. SDK vs hand-rolled (recommendation)

**Recommended: use the official `ModelContextProtocol` C# SDK** (Anthropic + Microsoft, preview on NuGet).
It provides stdio + HTTP/SSE client transports, the handshake/capability negotiation, `ListToolsAsync`,
`CallToolAsync`, and exposes each tool's JSON schema — so we don't re-implement JSON-RPC framing, init
handshake, or content-block parsing. It pulls in `Microsoft.Extensions.AI.Abstractions`. The app already uses
NuGet (Avalonia, CommunityToolkit.Mvvm, HtmlAgilityPack, PdfPig), so a dependency is consistent. **Verify it
targets net9.0** (it supports net8.0/net9.0) and that it builds clean against this SDK.

**Fallback: hand-roll a minimal stdio JSON-RPC client** (one service file, zero NuGet — the same ethos as the
Voice engine). Implements only what we need: spawn process, `initialize`, `tools/list`, `tools/call`, line-
delimited JSON over stdin/stdout. More code + maintenance, but full control and no extra deps. Keep this as
the contingency if the SDK fights net9.0 or the single-file publish.

Either way the **rest of the plan is identical** — both sit behind `IMcpService`.

---

## 4. Data model

`Models/McpServerConfig.cs` (new):

```
enum McpTransport { Stdio, Http }

sealed class McpServerConfig
{
    string  Id            // stable slug (used in the mcp__id__tool namespace)
    string  Name          // display name
    bool    Enabled = true
    McpTransport Transport = Stdio

    // stdio
    string  Command       // e.g. "npx", "uvx", "docker"
    List<string> Args     // e.g. ["-y","@modelcontextprotocol/server-filesystem","C:\\data"]
    Dictionary<string,string> Env   // extra env vars (may carry secrets)

    // http (Phase 2)
    string  Url
    Dictionary<string,string> Headers   // e.g. Authorization

    // trust
    bool    AutoApprove = false   // skip the per-call approval prompt for THIS server (trusted)
}
```

- Add `Mcp` to **`AgentToolGroup`** and a `bool Mcp` flag (default true under `AllowAll`) to **`AgentTools`**
  (`Models/AgentTools.cs`) so MCP can be gated per-agent and survive the `CapTools` intersection. Update
  `Allows`, `Restrict`, and the orchestrator's `CapTools`.
- `Models/McpToolInfo.cs` (optional) — discovered tool metadata (server id, raw name, namespaced name,
  description, schema) for the Settings UI's "N tools" display and the activity feed.

**Namespacing (important):** providers restrict tool names to `^[A-Za-z0-9_-]{1,64}$` (OpenAI; Gemini/Anthropic
similar). Use Claude Code's convention **`mcp__<server>__<tool>`**, but sanitize both parts to `[A-Za-z0-9_-]`
and **length-guard** the whole name to ≤64 chars (truncate + short hash suffix on overflow). A pure helper
`McpToolName.Make(serverId, toolName)` / `TryParse(name) → (serverId, toolName)` / `IsMcp(name)` — **unit-tested**.

---

## 5. Services

`Services/IMcpService.cs` + `Services/McpService.cs` (new) — the connection manager (DI **singleton**, holds
live connections):

```
interface IMcpService
{
    // Aggregated, namespaced tools across all enabled servers (global + the active project's, if any).
    Task<IReadOnlyList<AgentTool>> ListToolsAsync(string? projectDir, CancellationToken ct);

    // Route a namespaced call to its server; returns the tool's text result (content blocks flattened).
    Task<string> CallToolAsync(string toolName, JsonElement args, CancellationToken ct);

    // Settings "Test" button: connect, handshake, list tools, report status + count (then keep/cache).
    Task<McpProbe> TestAsync(McpServerConfig server, CancellationToken ct);

    Task DisconnectAllAsync();   // app exit / settings change / project exit
}
record McpProbe(bool Ok, int ToolCount, string Message);
```

Responsibilities:
- **Config source:** global servers from `ISettingsService` (`AppSettings.McpServers`) **+** optional per-project
  `<project>/.AI/mcp.json` (Claude-Code-compatible shape, so a project can ship its own servers). A small
  `McpConfigStore` loads/merges (project overrides global by id), mirroring how memory/agents are scoped.
- **Connection cache:** one `McpConnection` per server id, started lazily on first `ListToolsAsync`/`CallToolAsync`,
  reused across turns. Map namespaced name → (connection, raw tool) for routing.
- **Lifecycle:** child processes started with `CreateNoWindow`, env applied; killed with
  `entireProcessTree: true` (reuse `ProjectAgentService.TryKill`'s pattern) on dispose/disconnect. Connect/
  handshake timeouts; a crashed/unreachable server contributes **zero** tools (best-effort, like a failing
  provider in `ChatRouter`) and surfaces its error in Settings rather than throwing into the agent loop.
- **Result mapping:** MCP returns content blocks (text / image / embedded resource). Phase 1 flattens **text**
  blocks to the string the loop already expects; non-text blocks summarized as `[image]`/`[resource]`
  placeholders (real image/resource handling → Phase 2). Cap to `MaxResultChars` (6000), same as built-ins.
- **Threading/async:** all I/O `ConfigureAwait(false)`; the service never touches UI. The VM already marshals
  activity/approval callbacks.

---

## 6. Wiring into the agent loop

`ProjectAgentService` (and `AgentOrchestrator`):
- **Inject `IMcpService`** via the constructor (next to `_search`). Add a matching **design-time stub** in
  `ViewModels/DesignTimeServices.cs`.
- `RunAsync`: after building the built-in tool list, `var mcpTools = await _mcp.ListToolsAsync(project.Directory, ct)`
  and append them **only if** `allowedTools.Allows(AgentToolGroup.Mcp)`. (Keep `BuildTools` pure; merge the MCP
  list in `RunAsync` so the static builder stays testable.)
- `ExecuteAsync`: the `switch` default branch checks `McpToolName.IsMcp(call.Name)` → `_mcp.CallToolAsync(...)`.
  Gate it like any tool: `ToolGroupOf` maps an `mcp__…` name to `AgentToolGroup.Mcp`; `Describe` →
  `("Call <server>", "<tool>", destructive: true)`; `IconFor` → `🔌` (or `🧩`); approval-gated by default
  (MCP touches the outside world) unless the server is `AutoApprove`. `IsFailure` already catches `Error:`.
- **Orchestrator:** `CapTools(lead, specialist)` must intersect the new `Mcp` flag; the lead's tool ceiling
  decides whether its team may use MCP. `BuildLeadTools` can offer MCP tools to the lead's own loop too.
- `MainWindowViewModel.RunProjectAgentAsync`: no signature churn — MCP rides in via the injected service. (It
  already passes `ActiveProject` so the service can find the per-project `mcp.json`.)

**Activity feed / status bar:** add the MCP icon to `IconFor`; `CurrentActionLabel` and the structured
`ActivityUpdate` rows work unchanged. A delegated specialist's MCP calls already flow into its delegation card.

---

## 7. Settings + UI

- **`AppSettings`**: add `List<McpServerConfig> McpServers { get; set; } = new();` and optionally a master
  `bool McpEnabled` (a global off switch). Persisted by the existing best-effort JSON `SettingsService`.
- **New Settings category "MCP Servers"** under **AI FEATURES** (sibling of Agents/Voice):
  - `SettingsCategory` enum + per-category `IsVisible` bool + `SelectCategoryCommand` entry, and a nav
    `Button.nav` + content `Panel` in `SettingsWindow.axaml` (the established grouped-nav pattern).
  - A **master/detail panel** modelled on the Agents panel (`AgentsViewModel`, nested as `SettingsViewModel`'s
    `McpPanel`): server list (name + enabled + "N tools" + status dot), **＋ Add / Edit / Remove**, and a
    **Test connection** button that calls `IMcpService.TestAsync` and shows ✅/🔴 + tool count + message.
  - The Add/Edit form fields: Name, Transport, Command, Args (one per line), Env (key=value lines), Enabled,
    Auto-approve. (HTTP fields Url/Headers appear when Transport = Http — Phase 2.)
  - `SettingsWindow` calls `McpPanel.Initialize(projectDir)` before opening (like `AgentsPanel.Initialize` /
    `InitializeMemory`) so per-project servers show; the main window reloads nothing extra (the agent reads
    live config each run).
- **Agents editor**: add an **"MCP tools"** checkbox to the per-agent Tool-permissions group (drives
  `AgentTools.Mcp`), so a custom agent can be allowed/denied external tools.
- **Approval window**: `ToolApprovalWindow` already renders `ToolName/Summary/Detail/IsDestructive`; an MCP
  call shows e.g. *Call `github` · `create_issue`* with the JSON args in `Detail`. No new view needed.

---

## 8. Security & safety (must-haves)

- **Approval by default.** Every MCP call is approval-gated unless its server is explicitly `AutoApprove`
  (trusted). Honors the global `AgentApproval` mode + the same `approve` callback as built-in tools.
- **Per-agent gate.** `AgentTools.Mcp` lets the user deny external tools to a given agent; the orchestrator
  `CapTools` ceiling applies to the team.
- **Prompt-injection awareness.** Tool **descriptions and results come from third-party servers** and can try
  to steer the model. Document this; only connect to trusted servers; keep approval on for untrusted ones. (No
  silent auto-run of a freshly added server.)
- **Secrets.** `Env`/`Headers` may carry tokens. Stored in the same plaintext settings file as the existing API
  keys (consistent with the app today) — note it; consider a follow-up to redact them in the UI and logs.
- **Process hygiene.** Child servers are killed `entireProcessTree: true` on disconnect/exit; connect + call
  timeouts; a misbehaving server can't hang the loop (best-effort, contributes nothing).
- **Tool-count sanity.** Small local models handle few tools poorly. Per-server (and ideally per-tool) enable,
  plus a soft cap with a `log`/status note when tools are dropped, so dozens of MCP tools don't drown the model.

---

## 9. Testing (xUnit, I/O-free logic only — matches the repo's convention)

Process/socket I/O isn't unit-testable headlessly (same as Ollama/Piper). Cover the **pure** logic via
`InternalsVisibleTo`:
- `McpToolName.Make`/`TryParse`/`IsMcp` — round-trip, sanitization, 64-char length-guard + collision behavior.
- `McpServerConfig` (de)serialization + `McpConfigStore` global/project merge (project overrides by id).
- `AgentTools.Allows`/`Restrict` with the new `Mcp` group; `AgentOrchestrator.CapTools` intersecting `Mcp`.
- `ProjectAgentService.ToolGroupOf("mcp__x__y") == AgentToolGroup.Mcp`; `Describe`/`IconFor` for MCP names.
- Content-block → string flattening (text kept, non-text placeholdered, truncation).

Runtime verification is manual (needs a real server, e.g. `npx -y @modelcontextprotocol/server-everything`):
build → Settings → add server → Test (see tool count) → enter a project → ask the agent to use a server tool →
watch the approval prompt + activity row + result.

---

## 10. Phased delivery

**Phase 1 — stdio tools in Project mode (MVP).**
SDK (or hand-rolled) stdio client; `IMcpService` + config (global `AppSettings.McpServers`); namespacing helper;
merge tools into `ProjectAgentService` + orchestrator; route calls; approval + per-agent `Mcp` gate; Settings
"MCP Servers" panel with Add/Edit/Remove/Test + tool count; activity icon. Tests for the pure helpers.

**Phase 2 — remote servers + resources.**
HTTP/SSE transport (Url/Headers, basic auth); per-project `.AI/mcp.json`; MCP **resources** (browse + attach as
context, reusing the attachment/`[Attached…]` channel); image/resource result blocks; richer status UI.

**Phase 3 — prompts, Chat mode, OAuth.**
MCP **prompts** surfaced as slash-commands in the composer palette; MCP tools in **Chat** mode (give Chat a
bounded tool loop); OAuth for remote servers; resource subscriptions / change notifications.

---

## 11. File-by-file change list (Phase 1)

**New**
- `Models/McpServerConfig.cs` — config + `McpTransport`; `Models/McpToolInfo.cs` (optional).
- `Services/IMcpService.cs`, `Services/McpService.cs`, `Services/McpConfigStore.cs`,
  `Services/McpToolName.cs` (pure helper), `Services/McpConnection.cs` (per-server wrapper).
- `ViewModels/McpViewModel.cs` (master/detail panel) + an Add/Edit form (panel or small dialog).
- (If hand-rolled) `Services/StdioJsonRpcClient.cs`.

**Edit**
- `Models/AgentTools.cs` — add `Mcp` to `AgentToolGroup` + `bool Mcp`; update `Allows`/`Restrict`.
- `Models/AppSettings.cs` — `List<McpServerConfig> McpServers` (+ optional `McpEnabled`).
- `Services/ProjectAgentService.cs` — inject `IMcpService`; merge MCP tools in `RunAsync`; MCP branch in
  `ExecuteAsync`; `ToolGroupOf`/`Describe`/`IconFor` for `mcp__…`.
- `Services/AgentOrchestrator.cs` — inject `IMcpService`; `CapTools` includes `Mcp`; offer MCP to the lead.
- `ViewModels/SettingsViewModel.cs` — new `SettingsCategory.Mcp` + bool + `McpPanel` + `Initialize`.
- `ViewModels/AgentsViewModel.cs` — "MCP tools" permission checkbox.
- `ViewModels/DesignTimeServices.cs` — `DesignMcpService` stub; update VM design-time ctors.
- `Views/SettingsWindow.axaml(.cs)` — nav entry + MCP panel; `McpPanel.Initialize(projectDir)` on open.
- `App.axaml.cs` — `AddSingleton<IMcpService, McpService>()` (next to `IProjectAgentService`); typed
  `HttpClient` for the HTTP transport (Phase 2); register `McpViewModel`.
- `AI_Interface.csproj` — `<PackageReference Include="ModelContextProtocol" .../>` (if using the SDK).
- `tests/AI_Interface.Tests` — the pure-helper tests above.
- `CLAUDE.md`, `README.md`, `.claude/skills/add-mode` (note MCP tools join the agent toolset) — docs.

---

## 12. Open decisions (recommended defaults in **bold**)

1. **SDK vs hand-rolled** → **official `ModelContextProtocol` SDK** (fallback: zero-dep stdio client) — pending a
   net9.0 + single-file-publish build check.
2. **Transport priority** → **stdio first** (Phase 1), HTTP/SSE in Phase 2.
3. **Modes** → **Project mode first**; Chat mode in Phase 3.
4. **Config storage** → **global `AppSettings.McpServers` first**, per-project `.AI/mcp.json` in Phase 2.
5. **Default trust** → **approval-gated by default**, opt-in `AutoApprove` per server.
