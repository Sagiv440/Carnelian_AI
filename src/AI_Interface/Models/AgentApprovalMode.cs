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
