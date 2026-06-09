using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AI_Interface.Models;

/// <summary>
/// An agent profile: a persona (and, in later phases, a skill set, tool allow-list, autonomy level,
/// and memory) that parameterizes the existing prompt-build + tool loop rather than forking it.
/// Serialized to JSON (global agents in app-data, project agents under <c>.AI/agents</c>); built-ins
/// are an embedded read-only seed. In Phase 1 only <see cref="Persona"/> is wired into behaviour; the
/// other fields are carried for forward-compatibility.
/// </summary>
public sealed class Agent
{
    /// <summary>Stable slug (built-ins) or generated id (customs); identifies the agent across sources.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name, e.g. "Researcher", "Code Buddy".</summary>
    public string Name { get; set; } = "";

    /// <summary>Avatar/emoji shown in the picker and transcript header.</summary>
    public string Glyph { get; set; } = "🤖";

    /// <summary>Short "when to use this agent" summary (the Markdown frontmatter <c>description</c>; optional).</summary>
    public string Description { get; set; } = "";

    /// <summary>Personality / system-prompt text (tone, expertise) injected into every mode's system prompt.
    /// Stored as the Markdown <b>body</b> of the agent file.</summary>
    public string Persona { get; set; } = "";

    /// <summary>Optional preferred model "{provider}:{id}"; just pre-selects one — agent and model stay independent.</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Skill-pack ids and project SKILL names (Phase 2 — persisted only for now).</summary>
    public List<string> Skills { get; set; } = new();

    /// <summary>Per-tool allow-list (Phase 2 — persisted only for now).</summary>
    public AgentTools Tools { get; set; } = new();

    /// <summary>
    /// Autonomy level. Authoritative for a project-agent run: it sets the effective approval mode + step
    /// budget (<see cref="AutonomyMap.ForRun"/>) and, for <see cref="AutonomyLevel.Autonomous"/>, adds a
    /// plan-then-execute directive. Software-install permission stays an independent gate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AutonomyLevel Autonomy { get; set; } = AutonomyLevel.Guided;

    /// <summary>Whether persistent memory is enabled for this agent (Phase 4 — persisted only for now).</summary>
    public bool MemoryEnabled { get; set; } = true;

    /// <summary>Whether the agent ends turns with suggested next steps (Phase 5 — persisted only for now).</summary>
    public bool Proactive { get; set; }

    /// <summary>
    /// When true this agent is a <b>lead/orchestrator</b>: in Project mode it doesn't do the work in a
    /// single tool loop — instead it reads the roster and <c>delegate_task</c>s subtasks to specialist
    /// agents (the "agents as tools" pattern, run by <c>AgentOrchestrator</c>). An orchestrator can never
    /// delegate to another orchestrator (no nested orchestration). The built-in <i>Lead</i> sets this.
    /// </summary>
    public bool IsOrchestrator { get; set; }

    /// <summary>Where the agent comes from / persists. Built-ins are read-only.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentScope Scope { get; set; } = AgentScope.Global;

    /// <summary>True for embedded roster entries (read-only; never written to disk).</summary>
    public bool IsBuiltIn { get; set; }
}
