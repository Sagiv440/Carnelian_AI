using System.Collections.Generic;

namespace AI_Interface.Models;

// A lightweight checklist the project agent maintains via the update_plan tool. The agent resends the FULL
// ordered list on each call (like a todo list), so the UI replaces its plan wholesale per update.

/// <summary>State of one plan step.</summary>
public enum PlanStepStatus
{
    /// <summary>Not started yet.</summary>
    Pending,

    /// <summary>The step the agent is working on now (exactly one, by convention).</summary>
    Active,

    /// <summary>Finished.</summary>
    Done
}

/// <summary>One step in the agent's plan: a short description plus its status.</summary>
public sealed record PlanStep(string Text, PlanStepStatus Status);

/// <summary>The agent's full current plan (the complete ordered step list, resent on every update_plan call).</summary>
public sealed record PlanUpdate(IReadOnlyList<PlanStep> Steps);
