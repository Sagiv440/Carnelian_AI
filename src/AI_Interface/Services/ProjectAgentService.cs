using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IProjectAgentService"/>. Advertises a small set of file/folder/terminal tools to
/// the model, then loops: ask the model → run the tools it requests (gated by the approval mode and
/// confined to the project directory) → feed results back → repeat until the model answers in plain text.
/// </summary>
public sealed class ProjectAgentService : IProjectAgentService
{
    /// <summary>Fallback hard cap on tool-use rounds when a caller doesn't specify one (matches Guided).</summary>
    private const int DefaultMaxSteps = AutonomyMap.GuidedSteps;

    /// <summary>Output kept per tool result (both for the model and the transcript).</summary>
    private const int MaxResultChars = 6000;

    /// <summary>Wall-clock limit for a single terminal command.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private readonly IMemoryService _memory;
    private readonly IProjectDocsService _docs;
    private readonly IWebSearchService _search;
    private readonly IMcpService _mcp;
    private readonly IAgentService _agents;

    public ProjectAgentService(IMemoryService memory, IProjectDocsService docs, IWebSearchService search,
        IMcpService mcp, IAgentService agents)
    {
        _memory = memory;
        _docs = docs;
        _search = search;
        _mcp = mcp;
        _agents = agents;
    }

    // ---- limits for the search/find tools --------------------------------------------------

