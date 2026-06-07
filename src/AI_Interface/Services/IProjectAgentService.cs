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
    /// <param name="project">The active project (name + sandbox directory).</param>
    /// <param name="model">Ollama model to use (must support tool calling).</param>
    /// <param name="conversation">Prior user/assistant turns; the agent prepends its own system prompt.</param>
    /// <param name="approvalMode">Whether/when to ask the user before a tool runs.</param>
    /// <param name="thinkingDirective">Extra planning instruction appended to the system prompt (empty = off).</param>
    /// <param name="projectSkills">Project skill files appended to the system prompt (empty = none).</param>
    /// <param name="allowSoftwareInstall">When true, the agent may install software machine-wide.</param>
    /// <param name="status">Step progress (constructed on the UI thread, so it auto-marshals).</param>
    /// <param name="onDelta">Receives transcript text (action log + final answer). Must marshal to the UI thread.</param>
    /// <param name="approve">Asked to approve a single tool call; returns false to skip it.</param>
    Task RunAsync(
        Project project,
        string model,
        IReadOnlyList<ChatMessage> conversation,
        AgentApprovalMode approvalMode,
        string thinkingDirective,
        string projectSkills,
        bool allowSoftwareInstall,
        IProgress<string> status,
        Action<string> onDelta,
        Func<ToolApprovalRequest, Task<bool>> approve,
        CancellationToken ct);
}
