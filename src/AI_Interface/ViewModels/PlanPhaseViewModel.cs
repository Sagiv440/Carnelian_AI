using System.Collections.ObjectModel;
using AI_Interface.Models;

namespace AI_Interface.ViewModels;

/// <summary>
/// One named phase in the agent's plan (see <see cref="MessageViewModel.Phases"/>): a title, its status, and
/// the checklist steps within it. Like <see cref="PlanStepViewModel"/> it's rebuilt wholesale on each update
/// (the agent resends the full plan), so the steps live in a fresh collection per rebuild.
/// </summary>
public sealed class PlanPhaseViewModel
{
    public string Name { get; init; } = "";
    public PlanStepStatus Status { get; init; }

    public ObservableCollection<PlanStepViewModel> Steps { get; } = new();

    /// <summary>Phase glyph: ☑ done, ▶ active, ☐ pending (matches the step glyphs).</summary>
    public string Glyph => Status switch
    {
        PlanStepStatus.Done => "☑",
        PlanStepStatus.Active => "▶",
        _ => "☐"
    };

    /// <summary>A finished phase renders muted/struck-through.</summary>
    public bool IsDone => Status == PlanStepStatus.Done;

    /// <summary>The active phase renders emphasised.</summary>
    public bool IsActive => Status == PlanStepStatus.Active;
}
