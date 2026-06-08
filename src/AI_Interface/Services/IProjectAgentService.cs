using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Runs the project agent: an Ollama tool-use loop whose tools create/delete files and folders and
/// run terminal commands, all confined to the project's directory.
/// </summary>
public interface IProjectAgentService
{
    /// <summary>
    /// Drives the agent to completion for one user turn.
    /// </summary>
    /// <param name="client">The chat client serving the chosen model (resolved by the model router; must support tool calling).</param>
    /// <param name="project">The active project (name + sandbox directory).</param>
    /// <param name="model">Model id to use (must support tool calling).</param>
    /// <param name="conversation">Prior user/assistant turns; the agent prepends its own system prompt.</param>
    /// <param name="approvalMode">Whether/when to ask the user before a tool runs (derived from the active agent's autonomy).</param>
    /// <param name="maxSteps">Hard cap on tool-use rounds (derived from the active agent's autonomy; ≤0 falls back to the default).</param>
    /// <param name="allowedTools">The active agent's per-tool allow-list; only permitted tools are advertised (null = unrestricted).</param>
    /// <param name="personaPrefix">The active agent's persona, prepended to the system prompt (empty = none).</param>
    /// <param name="thinkingDirective">Extra planning instruction appended to the system prompt (empty = off).</param>
    /// <param name="projectSkills">Project skill files appended to the system prompt (empty = none).</param>
    /// <param name="installPermission">Whether/how the agent may install software machine-wide.</param>
    /// <param name="status">Step progress (constructed on the UI thread, so it auto-marshals).</param>
    /// <param name="onActivity">Receives the action log / intermediate reasoning (the "work"). Must marshal to the UI thread.</param>
    /// <param name="onAnswer">Receives the final plain-text answer. Must marshal to the UI thread.</param>
    /// <param name="approve">Asked to approve a single tool call; returns false to skip it.</param>
    Task RunAsync(
        IChatClient client,
        Project project,
        string model,
        IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode,
        int maxSteps,
        AgentTools allowedTools,
        string personaPrefix,
        string thinkingDirective,
        string projectSkills,
        SoftwareInstallPermission installPermission,
        IProgress<string> status,
        Action<string> onActivity,
        Action<string> onAnswer,
        Func<ToolApprovalRequest, Task<bool>> approve,
        CancellationToken ct);
}
