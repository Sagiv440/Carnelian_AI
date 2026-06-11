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

/// <summary>
/// One named phase of work (a level above <see cref="PlanStep"/>): a title, its status (Active = the phase
/// being worked now, by convention one at a time), and the checklist steps within it.
/// </summary>
public sealed record PlanPhase(string Name, PlanStepStatus Status, IReadOnlyList<PlanStep> Steps);

/// <summary>
/// The agent's full current plan, resent in whole on every <c>update_plan</c> call. A task is either a flat
/// checklist (<see cref="Steps"/>) or organised into named <see cref="Phases"/> — the UI renders phases when
/// present, else the flat list. Both are never populated at once.
/// </summary>
public sealed record PlanUpdate(IReadOnlyList<PlanStep> Steps, IReadOnlyList<PlanPhase> Phases)
{
    /// <summary>Flat-checklist convenience (no phases) — keeps existing callers and tests compiling.</summary>
    public PlanUpdate(IReadOnlyList<PlanStep> steps) : this(steps, System.Array.Empty<PlanPhase>()) { }
}
