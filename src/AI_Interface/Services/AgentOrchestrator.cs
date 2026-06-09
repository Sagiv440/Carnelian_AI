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
/// runs the EXISTING <see cref="IProjectAgentService.RunAsync"/> for that specialist (its own persona,
/// tools, autonomy, and model), capturing the specialist's final answer via the <c>onAnswer</c> callback
/// and returning it to the lead as the tool result. When the lead replies with no tool calls, that text
/// is the final summary — the same finish convention as the single-agent loop.
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

    public AgentOrchestrator(IProjectAgentService projectAgent, IModelRouter router, IAgentService agents)
    {
        _projectAgent = projectAgent;
        _router = router;
        _agents = agents;
    }

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
        IProgress<string> status,
        Action<string> onActivity,
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
            // The lead's persona + memory sit on top of the orchestrator's own coordination prompt.
            ChatMessage.System(
                AgentPromptBuilder.PersonaPrefix(lead, memoryBlock) +
                SystemPrompt(project, roster) +
                thinkingDirective)
        };
        messages.AddRange(conversation);

        // Repeat guard: remember each (agent_id, task) pair already delegated so the lead can't trivially loop.
        var done = new Dictionary<string, string>(StringComparer.Ordinal);

        // The per-run delegation counter. Owned here (a single-element holder, since it's mutated from the
        // async DelegateAsync where a `ref` can't flow) so every Started/Activity/Finished of one delegation
        // shares the same Index; advanced only when a specialist actually runs.
        var nextIndex = new int[1];

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
            if (!string.IsNullOrWhiteSpace(turn.Content))
                onActivity(turn.Content + "\n");

            foreach (var call in turn.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                var result = await ExecuteAsync(
                    call, lead, leadClient, leadModel, project, roster, memoryBlock, memoryEnabled,
                    projectSkills, thinkingDirective, installPermission, done, nextIndex,
                    status, onActivity, onDelegation, approve, ct).ConfigureAwait(false);
                messages.Add(new ChatMessage(ChatRole.Tool, result) { ToolName = call.Name });
            }
        }

        // Budget exhausted: force a wrap-up rather than spinning forever.
        onAnswer($"_(stopped after {MaxDelegations} delegations — the task may be unfinished)_");
    }

    // ---- the lead loop's single-tool step --------------------------------------------------

    private async Task<string> ExecuteAsync(
        AgentToolCall call, Agent lead, IChatClient leadClient, string leadModel, Project project,
        IReadOnlyList<Agent> roster, string memoryBlock, bool memoryEnabled, string projectSkills,
        string thinkingDirective, SoftwareInstallPermission installPermission,
        IDictionary<string, string> done, int[] nextIndex,
        IProgress<string> status, Action<string> onActivity, Action<DelegationUpdate> onDelegation,
        Func<ToolApprovalRequest, Task<bool>> approve, CancellationToken ct)
    {
        switch (call.Name)
        {
            case "list_directory":
            {
                var path = GetString(call.Arguments, "path");
                onActivity($"\n🔧 List directory  `{path ?? "."}`\n");
                var result = ListDirectory(project, path);
                onActivity(IndentForDisplay(result) + "\n");
                return Truncate(result);
            }
            case "read_file":
            {
                var path = GetString(call.Arguments, "path");
                onActivity($"\n🔧 Read file  `{path ?? ""}`\n");
                var result = ReadFile(project, path);
                onActivity(IndentForDisplay(result) + "\n");
                return Truncate(result);
            }
            case "delegate_task":
                return await DelegateAsync(
                    call, lead, leadClient, leadModel, project, roster, memoryBlock, memoryEnabled,
                    projectSkills, thinkingDirective, installPermission, done, nextIndex,
                    status, onActivity, onDelegation, approve, ct).ConfigureAwait(false);
            default:
                return $"Unknown tool '{call.Name}'.";
        }
    }

    /// <summary>
    /// Runs the requested specialist agent for the given task via the existing project-agent loop, capturing
    /// its final answer and returning it to the lead as the tool result. Honors the specialist's own model,
    /// persona, tool allow-list, and autonomy (approval mode + step budget).
    /// </summary>
    private async Task<string> DelegateAsync(
        AgentToolCall call, Agent lead, IChatClient leadClient, string leadModel, Project project,
        IReadOnlyList<Agent> roster, string memoryBlock, bool memoryEnabled, string projectSkills,
        string thinkingDirective, SoftwareInstallPermission installPermission,
        IDictionary<string, string> done, int[] nextIndex,
        IProgress<string> status, Action<string> onActivity, Action<DelegationUpdate> onDelegation,
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

        // The specialist's autonomy drives its own approval mode + step budget.
        var (approval, maxSteps) = AutonomyMap.ForRun(specialist.Autonomy);

        // The specialist can't see this conversation — the brief must be self-contained (the lead is told so).
        var subConversation = new List<ChatMessage> { ChatMessage.User(task) };

        // Capture the specialist's final answer to return to the lead.
        var captured = new StringBuilder();
        void CaptureAnswer(string text) => captured.Append(text);

        // Route the specialist's activity into THIS delegation's card (no name-prefixing needed — the card
        // header already identifies the specialist).
        void SpecialistActivity(string line) =>
            onDelegation(new DelegationUpdate(DelegationPhase.Activity, idx, specialist.Name, specialist.Glyph, task, line));

        try
        {
            await _projectAgent.RunAsync(
                specialistClient,
                project,
                specialistModel,
                subConversation,
                approval,
                maxSteps,
                specialist.Tools ?? new AgentTools(),
                AgentPromptBuilder.PersonaPrefix(specialist, memoryBlock),
                thinkingDirective,
                projectSkills,
                installPermission,
                specialist.MemoryEnabled && memoryEnabled,
                status,
                SpecialistActivity,
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
    /// description (or the first sentence of the persona), a tools summary, and the autonomy level.
    /// </summary>
    internal static string BuildRosterCatalog(IReadOnlyList<Agent> roster)
    {
        if (roster is null || roster.Count == 0)
            return "(no specialist agents are available to delegate to)";

        var sb = new StringBuilder();
        foreach (var a in roster)
            sb.Append("- ").Append(a.Id).Append(" (").Append(a.Name).Append("): ")
              .Append(ShortDescription(a)).Append(". Can: ").Append(ToolsSummary(a.Tools))
              .Append(". Autonomy: ").Append(a.Autonomy).Append(".\n");
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

    /// <summary>Indents a tool result two spaces per line for a readable inline transcript log.</summary>
    private static string IndentForDisplay(string s)
    {
        var trimmed = Truncate(s.TrimEnd());
        var lines = trimmed.Split('\n');
        return string.Join('\n', lines.Select(l => "   " + l));
    }

    // ---- lead tools + system prompt --------------------------------------------------------

    /// <summary>
    /// The lead's tools: the always-present <c>delegate_task</c> plus the read-only <c>list_directory</c> /
    /// <c>read_file</c> for situational awareness — those two are gated by the lead's own
    /// <see cref="AgentTools"/> allow-list (the built-in Lead is read-only, so they're offered).
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
                }))
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
        - When the goal is complete, STOP calling tools and reply with a short plain-text summary of what the
          team accomplished. (A reply with no tool call is treated as your final answer.)

        Your team (delegate by id):
        {BuildRosterCatalog(roster)}
        """;
}
