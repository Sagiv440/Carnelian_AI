using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IAgentOrchestrator"/>. Runs the lead agent as a tool-calling loop (modelled on
/// <see cref="ProjectAgentService"/>): the lead inspects the project with read-only tools, then
/// <c>delegate_task(agent_id, task)</c>s subtasks to specialist agents from the roster. Each delegation
/// runs the EXISTING <see cref="IProjectAgentService.RunAsync"/> for that specialist (its own persona and
/// model), capturing the specialist's final answer via the <c>onAnswer</c> callback and returning it to
/// the lead as the tool result. The single global approval setting (passed in by the VM) governs the run.
/// When the lead replies with no tool calls, that text is the final summary — the same finish convention as
/// the single-agent loop.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    /// <summary>Hard cap on lead-loop rounds (delegations + inspections), to bound a runaway coordinator.</summary>
    private const int MaxDelegations = 12;

    /// <summary>Specialist answer kept per delegation (both for the lead and the transcript).</summary>
    private const int MaxResultChars = 6000;

    private readonly IProjectAgentService _projectAgent;
    private readonly IModelRouter _router;
    private readonly IAgentService _agents;
    private readonly IProjectDocsService _docs;

    public AgentOrchestrator(IProjectAgentService projectAgent, IModelRouter router, IAgentService agents, IProjectDocsService docs)
    {
        _projectAgent = projectAgent;
        _router = router;
        _agents = agents;
        _docs = docs;
    }

    /// <summary>
    /// Maintenance directive appended to the lead's system prompt — the lead is a main agent that owns the
    /// project handbook (.AI/AI_DOCS.md) via update_docs. Mirrors the single-agent directive.
    /// </summary>
    private const string DocsDirective =
        "\n\nYou maintain this project's handbook (.AI/AI_DOCS.md) with the update_docs tool: update it when a " +
        "durable rule, convention, architecture fact, or command changes — keep it concise and accurate. " +
        "It is rules, not a log (use memory for transient facts).";

    public async Task RunAsync(
        Agent lead,
        IChatClient leadClient,
        string leadModel,
        Project project,
        IReadOnlyList<ChatMessage> conversation,
        string memoryBlock,
        bool memoryEnabled,
        string projectSkills,
        string thinkingDirective,
        SoftwareInstallPermission installPermission,
        AgentApprovalMode approval,
        IProgress<string> status,
        Action<ActivityUpdate> onActivityStep,
        Action<string> onAnswer,
        Action<DelegationUpdate> onDelegation,
        Func<ToolApprovalRequest, Task<bool>> approve,
        CancellationToken ct)
    {
        // Roster the lead may delegate to: everyone EXCEPT orchestrators and the lead itself. Excluding
        // orchestrators is a hard rule — an orchestrator can never delegate to another (no nested orchestration).
        var roster = BuildRoster(_agents.ListAgents(project.Directory), lead);

        var leadTools = BuildLeadTools(lead.Tools ?? new AgentTools());

        var messages = new List<ChatMessage>
        {
            // The lead's persona + memory sit on top of the orchestrator's own coordination prompt. As a main
            // agent the lead owns the handbook, so the maintenance directive is always appended here.
            ChatMessage.System(
                AgentPromptBuilder.PersonaPrefix(lead, memoryBlock) +
                SystemPrompt(project, roster) +
                DocsDirective +
                thinkingDirective)
        };
        messages.AddRange(conversation);

        // Repeat guard: remember each (agent_id, task) pair already delegated so the lead can't trivially loop.
        var done = new Dictionary<string, string>(StringComparer.Ordinal);

        // The per-run delegation counter. Owned here (a single-element holder, since it's mutated from the
        // async DelegateAsync where a `ref` can't flow) so every Started/Activity/Finished of one delegation
        // shares the same Index; advanced only when a specialist actually runs.
        var nextIndex = new int[1];

        // 0-based counter for the LEAD's OWN structured steps (its read/scan tools + interim narration),
        // which feed the message's single-agent-style activity feed. Independent of the delegation index
        // space (nextIndex above), which keys the separate per-delegation cards.
        var leadActivityIndex = 0;

        for (var step = 0; step < MaxDelegations; step++)
        {
            ct.ThrowIfCancellationRequested();
            status.Report(step == 0 ? "Planning…" : "Coordinating…");

            var turn = await leadClient.ChatWithToolsAsync(leadModel, messages, leadTools, ct).ConfigureAwait(false);

            // No tool calls → the lead gave its final summary.
            if (turn.ToolCalls.Count == 0)
            {
                onAnswer(string.IsNullOrWhiteSpace(turn.Content) ? "_(no response)_" : turn.Content);
                return;
            }

            // Record the assistant turn (with its tool calls) so the lead sees its own request next round.
            messages.Add(new ChatMessage(ChatRole.Assistant, turn.Content) { ToolCalls = turn.ToolCalls });
            // The lead's interim narration → a note row in its structured feed.
            if (!string.IsNullOrWhiteSpace(turn.Content))
                onActivityStep(new ActivityUpdate(
                    ActivityPhase.Note, leadActivityIndex++, "", "", "", turn.Content.Trim(), false));

            foreach (var call in turn.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                string result;
                if (string.Equals(call.Name, "delegate_task", StringComparison.Ordinal))
                {
                    // Delegations render as their own per-delegation cards (not the lead's own feed).
                    result = await DelegateAsync(
                        call, lead, leadClient, leadModel, project, roster, memoryBlock, memoryEnabled,
                        projectSkills, thinkingDirective, installPermission, approval, done, nextIndex,
                        status, onDelegation, approve, ct).ConfigureAwait(false);
                }
                else
                {
                    // The lead's OWN read/scan/handbook step → a structured row in its feed (mirrors the
                    // single agent): Started before, Finished (with success/failure) after.
                    var (summary, detail) = DescribeLeadTool(call);
                    var idx = leadActivityIndex++;
                    onActivityStep(new ActivityUpdate(
                        ActivityPhase.Started, idx, ProjectAgentService.IconFor(call.Name), summary, detail, "", false));
                    status.Report(ProjectAgentService.CurrentActionLabel(call.Name, summary, detail));

                    result = ExecuteLeadTool(call, project);

                    onActivityStep(new ActivityUpdate(
                        ActivityPhase.Finished, idx, "", "", "", result, ProjectAgentService.IsFailure(result)));
                }

                messages.Add(new ChatMessage(ChatRole.Tool, result) { ToolName = call.Name });
            }
        }

        // Budget exhausted: force a wrap-up rather than spinning forever.
        onAnswer($"_(stopped after {MaxDelegations} delegations — the task may be unfinished)_");
    }

    // ---- the lead's own (non-delegation) tools ---------------------------------------------

    /// <summary>
    /// Runs one of the lead's OWN tools — read-only situational awareness (<c>list_directory</c>/
    /// <c>read_file</c>) plus the handbook writer (<c>update_docs</c>) — and returns the truncated result.
    /// The lead never writes/deletes/runs project files directly; <c>delegate_task</c> is handled separately
    /// in the loop (it has its own per-delegation card, not the lead's structured feed).
    /// </summary>
    private string ExecuteLeadTool(AgentToolCall call, Project project) => call.Name switch
    {
        "list_directory" => Truncate(ListDirectory(project, GetString(call.Arguments, "path"))),
        "read_file"      => Truncate(ReadFile(project, GetString(call.Arguments, "path"))),
        "update_docs"    => Truncate(UpdateDocs(project, GetString(call.Arguments, "content"))),
        _                => $"Unknown tool '{call.Name}'."
    };

    /// <summary>
    /// Summary + target for the lead's own structured activity row (mirrors the single agent's Describe).
    /// Widened from <c>private static</c> to <c>internal static</c> for testing (no logic change), mirroring
    /// how <c>IconFor</c>/<c>IsFailure</c>/<c>IsHandbookPath</c> are exposed via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal static (string Summary, string Detail) DescribeLeadTool(AgentToolCall call) => call.Name switch
    {
        "list_directory" => ("List directory", GetString(call.Arguments, "path") ?? "."),
        "read_file"      => ("Read file", GetString(call.Arguments, "path") ?? ""),
        "update_docs"    => ("Update project handbook", ".AI/" + ProjectDocsService.FileName),
        _                => (call.Name, "")
    };

    /// <summary>
    /// Runs the requested specialist agent for the given task via the existing project-agent loop, capturing
    /// its final answer and returning it to the lead as the tool result. Honors the specialist's own model and
    /// persona, but its <b>tools</b> are CAPPED to the lead's (the lead/specialist intersection,
    /// <see cref="CapTools"/>) and the run uses the single <b>global approval setting</b> passed in by the VM
    /// (not the lead's or specialist's), so a sub-agent can never use a tool the lead lacks, and the user's
    /// approval policy governs every delegated run.
    /// </summary>
    private async Task<string> DelegateAsync(
        AgentToolCall call, Agent lead, IChatClient leadClient, string leadModel, Project project,
        IReadOnlyList<Agent> roster, string memoryBlock, bool memoryEnabled, string projectSkills,
        string thinkingDirective, SoftwareInstallPermission installPermission, AgentApprovalMode approval,
        IDictionary<string, string> done, int[] nextIndex,
        IProgress<string> status, Action<DelegationUpdate> onDelegation,
        Func<ToolApprovalRequest, Task<bool>> approve, CancellationToken ct)
    {
        var agentId = (GetString(call.Arguments, "agent_id") ?? "").Trim();
        var task = (GetString(call.Arguments, "task") ?? "").Trim();

        if (task.Length == 0)
            return "Provide a non-empty 'task' brief describing what the specialist should do.";

        var specialist = roster.FirstOrDefault(a => string.Equals(a.Id, agentId, StringComparison.OrdinalIgnoreCase));
        if (specialist is null)
        {
            var available = roster.Count == 0 ? "(none)" : string.Join(", ", roster.Select(a => a.Id));
            return $"No agent '{agentId}'. Available: {available}";
        }

        // Repeat guard: refuse an exact (agent_id, task) re-run; remind the lead of the prior result instead.
        var key = DelegationKey(specialist.Id, task);
        if (done.TryGetValue(key, out var prior))
            return $"That subtask was already delegated to '{specialist.Id}' and produced:\n{prior}\n" +
                   "Do not repeat it — review this result and either delegate a different follow-up or finish.";

        // This delegation actually runs a specialist, so it owns the next index (consumed past the early
        // returns above). Every Started/Activity/Finished below shares this idx → one card in the UI.
        var idx = nextIndex[0]++;

        onDelegation(new DelegationUpdate(DelegationPhase.Started, idx, specialist.Name, specialist.Glyph, task, ""));
        status.Report($"Delegating to {specialist.Name}…");

        // The specialist's model: parse its "{provider}:{id}" preference; fall back to the lead's client+model.
        var (specialistClient, specialistModel) = ResolveSpecialistModel(specialist, leadClient, leadModel);

        // Approval is a run-level policy from the single global user setting (Settings → Autonomy & Memory),
        // passed in by the VM: the delegated run uses it for both the approval mode and the step budget — NOT
        // the lead's or specialist's own (there is no per-agent autonomy anymore). Tools, by contrast, stay a
        // ceiling (CapTools below) — a specialist still can't exceed what the lead is allowed to touch.
        var maxSteps = AutonomyMap.ForApprovalMode(approval).MaxSteps;

        // The specialist can't see this conversation — the brief must be self-contained (the lead is told so).
        var subConversation = new List<ChatMessage> { ChatMessage.User(task) };

        // Capture the specialist's final answer to return to the lead.
        var captured = new StringBuilder();
        void CaptureAnswer(string text) => captured.Append(text);

        // Route the specialist's STRUCTURED steps (tool calls + interim narration, in its own per-run index
        // space) into THIS delegation's card, which renders the same structured feed as a single-agent run.
        // No name-prefixing needed — the card header already identifies the specialist.
        void SpecialistStep(ActivityUpdate u) =>
            onDelegation(new DelegationUpdate(DelegationPhase.Activity, idx, specialist.Name, specialist.Glyph, task, "", u));

        try
        {
            await _projectAgent.RunAsync(
                specialistClient,
                project,
                specialistModel,
                subConversation,
                approval,
                maxSteps,
                // Tool ceiling: the specialist may do AT MOST what the lead is allowed (per-group intersection).
                // install_software stays double-gated by the global SoftwareInstallPermission passed below.
                CapTools(lead.Tools ?? new AgentTools(), specialist.Tools ?? new AgentTools()),
                AgentPromptBuilder.PersonaPrefix(specialist, memoryBlock),
                thinkingDirective,
                projectSkills,
                installPermission,
                specialist.MemoryEnabled && memoryEnabled,
                // Delegated specialists never get update_docs — only the main agent (the lead) owns the handbook.
                allowDocsUpdate: false,
                status,
                // The specialist's monospace narration is fully superseded by its structured steps (the Note
                // rows + tool rows below carry everything), so discard the legacy onActivity channel.
                _ => { },
                onActivityStep: SpecialistStep,
                CaptureAnswer,
                approve,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A specialist failure (e.g. an unreachable model) is returned to the lead as a tool result so it
            // can route around it (try another specialist) rather than aborting the whole coordinated run.
            // Not recorded in `done`, so a corrected re-delegation isn't blocked by the repeat guard.
            onDelegation(new DelegationUpdate(
                DelegationPhase.Finished, idx, specialist.Name, specialist.Glyph, task, $"⚠ Failed: {ex.Message}"));
            return $"Specialist '{specialist.Id}' failed: {ex.Message}. " +
                   "Consider delegating to a different specialist or adjusting the task.";
        }

        var answer = captured.ToString().Trim();
        if (answer.Length == 0)
            answer = "(the specialist returned no answer)";
        answer = Truncate(answer);

        onDelegation(new DelegationUpdate(DelegationPhase.Finished, idx, specialist.Name, specialist.Glyph, task, answer));

        done[key] = answer;
        return answer;
    }

    /// <summary>
    /// Resolves the specialist's chat client + model id from its <see cref="Agent.DefaultModel"/>
    /// ("{provider}:{id}"); falls back to the lead's client + model when it's unset or unparseable.
    /// </summary>
    private (IChatClient Client, string Model) ResolveSpecialistModel(Agent specialist, IChatClient leadClient, string leadModel)
    {
        var pref = specialist.DefaultModel;
        if (!string.IsNullOrWhiteSpace(pref))
        {
            var sep = pref!.IndexOf(':');
            if (sep > 0 && Enum.TryParse<AiProvider>(pref[..sep], ignoreCase: true, out var provider))
            {
                var id = pref[(sep + 1)..];
                if (!string.IsNullOrWhiteSpace(id))
                    return (_router.For(provider), id);
            }
        }
        return (leadClient, leadModel);
    }

    // ---- pure helpers (internal for unit tests) --------------------------------------------

    /// <summary>
    /// The set of agents a lead may delegate to: everyone EXCEPT other orchestrators and the lead itself.
    /// Excluding orchestrators is the hard no-nested-orchestration invariant — a lead can never delegate to
    /// another lead. The lead is matched by id, case-insensitively.
    /// </summary>
    internal static IReadOnlyList<Agent> BuildRoster(IReadOnlyList<Agent> all, Agent lead)
    {
        if (all is null || all.Count == 0)
            return System.Array.Empty<Agent>();
        return all
            .Where(a => !a.IsOrchestrator && !string.Equals(a.Id, lead.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// A compact catalog of the agents the lead may delegate to — one line per agent: id, name, a short
    /// description (or the first sentence of the persona), and a tools summary.
    /// </summary>
    internal static string BuildRosterCatalog(IReadOnlyList<Agent> roster)
    {
        if (roster is null || roster.Count == 0)
            return "(no specialist agents are available to delegate to)";

        var sb = new StringBuilder();
        foreach (var a in roster)
            sb.Append("- ").Append(a.Id).Append(" (").Append(a.Name).Append("): ")
              .Append(ShortDescription(a)).Append(". Can: ").Append(ToolsSummary(a.Tools))
              .Append(".\n");
        return sb.ToString().TrimEnd();
    }

    /// <summary>The agent's description, else the first sentence of its persona, else a neutral fallback.</summary>
    internal static string ShortDescription(Agent a)
    {
        if (!string.IsNullOrWhiteSpace(a.Description))
            return a.Description.Trim();

        var persona = (a.Persona ?? "").Trim();
        if (persona.Length == 0)
            return "general-purpose agent";

        var end = persona.IndexOf('.');
        var sentence = end > 0 ? persona[..end] : persona;
        return sentence.Trim();
    }

    /// <summary>A short "read / write / delete / run / install" summary of an agent's tool allow-list.</summary>
    internal static string ToolsSummary(AgentTools? tools)
    {
        var t = tools ?? new AgentTools();
        if (t.AllowAll)
            return "all tools (read, write, delete, run, install)";

        var parts = new List<string>();
        if (t.Allows(AgentToolGroup.ReadFiles)) parts.Add("read");
        if (t.Allows(AgentToolGroup.WriteFiles)) parts.Add("write");
        if (t.Allows(AgentToolGroup.DeleteFiles)) parts.Add("delete");
        if (t.Allows(AgentToolGroup.RunCommands)) parts.Add("run commands");
        if (t.Allows(AgentToolGroup.InstallSoftware)) parts.Add("install software");
        return parts.Count == 0 ? "answer only (no tools)" : string.Join(", ", parts);
    }

    /// <summary>Normalised key for the repeat guard: a delegated subtask is "the same" by (id, trimmed task) ignoring case.</summary>
    internal static string DelegationKey(string agentId, string task) =>
        (agentId ?? "").Trim().ToLowerInvariant() + " " + (task ?? "").Trim().ToLowerInvariant();

    // ---- delegation ceiling (the lead's permissions cap every sub-agent) -------------------

    /// <summary>
    /// Caps a specialist's requested tool allow-list to the lead's, so a delegated sub-agent may do AT MOST
    /// what the lead is allowed: each group is permitted only when BOTH the ceiling and the request allow it.
    /// Resolves through <see cref="AgentTools.Allows"/> (not the raw flags) so <c>AllowAll</c> on either side
    /// is handled, and returns an explicit (<c>AllowAll = false</c>) allow-list. Both args are null-safe.
    /// </summary>
    internal static AgentTools CapTools(AgentTools ceiling, AgentTools requested)
    {
        var cap = ceiling ?? new AgentTools();
        var req = requested ?? new AgentTools();
        return new AgentTools
        {
            AllowAll = false,
            ReadFiles = cap.Allows(AgentToolGroup.ReadFiles) && req.Allows(AgentToolGroup.ReadFiles),
            WriteFiles = cap.Allows(AgentToolGroup.WriteFiles) && req.Allows(AgentToolGroup.WriteFiles),
            DeleteFiles = cap.Allows(AgentToolGroup.DeleteFiles) && req.Allows(AgentToolGroup.DeleteFiles),
            RunCommands = cap.Allows(AgentToolGroup.RunCommands) && req.Allows(AgentToolGroup.RunCommands),
            InstallSoftware = cap.Allows(AgentToolGroup.InstallSoftware) && req.Allows(AgentToolGroup.InstallSoftware)
        };
    }

    // ---- read-only project tools (confined to the project directory) -----------------------

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
        try
        {
            if (!TryResolve(project.Directory, path, out var full, out var error))
                return error;
            if (!File.Exists(full))
                return $"File not found: {Rel(project, full)}";
            return File.ReadAllText(full);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Creates or overwrites the project handbook (.AI/AI_DOCS.md) with the lead's authored Markdown. Errors
    /// are caught here (the lead loop has no per-tool try/catch) and returned as the tool result so a failed
    /// write doesn't abort the coordination run.
    /// </summary>
    private string UpdateDocs(Project project, string? content)
    {
        content = (content ?? "").Trim();
        if (content.Length == 0)
            return "Provide non-empty 'content' — the COMPLETE new handbook markdown to write.";

        try
        {
            _docs.Save(project.Directory, content);
            return $"Updated the project handbook (.AI/{ProjectDocsService.FileName}) — {content.Length} chars. " +
                   "It loads as authoritative project guidance on the next turn.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

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

    // ---- formatting helpers ----------------------------------------------------------------

    private static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static string Truncate(string s) =>
        s.Length <= MaxResultChars ? s : s[..MaxResultChars] + "\n…(truncated)";

    // ---- lead tools + system prompt --------------------------------------------------------

    /// <summary>
    /// The lead's <b>direct</b> tools: the always-present <c>delegate_task</c> plus the read-only
    /// <c>list_directory</c> / <c>read_file</c> for situational awareness (gated by the lead's
    /// <see cref="AgentToolGroup.ReadFiles"/> permission). The lead never writes/deletes/runs directly — its
    /// broader <see cref="AgentTools"/> allow-list serves only as the delegation ceiling for its specialists.
    /// </summary>
    private static IReadOnlyList<AgentTool> BuildLeadTools(AgentTools leadTools)
    {
        static JsonElement Schema(object o) => JsonSerializer.SerializeToElement(o);

        var tools = new List<AgentTool>
        {
            new("delegate_task",
                "Assign a self-contained subtask to a specialist agent from your team. The specialist runs " +
                "with its own tools/persona in the same project and returns its final result to you. The " +
                "specialist CANNOT see this conversation, so include all needed context and file paths in 'task'.",
                Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        agent_id = new { type = "string", description = "The id of the specialist agent to delegate to (from the team roster)." },
                        task = new { type = "string", description = "A complete, self-contained brief of what the specialist should do, with all needed context and paths." }
                    },
                    required = new[] { "agent_id", "task" }
                })),
            // The lead is a main agent: it owns the project handbook. update_docs writes only to the fixed
            // .AI/AI_DOCS.md path (so it isn't allow-list-gated) and is shared verbatim with the single agent.
            new("update_docs", ProjectAgentService.UpdateDocsToolDescription, ProjectAgentService.UpdateDocsSchema())
        };

        if (leadTools.Allows(AgentToolGroup.ReadFiles))
        {
            tools.Add(new("list_directory",
                "List the files and sub-folders of a directory in the project (path defaults to the root). " +
                "Use this to scope the project before planning who to delegate to.",
                Schema(new
                {
                    type = "object",
                    properties = new { path = new { type = "string", description = "Directory path relative to the project root. Omit or \".\" for the root." } }
                })));
            tools.Add(new("read_file",
                "Read and return the full text of a file in the project (read-only situational awareness).",
                Schema(new
                {
                    type = "object",
                    properties = new { path = new { type = "string", description = "Path relative to the project root." } },
                    required = new[] { "path" }
                })));
        }

        return tools;
    }

    private static string SystemPrompt(Project project, IReadOnlyList<Agent> roster) => $"""
        You are the lead coordinating a team of specialist agents to complete work on the project "{project.Name}":
          {project.Directory}

        You do NOT do the hands-on work yourself. Instead you plan, delegate, review, and iterate. Rules:
        - Optionally inspect the project first with list_directory / read_file to scope the work.
        - Break the goal into subtasks and delegate each with delegate_task(agent_id, task) to the best-fit
          specialist below. Each specialist CANNOT see this conversation — write a self-contained brief in
          'task' that includes the relevant file paths, context, and the exact expected outcome.
        - Review each specialist's returned result and delegate follow-ups as needed (for example, have a
          reviewer or tester check an implementer's work). Do not re-delegate an identical subtask.
        - Be efficient: delegate only what is necessary; don't over-delegate or fragment trivial work.
        - Your TOOL permissions are the team's CEILING: each specialist you delegate to is capped to YOUR OWN tool
          allow-list (it can do at most what you can — it can't write/delete/run/install unless you're allowed
          that too). The approval policy is a single global user setting (Settings → Autonomy & Memory) that
          governs the whole run — you don't set it. Plan within your tool limits.
        - When the goal is complete, STOP calling tools and reply with a short plain-text summary of what the
          team accomplished. (A reply with no tool call is treated as your final answer.)

        Your team (delegate by id):
        {BuildRosterCatalog(roster)}
        """;
}
