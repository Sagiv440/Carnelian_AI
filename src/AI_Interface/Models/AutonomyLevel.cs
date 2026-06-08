namespace AI_Interface.Models;

/// <summary>
/// How much an agent may act on its own. The active agent's level is authoritative for a project-agent
/// run: it maps onto the loop's approval mode and step budget (<see cref="AutonomyMap.ForRun"/>) and adds
/// an optional plan-then-execute directive (<c>AgentPromptBuilder.PlanningDirective</c>). Software-install
/// permission stays an independent gate on top of this.
/// </summary>
public enum AutonomyLevel
{
    /// <summary>Confirm every action; smallest step budget.</summary>
    Ask,

    /// <summary>Confirm only destructive actions (today's default behaviour).</summary>
    Guided,

    /// <summary>Auto-run within the agent's tool allow-list and sandbox; larger step budget.</summary>
    Autonomous
}

/// <summary>
/// Maps an <see cref="AutonomyLevel"/> onto the project-agent loop's run knobs (approval mode + step
/// budget). Planning is handled separately as a prompt directive (<c>AgentPromptBuilder.PlanningDirective</c>).
/// </summary>
public static class AutonomyMap
{
    /// <summary>Step budget for <see cref="AutonomyLevel.Guided"/> — matches the project agent's historical default.</summary>
    public const int GuidedSteps = 24;

    /// <summary>
    /// The effective approval mode and step budget for a run driven by the given autonomy level:
    /// <list type="bullet">
    /// <item>Ask → ConfirmEverything, 8 steps.</item>
    /// <item>Guided → ConfirmDestructive, 24 steps (today's behaviour).</item>
    /// <item>Autonomous → AutoRun, 40 steps.</item>
    /// </list>
    /// </summary>
    public static (AgentApprovalMode Approval, int MaxSteps) ForRun(AutonomyLevel level) => level switch
    {
        AutonomyLevel.Ask        => (AgentApprovalMode.ConfirmEverything, 8),
        AutonomyLevel.Autonomous => (AgentApprovalMode.AutoRun, 40),
        _                        => (AgentApprovalMode.ConfirmDestructive, GuidedSteps)
    };

    /// <summary>The autonomy level that corresponds to a global approval mode (used to seed new-agent defaults).</summary>
    public static AutonomyLevel FromApprovalMode(AgentApprovalMode mode) => mode switch
    {
        AgentApprovalMode.ConfirmEverything => AutonomyLevel.Ask,
        AgentApprovalMode.AutoRun           => AutonomyLevel.Autonomous,
        _                                   => AutonomyLevel.Guided
    };
}
