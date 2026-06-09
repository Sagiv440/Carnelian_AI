namespace AI_Interface.Models;

/// <summary>How the project agent's tool calls are gated before they run.</summary>
public enum AgentApprovalMode
{
    /// <summary>Run every tool call automatically; each action is still logged in the transcript.</summary>
    AutoRun,

    /// <summary>Auto-run read-only tools, but ask before anything that writes, deletes, or runs a command.</summary>
    ConfirmDestructive,

    /// <summary>Ask before every tool call, including read-only ones.</summary>
    ConfirmEverything
}

/// <summary>
/// Maps the single global <see cref="AgentApprovalMode"/> onto the project-agent loop's run knobs
/// (approval mode + step budget). This is the authoritative source of approval policy + step budget for
/// every project-agent run — both the single-agent path and Lead/orchestrator-delegated runs. Planning is
/// handled separately as a prompt directive (<c>AgentPromptBuilder.PlanningDirective</c>).
/// </summary>
public static class AutonomyMap
{
    /// <summary>Step budget for <see cref="AgentApprovalMode.ConfirmDestructive"/> — the project agent's historical default.</summary>
    public const int GuidedSteps = 24;

    /// <summary>Effective approval mode + step budget for a run, from the global approval setting.</summary>
    public static (AgentApprovalMode Approval, int MaxSteps) ForApprovalMode(AgentApprovalMode mode) => mode switch
    {
        AgentApprovalMode.ConfirmEverything => (AgentApprovalMode.ConfirmEverything, 8),
        AgentApprovalMode.AutoRun           => (AgentApprovalMode.AutoRun, 40),
        _                                   => (AgentApprovalMode.ConfirmDestructive, GuidedSteps)
    };
}