    /// <summary>Folders skipped by search_files / find_files (vcs, build output, deps, the app's own .AI).</summary>
    private static readonly string[] ScanExcludeDirs =
        { ".git", ".AI", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", ".next", "target" };

    /// <summary>Cap on matching lines returned by search_files (keeps the tool result bounded).</summary>
    private const int MaxSearchMatches = 100;

    /// <summary>Cap on paths returned by find_files.</summary>
    private const int MaxFindResults = 200;

    /// <summary>Files larger than this are skipped by search_files (likely data/binaries, not source).</summary>
    private const long MaxScanFileBytes = 1_000_000;

    /// <summary>
    /// Maintenance directive appended to the system prompt when the agent owns the handbook (top-level run).
    /// Mirrors how Claude Code keeps CLAUDE.md: durable rules go here, transient facts go to memory.
    /// </summary>
    private const string DocsDirective =
        "\n\nYou maintain this project's handbook (.AI/AI_DOCS.md) with the update_docs tool: update it when a " +
        "durable rule, convention, architecture fact, or command changes — keep it concise and accurate. " +
        "It is rules, not a log (use memory for transient facts).";

    /// <summary>
    /// The update_docs tool's model-facing description, encoding the CLAUDE.md-maintenance discipline. Shared
    /// with <see cref="AgentOrchestrator"/> so the lead and the single agent advertise an identical tool.
    /// </summary>
    internal const string UpdateDocsToolDescription =
        "Create or update this project's handbook at .AI/AI_DOCS.md — the authoritative 'how this project " +
        "works' guide you and other agents read every turn. Submit the COMPLETE new handbook in `content` " +
        "(it replaces the file). Rules (same discipline a developer uses for a CLAUDE.md): (1) Update only " +
        "when a DURABLE rule, convention, architecture fact, or build/run/test command changes — things a " +
        "future run must know. (2) It is RULES/orientation, NOT a log or scratchpad — transient notes and " +
        "learned facts go to memory (the remember tool), never here. (3) Keep it concise and accurate; a " +
        "stale or bloated handbook is worse than none. (4) You can see the current handbook in your context " +
        "— revise it surgically, preserving useful existing content, rather than discarding it. (5) Don't " +
        "update for trivial or one-off things.";

    /// <summary>The JSON schema for the update_docs tool's single required <c>content</c> argument. Shared with the orchestrator.</summary>
    internal static JsonElement UpdateDocsSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            content = new { type = "string", description = "The COMPLETE new handbook markdown (it replaces the file)." }
        },
        required = new[] { "content" }
    });

    /// <summary>
    /// The update_plan tool's model-facing description. Shared with <see cref="AgentOrchestrator"/> so the Lead
    /// and the single agent advertise an identical tool. Supports a flat <c>steps</c> checklist OR named
    /// <c>phases</c> (each with its own steps) for multi-stage work.
    /// </summary>
    internal const string UpdatePlanToolDescription =
        "Maintain a short, visible plan for a multi-step task. Call it to post your plan, then again as you " +
        "progress — always resending the FULL list, each item with a status ('pending', 'active' = working it " +
        "now, 'done'). For a complex task, organise the work into a few named PHASES (e.g. Explore, Implement, " +
        "Verify) via 'phases' — each phase with its own 'steps'; mark exactly one phase 'active' at a time and " +
        "advance it as you go. For a simple task a flat 'steps' list is fine. Send EITHER 'phases' OR 'steps'.";

    /// <summary>The JSON schema for the update_plan tool (a flat <c>steps</c> array or named <c>phases</c>). Shared with the orchestrator.</summary>
    internal static JsonElement UpdatePlanSchema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            steps = new
            {
                type = "array",
                description = "A flat ordered checklist (for a simple task). Resend the whole list every call.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string", description = "Short imperative description of the step." },
                        status = new { type = "string", description = "'pending', 'active' (working it now), or 'done'." }
                    },
                    required = new[] { "text" }
                }
            },
            phases = new
            {
                type = "array",
                description = "Named phases for a complex task (preferred). Resend the whole list every call.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Short phase name, e.g. \"Explore\"." },
                        status = new { type = "string", description = "'pending', 'active' (the phase you're in now), or 'done'." },
                        steps = new
                        {
                            type = "array",
                            description = "The checklist steps within this phase.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    text = new { type = "string", description = "Short imperative description of the step." },
                                    status = new { type = "string", description = "'pending', 'active', or 'done'." }
                                },
                                required = new[] { "text" }
                            }
                        }
                    },
                    required = new[] { "name" }
                }
            }
        }
    });

    public async Task RunAsync(
        IChatClient client,
        Project project,
        string model,
        IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode,
        int maxSteps,
        AgentTools allowedTools,
        string personaPrefix,
        string thinkingDirective,
        string projectSkills,
        SoftwareInstallPermission installPermission,
        bool memoryEnabled,
        bool allowDocsUpdate,
        IProgress<string> status,
        Action<string> onActivity,
        Action<ActivityUpdate>? onActivityStep,
        Action<PlanUpdate>? onPlan,
        Action<string> onAnswer,
        Func<ToolApprovalRequest, Task<bool>> approve,
        bool autoFlowPhases,
        Func<PhaseGate, Task<bool>>? phaseGate,
        CancellationToken ct)
    {
        // A null allow-list (e.g. an agent with no tool profile) is treated as unrestricted.
        allowedTools ??= new AgentTools();
        // The step budget is set by the active agent's autonomy level; fall back to the Guided default.
        if (maxSteps <= 0)
            maxSteps = DefaultMaxSteps;
        var tools = BuildTools(allowedTools, installPermission, memoryEnabled, allowDocsUpdate);

        // Append tools from configured MCP servers (external services) when this agent is allowed the MCP group.
        // Fetched once per run; best-effort — a server that fails to connect simply isn't offered this turn.
        if (allowedTools.Allows(AgentToolGroup.Mcp))
        {
            try
            {
                var mcpTools = await _mcp.ListToolsAsync(project.Directory, ct).ConfigureAwait(false);
                if (mcpTools.Count > 0)
                    tools = tools.Concat(mcpTools).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch { /* best-effort: MCP servers that fail to list just aren't advertised this run */ }
        }

        var messages = new List<ChatMessage>
        {
            // The active agent's persona sits on top of the service-owned sandbox prompt. The handbook
            // maintenance directive is added only when this is a top-level (main) run that owns update_docs.
            ChatMessage.System(personaPrefix + SystemPrompt(project, installPermission) +
                (allowDocsUpdate ? DocsDirective : "") + thinkingDirective + projectSkills)
        };
        messages.AddRange(conversation);

        // 0-based counter correlating each tool call's Started/Finished structured update (mirrors the
        // orchestrator's delegation index). Notes consume an index too so each lands in the feed in order.
        var activityIndex = 0;

        // Tracks the phase the model is currently in, to detect a move into a NEW phase across update_plan
        // calls — used by the phase gate (pause between phases when AutoFlowPhases is off).
        string? previousActivePhase = null;

        for (var step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            status.Report(step == 0 ? "Thinking…" : "Working…");

            var turn = await client.ChatWithToolsAsync(model, messages, tools, ct).ConfigureAwait(false);

            // No tool calls → the model gave its final answer.
            if (turn.ToolCalls.Count == 0)
            {
                onAnswer(string.IsNullOrWhiteSpace(turn.Content) ? "_(no response)_" : turn.Content);
                return;
            }

            // Record the assistant turn (with its tool calls) so the model sees its own request next round.
            messages.Add(new ChatMessage(ChatRole.Assistant, turn.Content) { ToolCalls = turn.ToolCalls });
            // Any text the model emits alongside a tool call is part of its "work", not the final answer.
            if (!string.IsNullOrWhiteSpace(turn.Content))
            {
                onActivity(turn.Content + "\n");
                // Structured feed: the interim narration as a lightweight "note" line.
                onActivityStep?.Invoke(new ActivityUpdate(
                    ActivityPhase.Note, activityIndex++, "", "", "", turn.Content.Trim(), false));
            }

            foreach (var call in turn.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                // Structured feed: emit a "Started" row before running the tool (derived from the same pure
                // Describe(...) the inline log uses). ExecuteAsync itself is unchanged — it still emits the
                // 🔧 strings via onActivity, which the VM hides for project runs once the structured feed exists.
                var (summary, detail, _) = Describe(project, call, GetString(call.Arguments, "path"));
                var idx = activityIndex++;
                onActivityStep?.Invoke(new ActivityUpdate(
                    ActivityPhase.Started, idx, IconFor(call.Name), summary, detail, "", false));

                var result = await ExecuteAsync(
                    project, call, approvalMode, allowedTools, installPermission, allowDocsUpdate,
                    status, onActivity, onPlan, approve, ct)
                    .ConfigureAwait(false);

                // Structured feed: resolve the row with this tool's result + success/failure status.
                onActivityStep?.Invoke(new ActivityUpdate(
                    ActivityPhase.Finished, idx, "", "", "", result, IsFailure(result)));

                messages.Add(new ChatMessage(ChatRole.Tool, result) { ToolName = call.Name });

                // Phase gate: when the model moves into a NEW phase via update_plan, optionally pause for the
                // user before it does that phase's work (the loop blocks here until the gate resolves).
                if (string.Equals(call.Name, "update_plan", StringComparison.Ordinal))
                {
                    var (prev, stop) = await ApplyPhaseGateAsync(
                        previousActivePhase, call.Arguments, autoFlowPhases, phaseGate).ConfigureAwait(false);
                    previousActivePhase = prev;
                    if (stop is not null)
                    {
                        onAnswer($"_(paused at the “{stop.NextPhase}” phase — send another message to continue.)_");
                        return;
                    }
                }
            }
        }

        onAnswer($"_(stopped after {maxSteps} steps — the task may be unfinished)_");
    }

    // ---- the agent loop's single-tool step -------------------------------------------------

    private async Task<string> ExecuteAsync(
        Project project, AgentToolCall call, AgentApprovalMode approvalMode,
        AgentTools allowedTools, SoftwareInstallPermission installPermission, bool allowDocsUpdate,
        IProgress<string> status, Action<string> onActivity, Action<PlanUpdate>? onPlan,
        Func<ToolApprovalRequest, Task<bool>> approve, CancellationToken ct)
    {
        var path = GetString(call.Arguments, "path");
        var (summary, detail, destructive) = Describe(project, call, path);

        onActivity($"\n🔧 {summary}{(string.IsNullOrEmpty(detail) ? "" : $"  `{detail}`")}\n");

        // Defense in depth: update_docs is offered only to the top-level (main) agent and isn't gated by the
        // AgentTools allow-list below, so refuse it here for a delegated specialist that calls it anyway.
        if (call.Name == "update_docs" && !allowDocsUpdate)
        {
            onActivity("   ⛔ blocked — only the main agent may update the project handbook\n");
            return "Only the main (top-level) agent may update the project handbook (.AI/" +
                   ProjectDocsService.FileName + "). A delegated specialist can't.";
        }

        // Defense in depth: even though disallowed tools aren't advertised, refuse one if the model calls
        // it anyway (e.g. it hallucinated a tool name) rather than silently running it.
        if (ToolGroupOf(call.Name) is { } group && !allowedTools.Allows(group))
        {
            onActivity($"   ⛔ blocked — this agent isn't allowed to {PermissionLabel(group)}\n");
            return $"This agent is not permitted to {PermissionLabel(group)}. The user can enable this in " +
                   "Settings → AI Features → Agents (Tool permissions) for a custom agent, then retry.";
        }

        // Is this a machine-wide software install (the install tool, or a run_command that looks like one)?
        var isInstallAction = call.Name == "install_software" ||
            (call.Name == "run_command" && LooksLikeSystemInstall(GetString(call.Arguments, "command") ?? ""));

        // Permission gate: when installs aren't permitted, refuse install actions outright.
        if (isInstallAction && installPermission == SoftwareInstallPermission.Never)
        {
            if (call.Name == "install_software")
            {
                onActivity("   ⛔ blocked — software installation is disabled for this project\n");
                return "Software installation is disabled. Ask the user to allow installs in " +
                       "Settings → Project (\"Ask every time\" or \"Allow agent to install software\"), then retry.";
            }
            onActivity("   ⛔ blocked — that looks like a system install (disabled for this project)\n");
            return "That command installs software machine-wide, which is disabled. Ask the user to allow " +
                   "installs in Settings → Project, then retry.";
        }

        // "Ask every time" forces confirmation for installs even when the approval mode wouldn't.
        var needsApproval = NeedsApproval(approvalMode, destructive) ||
            (isInstallAction && installPermission == SoftwareInstallPermission.Ask);

        // A trusted (auto-approve) MCP server bypasses the per-call prompt entirely, even under ConfirmEverything.
        if (McpToolName.IsMcp(call.Name) && _mcp.IsAutoApproved(call.Name))
            needsApproval = false;

        if (needsApproval)
        {
            var ok = await approve(new ToolApprovalRequest(call.Name, summary, detail, destructive))
                .ConfigureAwait(false);
            if (!ok)
            {
                onActivity("   ⛔ skipped (you declined this action)\n");
                return "The user declined to run this action.";
            }
        }

        // Live "current action" (Phase 3A): show the actual step (icon · summary · target) in the busy
        // status bar, so the user can see what's running without opening the activity feed.
        status.Report(CurrentActionLabel(call.Name, summary, detail));

        string result;
        try
        {
            // MCP tools (namespaced mcp__server__tool) are routed to their server via the MCP service; everything
            // else is a built-in tool handled by the switch below.
            result = McpToolName.IsMcp(call.Name)
                ? await _mcp.CallToolAsync(call.Name, call.Arguments, ct).ConfigureAwait(false)
                : call.Name switch
            {
                "list_directory" => ListDirectory(project, path),
                "read_file"      => ReadFile(project, path),
                "search_files"   => SearchFiles(project, GetString(call.Arguments, "pattern"), path,
                                        GetString(call.Arguments, "glob")),
                "find_files"     => FindFiles(project, GetString(call.Arguments, "glob"), path),
                "write_file"     => WriteFile(project, path, GetString(call.Arguments, "content") ?? ""),
                "edit_file"      => EditFile(project, path, GetString(call.Arguments, "find"),
                                        GetString(call.Arguments, "replace")),
                "move_file"      => MoveFile(project, GetString(call.Arguments, "source"),
                                        GetString(call.Arguments, "destination")),
                "copy_file"      => CopyFile(project, GetString(call.Arguments, "source"),
                                        GetString(call.Arguments, "destination")),
                "create_folder"  => CreateFolder(project, path),
                "delete_file"    => DeleteFile(project, path),
                "delete_folder"  => DeleteFolder(project, path),
                "web_search"     => await WebSearchAsync(GetString(call.Arguments, "query"),
                                        GetString(call.Arguments, "max"), ct).ConfigureAwait(false),
                "run_command"    => await RunCommandAsync(project, GetString(call.Arguments, "command") ?? "", ct)
                                        .ConfigureAwait(false),
                "install_software" => await RunCommandAsync(project, GetString(call.Arguments, "command") ?? "", ct)
                                        .ConfigureAwait(false),
                "update_plan"    => UpdatePlan(call.Arguments, onPlan),
                "remember"       => Remember(project, GetString(call.Arguments, "text"), GetString(call.Arguments, "scope")),
                "create_skill"   => CreateSkill(project, GetString(call.Arguments, "name"),
                                        GetString(call.Arguments, "content"), GetString(call.Arguments, "description")),
                "create_agent"   => CreateAgent(project, GetString(call.Arguments, "name"),
                                        GetString(call.Arguments, "persona"), GetString(call.Arguments, "description"),
                                        GetString(call.Arguments, "glyph"), GetString(call.Arguments, "tools")),
                "update_docs"    => UpdateDocs(project, GetString(call.Arguments, "content")),
                _ => $"Unknown tool '{call.Name}'."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = $"Error: {ex.Message}";
        }

        onActivity(IndentForDisplay(result) + "\n");
        return Truncate(result);
    }

    private static bool NeedsApproval(AgentApprovalMode mode, bool destructive) => mode switch
    {
        AgentApprovalMode.AutoRun => false,
        AgentApprovalMode.ConfirmDestructive => destructive,
        AgentApprovalMode.ConfirmEverything => true,
        _ => destructive
    };

    /// <summary>Human-readable label, the detail to show/confirm, and whether the call is destructive.</summary>
    private static (string Summary, string Detail, bool Destructive) Describe(
        Project project, AgentToolCall call, string? path)
    {
        // MCP tools reach external services — always treat as approval-worthy (a trusted server bypasses the
        // prompt separately, in ExecuteAsync). Show the server + tool from the namespaced name.
        if (McpToolName.IsMcp(call.Name))
        {
            McpToolName.TryParse(call.Name, out var server, out var tool);
            return ($"Call {server} (MCP)", tool, true);
        }

        return call.Name switch
        {
        "list_directory" => ("List directory", path ?? ".", false),
        "read_file"      => ("Read file", path ?? "", false),
        "search_files"   => ("Search files", GetString(call.Arguments, "pattern") ?? "", false),
        "find_files"     => ("Find files", GetString(call.Arguments, "glob") ?? "", false),
        "create_folder"  => ("Create folder", path ?? "", false),
        "write_file"     => ("Write file", path ?? "", WouldOverwrite(project, path)),
        "edit_file"      => ("Edit file", path ?? "", true),
        "move_file"      => ("Move/rename file", GetString(call.Arguments, "source") ?? "", true),
        "copy_file"      => ("Copy file", GetString(call.Arguments, "source") ?? "", false),
        "delete_file"    => ("Delete file", path ?? "", true),
        "delete_folder"  => ("Delete folder", path ?? "", true),
        "run_command"    => ("Run command", GetString(call.Arguments, "command") ?? "", true),
        "web_search"     => ("Web search", GetString(call.Arguments, "query") ?? "", false),
        "update_plan"    => ("Update plan", "", false),
        "install_software" => ("Install software", GetString(call.Arguments, "command") ?? "", true),
        "remember"       => ("Remember a note", GetString(call.Arguments, "text") ?? "", false),
        "create_skill"   => ("Create project skill", GetString(call.Arguments, "name") ?? "", false),
        "create_agent"   => ("Create project agent", GetString(call.Arguments, "name") ?? "", false),
        "update_docs"    => ("Update project handbook", ".AI/" + ProjectDocsService.FileName, true),
        _ => (call.Name, "", true)
        };
    }

    /// <summary>
    /// A compact one-line label for the live "current action" status bar (Phase 3A): the tool's glyph +
    /// summary, plus a shortened target/command when present — e.g. "⌘ Run command · npm run build". Pure
    /// and deterministic: the detail is collapsed to a single line and capped so a long command can't blow
    /// out the status line (the bar also ellipsizes, but the cap keeps the string itself bounded).
    /// </summary>
    internal static string CurrentActionLabel(string tool, string summary, string detail)
    {
        var icon = IconFor(tool);
        var d = (detail ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (d.Length == 0)
            return $"{icon} {summary}";

        const int max = 80;
        if (d.Length > max)
            d = d[..max] + "…";
        return $"{icon} {summary} · {d}";
    }

    /// <summary>Tool glyph for the structured activity feed's "Started" row.</summary>
    internal static string IconFor(string tool) => tool switch
    {
        "list_directory"   => "📂",
        "read_file"        => "📄",
        "search_files"     => "🔍",
        "find_files"       => "🔎",
        "write_file"       => "✏️",
        "edit_file"        => "✏️",
        "move_file"        => "➡️",
        "copy_file"        => "📋",
        "create_folder"    => "📁",
        "delete_file"      => "🗑",
        "delete_folder"    => "🗑",
        "run_command"      => "⌘",
        "install_software" => "📦",
        "web_search"       => "🌐",
        "update_plan"      => "📝",
        "remember"         => "💾",
        "create_skill"     => "✨",
        "create_agent"     => "👤",
        "update_docs"      => "📘",
        _ when McpToolName.IsMcp(tool) => "🔌",
        _                  => "🔧"
    };

    /// <summary>
    /// Whether a tool <paramref name="result"/> string indicates the call didn't succeed (drives the ✗ vs ✓
    /// status glyph). Heuristic on the markers <see cref="ExecuteAsync"/> and the tool helpers return: an
    /// "Error:" prefix, the refusal/guard phrases (declined, not permitted, disabled, refusing, handbook-guard),
    /// and the operational failures (not-found, command-start failure, timeout, sandbox-escape block). Markers
    /// are deliberately distinctive (e.g. "not found:" with the colon) to avoid matching file/command output
    /// that merely mentions the words.
    /// </summary>
    internal static bool IsFailure(string result)
    {
        if (string.IsNullOrEmpty(result))
            return false;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var marker in FailureMarkers)
            if (result.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static readonly string[] FailureMarkers =
    {
        "declined to run", "not permitted", "is disabled", "Refusing to",
        "can only be changed with the update_docs",
        "not found:", "Failed to start command", "timed out after", "was blocked",
        "— nothing changed." // distinctive edit_file/move/copy no-op suffix (em-dash form avoids matching benign tool output)
    };

    /// <summary>
    /// Surfaces the agent's plan to the UI (the full list is resent each call). Pure UI side effect via
    /// <paramref name="onPlan"/> — no files touched. Prefers a <c>phases</c> array (named phases, each with its
    /// own steps); falls back to a flat <c>steps</c> checklist. Returns a short confirmation. <c>internal</c>
    /// so the Lead orchestrator can reuse it.
    /// </summary>
    internal static string UpdatePlan(JsonElement args, Action<PlanUpdate>? onPlan)
    {
        var phases = ParsePhases(args);
        if (phases.Count > 0)
        {
            onPlan?.Invoke(new PlanUpdate(System.Array.Empty<PlanStep>(), phases));
            var pdone = phases.Count(p => p.Status == PlanStepStatus.Done);
            var pactive = phases.Count(p => p.Status == PlanStepStatus.Active);
            return $"Plan updated: {phases.Count} phase(s) — {pdone} done, {pactive} in progress.";
        }

        var steps = ParsePlanSteps(args);
        if (steps.Count == 0)
            return "Provide a non-empty 'steps' array (each with 'text' and optional 'status'), " +
                   "or a 'phases' array (each with a 'name' and its own 'steps').";

        onPlan?.Invoke(new PlanUpdate(steps));

        var done = steps.Count(s => s.Status == PlanStepStatus.Done);
        var active = steps.Count(s => s.Status == PlanStepStatus.Active);
        return $"Plan updated: {steps.Count} step(s) — {done} done, {active} in progress.";
    }

    /// <summary>Parses the top-level <c>steps</c> array of update_plan into <see cref="PlanStep"/>s (flat plan).</summary>
    internal static IReadOnlyList<PlanStep> ParsePlanSteps(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return System.Array.Empty<PlanStep>();
        return ParseStepsArray(steps);
    }

    /// <summary>
    /// Parses a JSON array of steps (each item a string or {text, status}) into <see cref="PlanStep"/>s. Fully
    /// value-kind guarded — malformed items are skipped, never thrown on. <c>status</c> is honored only when a
    /// JSON string (a numeric/bool status degrades to Pending). Shared by the flat plan and each phase's steps.
    /// </summary>
    internal static IReadOnlyList<PlanStep> ParseStepsArray(JsonElement stepsArray)
    {
        var list = new List<PlanStep>();
        if (stepsArray.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in stepsArray.EnumerateArray())
        {
            string text;
            var status = PlanStepStatus.Pending;
            if (el.ValueKind == JsonValueKind.String)
            {
                text = el.GetString() ?? "";
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                text = el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() ?? "" : "";
                if (el.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
                    status = ParseStatus(st.GetString());
            }
            else continue;

            text = text.Trim();
            if (text.Length > 0)
                list.Add(new PlanStep(text, status));
        }
        return list;
    }

    /// <summary>
    /// Parses the update_plan <c>phases</c> array (each item {name, status?, steps?}). Fully value-kind guarded
    /// — items without a non-empty string <c>name</c> are skipped, malformed steps dropped, never thrown on.
    /// <c>internal</c> for unit testing.
    /// </summary>
    internal static IReadOnlyList<PlanPhase> ParsePhases(JsonElement args)
    {
        var list = new List<PlanPhase>();
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("phases", out var phases) || phases.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in phases.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            var name = el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? (n.GetString() ?? "").Trim() : "";
            if (name.Length == 0)
                continue;

            var status = el.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? ParseStatus(st.GetString()) : PlanStepStatus.Pending;
            var steps = el.TryGetProperty("steps", out var s) ? ParseStepsArray(s) : System.Array.Empty<PlanStep>();

            list.Add(new PlanPhase(name, status, steps));
        }
        return list;
    }

    /// <summary>The name of the phase currently marked Active (the convention is exactly one), or null.</summary>
    internal static string? ActivePhaseName(IReadOnlyList<PlanPhase> phases) =>
        phases.FirstOrDefault(p => p.Status == PlanStepStatus.Active)?.Name;

    /// <summary>
    /// Detects a move into a NEW phase across the (stateless) update_plan calls: returns a
    /// <see cref="PhaseGate"/> when the active phase's name changed from <paramref name="previousActivePhase"/>
    /// to a different non-null phase. Returns null before the first active phase, when nothing is active now,
    /// or when the active phase is unchanged — so a non-phased / non-compliant plan never gates.
    /// </summary>
    internal static PhaseGate? DetectPhaseAdvance(string? previousActivePhase, IReadOnlyList<PlanPhase> phases)
    {
        if (string.IsNullOrEmpty(previousActivePhase))
            return null;
        var now = ActivePhaseName(phases);
        return now is null || string.Equals(now, previousActivePhase, StringComparison.OrdinalIgnoreCase)
            ? null
            : new PhaseGate(previousActivePhase, now);
    }

    /// <summary>
    /// The shared phase-gate step for both agent loops: parse the just-executed <c>update_plan</c> args,
    /// detect a move into a new phase, advance the previous-active-phase tracker, and — when not auto-flowing
    /// — ask the user to continue. Returns the updated tracker and, if the user declined, the
    /// <see cref="PhaseGate"/> to stop at (the caller emits the "paused" answer). UI-free.
    /// </summary>
    internal static async Task<(string? PreviousActivePhase, PhaseGate? Stop)> ApplyPhaseGateAsync(
        string? previousActivePhase, JsonElement updatePlanArgs,
        bool autoFlowPhases, Func<PhaseGate, Task<bool>>? phaseGate)
    {
        var phases = ParsePhases(updatePlanArgs);
        var advance = DetectPhaseAdvance(previousActivePhase, phases);
        var next = ActivePhaseName(phases) ?? previousActivePhase;
        if (advance is not null && !autoFlowPhases && phaseGate is not null
            && !await phaseGate(advance).ConfigureAwait(false))
            return (next, advance); // user declined → stop, carrying the phase we won't enter
        return (next, null);
    }

    /// <summary>Maps a free-form status string to <see cref="PlanStepStatus"/> (default Pending). Tolerant of synonyms.</summary>
    internal static PlanStepStatus ParseStatus(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "done" or "complete" or "completed" or "finished" or "x" or "✓" => PlanStepStatus.Done,
        "active" or "in_progress" or "in-progress" or "in progress"
            or "doing" or "current" or "wip" => PlanStepStatus.Active,
        _ => PlanStepStatus.Pending
    };

    /// <summary>Persists a fact to memory. <c>scope</c> "user" → global; anything else (default) → this project.</summary>
    private string Remember(Project project, string? text, string? scope)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return "Nothing to remember (no text was provided).";

        if (string.Equals(scope?.Trim(), "user", StringComparison.OrdinalIgnoreCase))
        {
            _memory.Add(MemoryScope.Global, text, "project-agent", project.Directory);
            return $"Remembered (about the user): {text}";
        }

        _memory.Add(MemoryScope.Project, text, "project-agent", project.Directory);
        return $"Remembered (this project): {text}";
    }

    /// <summary>
    /// Writes a reusable project skill to <c>&lt;project&gt;/.AI/skills/&lt;slug&gt;.skill.md</c> (frontmatter +
    /// the model-authored body). The path is computed here from a slugified name, so the model can't write
    /// outside the skills folder. Files here load as project guidance the next time the project is opened.
    /// </summary>
    private static string CreateSkill(Project project, string? name, string? content, string? description)
    {
        name = (name ?? "").Trim();
        content = (content ?? "").Trim();
        if (name.Length == 0 || content.Length == 0)
            return "Provide both a skill 'name' and 'content'.";

        var dir = Path.Combine(project.Directory, ".AI", "skills");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Slugify(name) + ".skill.md");

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(name.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
        if (!string.IsNullOrWhiteSpace(description))
            sb.Append("description: ").Append(description!.Replace('\n', ' ').Replace('\r', ' ').Trim()).Append('\n');
        sb.Append("---\n\n");
        sb.Append(content).Append('\n');
        File.WriteAllText(file, sb.ToString());

        return $"Created skill '{name}' at {Rel(project, file)} ({content.Length} chars). " +
               "It loads as project guidance the next time this project is opened.";
    }

    /// <summary>
    /// Authors a NEW project-scoped agent (a specialist persona) at <c>.AI/agents/&lt;slug&gt;.md</c> via
    /// <see cref="IAgentService.SaveCustom"/>. Always offered (it writes only to that controlled location). The
    /// new agent becomes a delegation target for the Lead and appears in the agent picker. Built-in ids are
    /// refused (they can't be overwritten); an existing custom id is updated in place.
    /// </summary>
    private string CreateAgent(Project project, string? name, string? persona, string? description, string? glyph, string? tools)
    {
        name = (name ?? "").Trim();
        persona = (persona ?? "").Trim();
        if (name.Length == 0 || persona.Length == 0)
            return "Provide both an agent 'name' and a 'persona' (its role/instructions).";

        var id = Slugify(name);
        if (id.Length == 0)
            return "Couldn't derive a valid id from that name — use letters or digits.";

        var existing = _agents.Get(id, project.Directory);
        if (existing is { IsBuiltIn: true })
            return $"'{id}' is a built-in agent id and can't be overwritten — choose a different name.";

        var agent = new Agent
        {
            Id = id,
            Name = name,
            Glyph = string.IsNullOrWhiteSpace(glyph) ? "🤖" : glyph!.Trim(),
            Description = (description ?? "").Trim(),
            Persona = persona,
            Tools = AgentMarkdown.ParseTools(tools), // null/"all" ⇒ unrestricted (capped by the Lead when delegated)
            Scope = AgentScope.Project
        };
        _agents.SaveCustom(agent, project.Directory);

        return $"{(existing is null ? "Created" : "Updated")} project agent '{name}' (id '{id}') at " +
               $".AI/agents/{id}.md. It's available as a specialist the Lead can delegate to, and in the agent picker.";
    }

    /// <summary>
    /// Creates or overwrites the project handbook at <c>.AI/AI_DOCS.md</c> with the model-authored Markdown.
    /// This is the SOLE writer of that file (write_file/delete_file/delete_folder refuse it), so the handbook
    /// stays under a single sanctioned, approval-gated path. It loads as authoritative project guidance on the
    /// next turn (the VM re-scans after every project-agent turn).
    /// </summary>
    private string UpdateDocs(Project project, string? content)
    {
        content = (content ?? "").Trim();
        if (content.Length == 0)
            return "Provide non-empty 'content' — the COMPLETE new handbook markdown to write.";

        _docs.Save(project.Directory, content);
        return $"Updated the project handbook (.AI/{ProjectDocsService.FileName}) — {content.Length} chars. " +
               "It loads as authoritative project guidance on the next turn.";
    }

    /// <summary>Lowercases a name and reduces it to a safe filename slug (letters/digits → '-' runs collapsed).</summary>
    private static string Slugify(string s)
    {
        var slug = new string(s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length == 0 ? "skill" : slug;
    }

    private static readonly string[] SystemInstallSignatures =
    {
        "winget install", "winget upgrade", "choco install", "choco upgrade", "scoop install",
        "apt install", "apt-get install", "apt install", "dnf install", "yum install",
        "pacman -s", "zypper install", "brew install", "snap install",
        "npm install -g", "npm i -g", "npm install --global", "yarn global add", "pnpm add -g"
    };

    /// <summary>Heuristically detects a machine-wide package-manager install command (to gate run_command).</summary>
    private static bool LooksLikeSystemInstall(string command)
    {
        var c = command.ToLowerInvariant();
        foreach (var sig in SystemInstallSignatures)
            if (c.Contains(sig))
                return true;
        // elevated install on Windows (e.g. "sudo …" handled above for *nix package managers)
        return c.Contains("msiexec") || c.Contains("sudo apt") || c.Contains("sudo dnf") || c.Contains("sudo yum");
    }

    private static bool WouldOverwrite(Project project, string? path) =>
        TryResolve(project.Directory, path, out var full, out _) && File.Exists(full);

    /// <summary>Maps a tool name to the <see cref="AgentToolGroup"/> that gates it (null = always allowed).</summary>
    private static AgentToolGroup? ToolGroupOf(string toolName) => toolName switch
    {
        "list_directory" or "read_file"
            or "search_files" or "find_files"          => AgentToolGroup.ReadFiles,
        "write_file" or "create_folder"
            or "edit_file" or "move_file" or "copy_file" => AgentToolGroup.WriteFiles,
        "delete_file" or "delete_folder"               => AgentToolGroup.DeleteFiles,
        "run_command"                                  => AgentToolGroup.RunCommands,
        "install_software"                             => AgentToolGroup.InstallSoftware,
        "update_docs"                                  => null, // writes only to the fixed handbook path — not allow-list-gated
        "web_search"                                   => null, // network read, not a file/command tool — ungated
        "update_plan"                                  => null, // UI checklist only — not a file/command tool — ungated
        _ when McpToolName.IsMcp(toolName)             => AgentToolGroup.Mcp, // external-service tools — gated by the Mcp group
        _                                              => null
    };

    private static string PermissionLabel(AgentToolGroup group) => group switch
    {
        AgentToolGroup.ReadFiles       => "read files or list directories",
        AgentToolGroup.WriteFiles      => "create or write files and folders",
        AgentToolGroup.DeleteFiles     => "delete files or folders",
        AgentToolGroup.RunCommands     => "run terminal commands",
        AgentToolGroup.InstallSoftware => "install software",
        AgentToolGroup.Mcp             => "use external (MCP) tools",
        _                              => "use that tool"
    };

    // ---- tools (all confined to the project directory) -------------------------------------

    private static string ListDirectory(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (!Directory.Exists(full))
            return $"Directory not found: {Rel(project, full)}";

        var sb = new StringBuilder();
        foreach (var dir in Directory.GetDirectories(full).OrderBy(p => p))
            sb.AppendLine($"[dir]  {Path.GetFileName(dir)}/");
        foreach (var file in Directory.GetFiles(full).OrderBy(p => p))
            sb.AppendLine($"[file] {Path.GetFileName(file)}  ({new FileInfo(file).Length} bytes)");

        var listing = sb.ToString();
        return listing.Length == 0 ? "(empty directory)" : listing.TrimEnd();
    }

    private static string ReadFile(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (!File.Exists(full))
            return $"File not found: {Rel(project, full)}";
        return File.ReadAllText(full);
    }

    private static string WriteFile(Project project, string? path, string content)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
        return $"Wrote {content.Length} characters to {Rel(project, full)}.";
    }

    private static string CreateFolder(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        Directory.CreateDirectory(full);
        return $"Created folder {Rel(project, full)}.";
    }

    private static string DeleteFile(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
        if (!File.Exists(full))
            return $"File not found: {Rel(project, full)}";
        File.Delete(full);
        return $"Deleted file {Rel(project, full)}.";
    }

    private static string DeleteFolder(Project project, string? path)
    {
        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
        if (string.Equals(Path.GetFullPath(full).TrimEnd(Path.DirectorySeparatorChar),
                           Path.GetFullPath(project.Directory).TrimEnd(Path.DirectorySeparatorChar),
                           PathComparison))
            return "Refusing to delete the project root directory itself.";
        if (ContainsHandbook(project, full))
            return "Refusing to delete the .AI folder — it holds the project handbook (.AI/" +
                   ProjectDocsService.FileName + "), skills, agents, and memory. Use update_docs to change the handbook.";
        if (!Directory.Exists(full))
            return $"Folder not found: {Rel(project, full)}";
        Directory.Delete(full, recursive: true);
        return $"Deleted folder {Rel(project, full)} (and its contents).";
    }

    private static async Task<string> RunCommandAsync(Project project, string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "No command was provided.";

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = project.Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c " + command; // cmd handles the rest of the line as the command
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return $"Failed to start command: {ex.Message}";
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CommandTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            return $"Command timed out after {CommandTimeout.TotalSeconds:0}s and was terminated.";
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw; // user-requested cancellation bubbles up
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"exit code: {process.ExitCode}");
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.Append("stdout:\n").AppendLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.Append("stderr:\n").AppendLine(stderr.TrimEnd());
        return sb.ToString().TrimEnd();
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    // ---- search / find / edit / move / copy / web (all confined to the project) ------------

    /// <summary>
    /// grep over project files: returns matching lines as <c>relpath:line: text</c> (capped). <c>internal</c>
    /// so the orchestrator's lead loop can reuse it (read-only; no instance state).
    /// </summary>
    internal static string SearchFiles(Project project, string? pattern, string? path, string? glob)
    {
        pattern = (pattern ?? "").Trim();
        if (pattern.Length == 0)
            return "Provide a non-empty 'pattern' to search for.";

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (Exception ex) { return $"Error: invalid search pattern — {ex.Message}"; }

        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;

        Regex? globRegex = null;
        var globFull = false;
        if (!string.IsNullOrWhiteSpace(glob))
        {
            globRegex = GlobToRegex(glob!);
            globFull = GlobMatchesPath(glob!);
        }

        IEnumerable<string> files;
        if (File.Exists(full)) files = new[] { full };
        else if (Directory.Exists(full)) files = EnumerateProjectFiles(full);
        else return $"Path not found: {Rel(project, full)}";

        var sb = new StringBuilder();
        var matches = 0;
        foreach (var file in files)
        {
            if (matches >= MaxSearchMatches) break;

            if (globRegex is not null)
            {
                var forGlob = globFull ? RelForward(project, file) : Path.GetFileName(file);
                if (!globRegex.IsMatch(forGlob)) continue;
            }

            long length;
            try { length = new FileInfo(file).Length; } catch { continue; }
            if (length > MaxScanFileBytes) continue;

            string text;
            try { text = File.ReadAllText(file); } catch { continue; }
            if (text.IndexOf('\0') >= 0) continue; // skip binary

            var rel = RelForward(project, file);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length && matches < MaxSearchMatches; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (regex.IsMatch(line))
                {
                    sb.Append(rel).Append(':').Append(i + 1).Append(": ").AppendLine(line.Trim());
                    matches++;
                }
            }
        }

        if (matches == 0)
            return $"No matches for /{pattern}/" + (string.IsNullOrWhiteSpace(glob) ? "" : $" in files matching '{glob}'") + ".";
        var header = matches >= MaxSearchMatches ? $"(showing the first {MaxSearchMatches} matches)\n" : "";
        return header + sb.ToString().TrimEnd();
    }

    /// <summary>Lists project files whose path (or name) matches a glob (capped). <c>internal</c> for lead reuse.</summary>
    internal static string FindFiles(Project project, string? glob, string? path)
    {
        glob = (glob ?? "").Trim();
        if (glob.Length == 0)
            return "Provide a non-empty 'glob' pattern (e.g. \"**/*.cs\").";

        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (!Directory.Exists(full))
            return $"Directory not found: {Rel(project, full)}";

        var regex = GlobToRegex(glob);
        var matchFull = GlobMatchesPath(glob);

        var hits = new List<string>();
        foreach (var file in EnumerateProjectFiles(full))
        {
            var target = matchFull ? RelForward(project, file) : Path.GetFileName(file);
            if (regex.IsMatch(target))
            {
                hits.Add(RelForward(project, file));
                if (hits.Count >= MaxFindResults) break;
            }
        }

        if (hits.Count == 0)
            return $"No files match '{glob}'.";
        hits.Sort(StringComparer.OrdinalIgnoreCase);
        var header = hits.Count >= MaxFindResults ? $"(showing the first {MaxFindResults})\n" : "";
        return header + string.Join('\n', hits);
    }

    /// <summary>Replaces the FIRST exact occurrence of <paramref name="find"/> with <paramref name="replace"/>.</summary>
    private static string EditFile(Project project, string? path, string? find, string? replace)
    {
        if (string.IsNullOrEmpty(find))
            return "Provide the 'find' text to replace (it must match the file exactly).";
        replace ??= "";

        if (!TryResolve(project.Directory, path, out var full, out var error))
            return error;
        if (IsHandbookPath(project, full))
            return HandbookGuardMessage;
        if (!File.Exists(full))
            return $"File not found: {Rel(project, full)}";

        var content = File.ReadAllText(full);
        var idx = content.IndexOf(find, StringComparison.Ordinal);
        if (idx < 0)
            return $"The 'find' text was not present in {Rel(project, full)} — nothing changed. " +
                   "Read the file and copy an exact snippet (whitespace and indentation included).";

        var count = CountOccurrences(content, find);
        var updated = content[..idx] + replace + content[(idx + find.Length)..];
        File.WriteAllText(full, updated);
        return count == 1
            ? $"Edited {Rel(project, full)} — replaced 1 occurrence ({find.Length}→{replace.Length} chars)."
            : $"Edited {Rel(project, full)} — replaced the first of {count} occurrences.";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0) return 0;
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>Moves/renames a file or folder within the project.</summary>
    private static string MoveFile(Project project, string? source, string? destination)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            return "Provide both 'source' and 'destination' paths.";
        if (!TryResolve(project.Directory, source, out var src, out var e1)) return e1;
        if (!TryResolve(project.Directory, destination, out var dst, out var e2)) return e2;
        if (IsHandbookPath(project, src) || IsHandbookPath(project, dst))
            return HandbookGuardMessage;

        var isDir = Directory.Exists(src);
        if (!isDir && !File.Exists(src))
            return $"Source not found: {Rel(project, src)}";
        if (Directory.Exists(dst) || File.Exists(dst))
            return $"Destination already exists: {Rel(project, dst)}. Delete it or choose another path " +
                   "— nothing changed.";

        var dstDir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dstDir))
            Directory.CreateDirectory(dstDir);

        if (isDir) Directory.Move(src, dst);
        else File.Move(src, dst);
        return $"Moved {Rel(project, src)} → {Rel(project, dst)}.";
    }

    /// <summary>Copies a file to a new path within the project.</summary>
    private static string CopyFile(Project project, string? source, string? destination)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            return "Provide both 'source' and 'destination' paths.";
        if (!TryResolve(project.Directory, source, out var src, out var e1)) return e1;
        if (!TryResolve(project.Directory, destination, out var dst, out var e2)) return e2;
        if (IsHandbookPath(project, dst))
            return HandbookGuardMessage;
        if (!File.Exists(src))
            return $"File not found: {Rel(project, src)}";
        if (Directory.Exists(dst) || File.Exists(dst))
            return $"Destination already exists: {Rel(project, dst)}. Delete it or choose another path " +
                   "— nothing changed.";

        var dstDir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dstDir))
            Directory.CreateDirectory(dstDir);
        File.Copy(src, dst);
        return $"Copied {Rel(project, src)} → {Rel(project, dst)}.";
    }

    /// <summary>Searches the web via the shared <see cref="IWebSearchService"/>; formats the top results.</summary>
    private async Task<string> WebSearchAsync(string? query, string? maxRaw, CancellationToken ct)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0)
            return "Provide a non-empty 'query' to search the web for.";

        var max = 5;
        if (int.TryParse(maxRaw, out var m))
            max = Math.Clamp(m, 1, 10);

        IReadOnlyList<SearchResult> results;
        try { results = await _search.SearchAsync(query, max, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return $"Error: web search failed — {ex.Message}"; }

        if (results.Count == 0)
            return $"No web results for '{query}'.";

        var sb = new StringBuilder();
        var i = 1;
        foreach (var r in results)
        {
            sb.Append(i++).Append(". ").AppendLine(r.Title);
            sb.Append("   ").AppendLine(r.Url);
            if (!string.IsNullOrWhiteSpace(r.Snippet))
                sb.Append("   ").AppendLine(r.Snippet.Trim());
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Walks files under <paramref name="root"/>, pruning <see cref="ScanExcludeDirs"/> (by name).</summary>
    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (ScanExcludeDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                stack.Push(sub);
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }
            foreach (var f in files)
                yield return f;
        }
    }

    /// <summary>Project-relative path with forward slashes (stable display + glob matching across OSes).</summary>
    private static string RelForward(Project project, string full) =>
        Path.GetRelativePath(project.Directory, full).Replace('\\', '/');

    /// <summary>True when a glob is path-scoped (contains a separator or <c>**</c>); else it matches by file name.</summary>
    internal static bool GlobMatchesPath(string glob) =>
        glob.Contains('/') || glob.Contains('\\') || glob.Contains("**");

    /// <summary>
    /// Compiles a glob to an anchored, case-insensitive <see cref="Regex"/> over a '/'-separated path:
    /// <c>*</c> matches within a segment, <c>**</c> matches across segments (<c>**/</c> also matches zero
    /// segments), <c>?</c> matches one non-separator char, everything else is literal. Pure (unit-tested).
    /// </summary>
    internal static Regex GlobToRegex(string glob)
    {
        glob = (glob ?? "").Replace('\\', '/');
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++; // consume the second '*'
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++; // consume the '/', so "**/x" also matches "x" at the root
                            sb.Append("(?:.*/)?");
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolves a (relative or absolute) path and rejects anything outside the project root.</summary>
    private static bool TryResolve(string root, string? relative, out string full, out string error)
    {
        relative ??= "";
        var rootFull = Path.GetFullPath(root);
        var combined = Path.IsPathRooted(relative) ? relative : Path.Combine(rootFull, relative);
        full = Path.GetFullPath(combined);

        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!string.Equals(full, rootFull, PathComparison) && !full.StartsWith(rootWithSep, PathComparison))
        {
            error = $"Path '{relative}' is outside the project directory and was blocked.";
            full = "";
            return false;
        }

        error = "";
        return true;
    }

    private static string Rel(Project project, string full)
    {
        var rel = Path.GetRelativePath(project.Directory, full);
        return rel == "." ? "(project root)" : rel;
    }

    /// <summary>Message returned when a generic write/delete tool targets the handbook (use update_docs instead).</summary>
    private const string HandbookGuardMessage =
        "The project handbook (.AI/" + ProjectDocsService.FileName + ") can only be changed with the update_docs tool.";

    /// <summary>
    /// True when <paramref name="fullPath"/> is the project handbook (.AI/AI_DOCS.md). The generic
    /// write_file/delete_file/delete_folder tools refuse it so update_docs stays the sole writer — this stops
    /// any agent (including a delegated specialist that lacks update_docs) from clobbering the handbook.
    /// </summary>
    internal static bool IsHandbookPath(Project project, string fullPath)
    {
        var handbook = Path.GetFullPath(Path.Combine(project.Directory, ".AI", ProjectDocsService.FileName));
        return string.Equals(
            Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar),
            handbook.TrimEnd(Path.DirectorySeparatorChar),
            PathComparison);
    }

    /// <summary>
    /// True when <paramref name="fullDir"/> is a directory that CONTAINS the handbook (the <c>.AI</c> folder,
    /// or an ancestor of it). Lets delete_folder refuse wiping the handbook — and the rest of <c>.AI</c>
    /// (skills, agents, memory, chats) — by deleting a parent folder rather than the file itself.
    /// </summary>
    internal static bool ContainsHandbook(Project project, string fullDir)
    {
        var handbook = Path.GetFullPath(Path.Combine(project.Directory, ".AI", ProjectDocsService.FileName));
        var dir = Path.GetFullPath(fullDir).TrimEnd(Path.DirectorySeparatorChar);
        return handbook.StartsWith(dir + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static string Truncate(string s) =>
        s.Length <= MaxResultChars ? s : s[..MaxResultChars] + "\n…(truncated)";

    /// <summary>Indents a tool result two spaces per line for a readable inline transcript log.</summary>
    private static string IndentForDisplay(string s)
    {
        var trimmed = Truncate(s.TrimEnd());
        var lines = trimmed.Split('\n');
        return string.Join('\n', lines.Select(l => "   " + l));
    }

    private static string SystemPrompt(Project project, SoftwareInstallPermission installPermission)
    {
        var install = installPermission != SoftwareInstallPermission.Never
            ? "- If the project is missing software needed to run or build it (a runtime, SDK, CLI, or " +
              "package manager package), you MAY install it with the install_software tool, which the user " +
              "has permitted for this project. Prefer the platform's package manager."
            : "- You may NOT install software machine-wide. If the project needs a missing runtime/SDK/tool, " +
              "say so and ask the user to allow installs in Settings → Project. " +
              "Installing project-local dependencies (e.g. npm install in the project) is still fine.";

        return $"""
        You are the project agent for "{project.Name}", working inside this directory:
          {project.Directory}

        You have tools to inspect and change the project and to run terminal commands. Rules:
        - All paths are relative to the project root. You cannot read or change anything outside it.
        - Terminal commands run with the project root as the working directory.
        - Inspect before you change: use search_files / find_files / read_file to locate and read code
          before write_file, edit_file, or delete.
        - For a small change, prefer edit_file (replace an exact snippet) over rewriting a whole file with
          write_file.
        - Make the smallest change that satisfies the request. Do not invent unrelated work.
        {install}
        - When the task is complete, stop calling tools and reply with a short plain-text summary of
          what you did.
        """;
    }

    /// <summary>
    /// Advertises only the tools the active agent is permitted to use. Each tool group is gated by the
    /// agent's <see cref="AgentTools"/> allow-list; <c>install_software</c> additionally requires the global
    /// <see cref="SoftwareInstallPermission"/> to not be <see cref="SoftwareInstallPermission.Never"/> (both
    /// gates must allow). An unrestricted agent (the default) offers the full set, so behaviour is unchanged
    /// when the user hasn't restricted anything.
    /// </summary>
    private static IReadOnlyList<AgentTool> BuildTools(AgentTools allowed, SoftwareInstallPermission installPermission, bool memoryEnabled, bool allowDocsUpdate)
    {
        static JsonElement Schema(object o) => JsonSerializer.SerializeToElement(o);

        var pathOnly = Schema(new
        {
            type = "object",
            properties = new { path = new { type = "string", description = "Path relative to the project root." } },
            required = new[] { "path" }
        });

        var commandSchema = Schema(new
        {
            type = "object",
            properties = new { command = new { type = "string", description = "The shell command line to run." } },
            required = new[] { "command" }
        });

        var tools = new List<AgentTool>();

        if (allowed.Allows(AgentToolGroup.ReadFiles))
        {
            tools.Add(new("list_directory",
                "List the files and sub-folders of a directory in the project (path defaults to the root).",
                Schema(new
                {
                    type = "object",
                    properties = new { path = new { type = "string", description = "Directory path relative to the project root. Omit or \".\" for the root." } }
                })));
            tools.Add(new("read_file", "Read and return the full text of a file in the project.", pathOnly));
            tools.Add(new("search_files",
                "Search file contents across the project for a regular expression (like grep). Returns matching " +
                "lines as 'path:line: text'. Use this to locate code instead of reading whole files. Skips " +
                ".git/.AI/node_modules/bin/obj and binary/large files; results are capped.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new { type = "string", description = "Regular expression to search for (case-insensitive)." },
                        path = new { type = "string", description = "File or folder to search in, relative to the project root. Omit or \".\" for the whole project." },
                        glob = new { type = "string", description = "Optional glob to limit which files are searched, e.g. \"*.cs\" or \"src/**/*.ts\"." }
                    },
                    required = new[] { "pattern" }
                })));
            tools.Add(new("find_files",
                "Find files in the project whose path matches a glob pattern (e.g. \"**/*.razor\", \"*.csproj\", " +
                "\"src/**/*.ts\"). Returns matching paths. Cheaper than listing directories recursively.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        glob = new { type = "string", description = "Glob pattern. A pattern without '/' matches by file name anywhere (e.g. \"*.cs\"); include '/' or '**' to match by path." },
                        path = new { type = "string", description = "Folder to search under, relative to the project root. Omit or \".\" for the whole project." }
                    },
                    required = new[] { "glob" }
                })));
        }

        if (allowed.Allows(AgentToolGroup.WriteFiles))
        {
            tools.Add(new("write_file",
                "Create a file or overwrite an existing one with the given text content.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "File path relative to the project root." },
                        content = new { type = "string", description = "Full text to write to the file." }
                    },
                    required = new[] { "path", "content" }
                })));
            tools.Add(new("edit_file",
                "Make a targeted edit to an existing file: replace the FIRST exact occurrence of 'find' with " +
                "'replace', without rewriting the whole file. Prefer this over write_file for small changes. " +
                "'find' must match the file's text exactly (including whitespace); read the file first if unsure.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "File path relative to the project root." },
                        find = new { type = "string", description = "The exact text to find (the first occurrence is replaced)." },
                        replace = new { type = "string", description = "The replacement text." }
                    },
                    required = new[] { "path", "find", "replace" }
                })));
            tools.Add(new("move_file",
                "Move or rename a file or folder within the project.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        source = new { type = "string", description = "Existing path, relative to the project root." },
                        destination = new { type = "string", description = "New path, relative to the project root." }
                    },
                    required = new[] { "source", "destination" }
                })));
            tools.Add(new("copy_file",
                "Copy a file to a new path within the project.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        source = new { type = "string", description = "Existing file path, relative to the project root." },
                        destination = new { type = "string", description = "Destination path, relative to the project root." }
                    },
                    required = new[] { "source", "destination" }
                })));
            tools.Add(new("create_folder", "Create a folder (and any missing parents) in the project.", pathOnly));
        }

        if (allowed.Allows(AgentToolGroup.DeleteFiles))
        {
            tools.Add(new("delete_file", "Delete a file in the project.", pathOnly));
            tools.Add(new("delete_folder", "Delete a folder in the project and everything inside it.", pathOnly));
        }

        if (allowed.Allows(AgentToolGroup.RunCommands))
        {
            tools.Add(new("run_command",
                "Run a terminal command with the project root as the working directory; returns its exit code and output.",
                commandSchema));
        }

        // install_software needs BOTH the agent's permission AND the project's software-install setting.
        if (allowed.Allows(AgentToolGroup.InstallSoftware) && installPermission != SoftwareInstallPermission.Never)
        {
            tools.Add(new AgentTool("install_software",
                "Install software/tooling machine-wide (e.g. a runtime, SDK, CLI, or package-manager package) " +
                "needed to run or build the project. Provide the full install command (e.g. 'winget install ...', " +
                "'apt-get install -y ...', 'brew install ...').",
                commandSchema));
        }

        // web_search: a read-only network tool (reuses the app's web-search service). Not a file/command
        // tool, so it isn't gated by the allow-list — any agent may look things up while it works.
        tools.Add(new AgentTool("web_search",
            "Search the web for current information while working (docs, error messages, API/syntax). Returns " +
            "the top results as title + url + snippet. Use it when project files don't have the answer.",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "The web search query." },
                    max = new { type = "integer", description = "How many results to return (1–10, default 5)." }
                },
                required = new[] { "query" }
            })));

        // update_plan: a UI-only plan/checklist the agent maintains (no file/command side effects), so it's
        // ungated like web_search. The agent resends the full list each call (flat steps or named phases).
        tools.Add(new AgentTool("update_plan", UpdatePlanToolDescription, UpdatePlanSchema()));

        // create_skill: a meta tool that writes a reusable guidance file under .AI/skills/. Always offered
        // (it only writes to that controlled location), so "create a skill for X" works with any agent.
        tools.Add(new AgentTool("create_skill",
            "Create a reusable project skill file under .AI/skills/ that captures how to work in this project " +
            "(purpose, conventions, architecture, workflow steps, do/don't rules, examples). Use this when the " +
            "user asks to \"create a skill for <subject>\". Write thorough, well-structured Markdown in 'content' " +
            "with clear section headings so future sessions load it as authoritative project guidance.",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Short skill title, e.g. \"API conventions\"." },
                    description = new { type = "string", description = "One-line summary of what the skill covers." },
                    content = new { type = "string", description = "The full skill guidance as structured Markdown (sections, do/don't, examples)." }
                },
                required = new[] { "name", "content" }
            })));

        // create_agent: a meta tool that writes a NEW project-scoped specialist agent under .AI/agents/.
        // Always offered (writes only to that controlled location) so the agent can build out its team.
        tools.Add(new AgentTool("create_agent",
            "Create a new project-scoped specialist agent (saved under .AI/agents/) that the Lead can delegate " +
            "subtasks to and that appears in the agent picker. Use this to build out a team — e.g. \"create an " +
            "agent for writing tests\" or \"add a code-reviewer agent\". Give it a clear 'name' and a thorough " +
            "'persona' (its role, expertise, tone, and how it should work). Optionally restrict 'tools'.",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Short agent name, e.g. \"Test Writer\" (also becomes its id)." },
                    persona = new { type = "string", description = "The agent's system-prompt body: role, expertise, tone, working style." },
                    description = new { type = "string", description = "One-line \"when to use this agent\" summary." },
                    glyph = new { type = "string", description = "A single emoji avatar (optional; defaults to 🤖)." },
                    tools = new { type = "string", description = "Optional allow-list: 'all' (default) or a comma-separated subset of read, write, delete, run, install." }
                },
                required = new[] { "name", "persona" }
            })));

        // update_docs: maintains the project handbook (.AI/AI_DOCS.md). Like create_skill it writes only to a
        // fixed, controlled path so it isn't gated by the allow-list — but it's offered ONLY to a top-level
        // (main) agent run (allowDocsUpdate), never to a delegated specialist.
        if (allowDocsUpdate)
            tools.Add(new AgentTool("update_docs", UpdateDocsToolDescription, UpdateDocsSchema()));

        // remember: not a file/command tool, so it isn't gated by the allow-list — only by the memory switch.
        if (memoryEnabled)
        {
            tools.Add(new AgentTool("remember",
                "Save a short, durable fact to recall in future sessions (a user preference or a key detail " +
                "about this project). Use scope 'project' (default) for facts about this project, 'user' for " +
                "facts about the user. Only remember stable, useful facts — never transient or sensitive details.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string", description = "The fact to remember, as one short sentence." },
                        scope = new { type = "string", description = "'project' (default) or 'user'." }
                    },
                    required = new[] { "text" }
                })));
        }

        return tools;
    }
}
