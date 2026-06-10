using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// One row of the agent's plan checklist (see <see cref="MessageViewModel.Plan"/>). Immutable — the agent
/// resends the whole plan on each update, so the VM rebuilds the rows rather than mutating them in place.
/// </summary>
public sealed class PlanStepViewModel
{
    public string Text { get; init; } = "";
    public PlanStepStatus Status { get; init; }

    /// <summary>Checkbox-style glyph: ☑ done, ▶ active, ☐ pending.</summary>
    public string Glyph => Status switch
    {
        PlanStepStatus.Done => "☑",
        PlanStepStatus.Active => "▶",
        _ => "☐"
    };

    /// <summary>Done steps render struck-through + muted.</summary>
    public bool IsDone => Status == PlanStepStatus.Done;

    /// <summary>The active step renders emphasised.</summary>
    public bool IsActive => Status == PlanStepStatus.Active;
}
