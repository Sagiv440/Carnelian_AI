using System.Collections.Generic;
using System.Linq;
using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// Pure helpers that shape the top-bar agent picker for Project mode (Phase: prefer "team" agents).
/// In a project the picker leads with the coordinated "team" experience: orchestrators are sorted to the
/// top and, optionally, single agents are hidden entirely. The Lead's delegation roster is unaffected —
/// it comes from <see cref="Services.IAgentService.ListAgents"/>, not this collection — so hiding single
/// agents here never stops the Lead delegating to them. <c>internal static</c> for unit testing.
/// </summary>
internal static class ProjectAgentPicker
{
    /// <summary>The built-in Lead orchestrator's id (preferred auto-selection in Project mode).</summary>
    private const string LeadId = "lead";

    /// <summary>
    /// The Project-mode picker contents for <paramref name="agents"/>: when <paramref name="teamOnly"/>,
    /// keep only orchestrators; then stable-sort orchestrators first (relative order preserved within each
    /// group). Returns a new list; the input is not mutated. Null-safe (null ⇒ empty).
    /// </summary>
    public static IReadOnlyList<Agent> Arrange(IReadOnlyList<Agent>? agents, bool teamOnly)
    {
        if (agents is null || agents.Count == 0)
            return System.Array.Empty<Agent>();

        IEnumerable<Agent> source = agents;
        if (teamOnly)
            source = source.Where(a => a.IsOrchestrator);

        // OrderBy is a stable sort in .NET, so orchestrators keep their relative order, then non-orchestrators.
        return source.OrderBy(a => a.IsOrchestrator ? 0 : 1).ToList();
    }

    /// <summary>
    /// The orchestrator to auto-select when entering a project: the built-in Lead (<c>id == "lead"</c>) if
    /// present, else the first orchestrator in order, else <c>null</c> (no orchestrator available).
    /// </summary>
    public static Agent? PreferredOrchestrator(IReadOnlyList<Agent>? agents)
    {
        if (agents is null || agents.Count == 0)
            return null;

        return agents.FirstOrDefault(a => a.IsOrchestrator &&
                   string.Equals(a.Id, LeadId, System.StringComparison.OrdinalIgnoreCase))
               ?? agents.FirstOrDefault(a => a.IsOrchestrator);
    }
}
