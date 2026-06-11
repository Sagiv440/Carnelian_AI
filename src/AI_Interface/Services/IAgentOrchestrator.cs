using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Runs a <b>lead / orchestrator</b> agent in Project mode: a tool-calling loop (modelled on
/// <see cref="IProjectAgentService"/>) whose main tool, <c>delegate_task</c>, runs a <i>nested</i>
/// specialist agent via the existing <see cref="IProjectAgentService"/> and feeds its final answer back
/// to the lead. The lead also has read-only <c>list_directory</c>/<c>read_file</c> tools to scope the
/// project before planning. This is the "agents as tools" pattern: the lead coordinates a team rather
/// than doing the work in one loop. A lead can never delegate to another orchestrator (no nesting).
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Drives the lead agent to completion for one user turn: plan → delegate subtasks → review → repeat
    /// until it replies in plain text (the final summary).
    /// </summary>
    /// <param name="lead">The active orchestrator agent (its persona and read tools drive the lead loop; tool permissions are the team's ceiling).</param>
    /// <param name="leadClient">The chat client serving the lead's model (must support tool calling).</param>
    /// <param name="leadModel">Model id for the lead loop (must support tool calling).</param>
    /// <param name="project">The active project (name + sandbox directory) shared by lead and specialists.</param>
    /// <param name="conversation">Prior user/assistant turns; the lead prepends its own system prompt.</param>
    /// <param name="memoryBlock">The persistent-memory context block (empty = off), threaded into personas.</param>
    /// <param name="memoryEnabled">Whether persistent memory is active this turn (gates a specialist's <c>remember</c> tool).</param>
    /// <param name="projectSkills">Project skill files appended to a specialist's system prompt (empty = none).</param>
    /// <param name="thinkingDirective">Extra planning instruction appended to system prompts (empty = off).</param>
    /// <param name="installPermission">Whether/how a delegated specialist may install software machine-wide.</param>
    /// <param name="approval">The single global approval setting that governs the lead loop and every delegated run.</param>
    /// <param name="status">Step progress (constructed on the UI thread, so it auto-marshals).</param>
    /// <param name="onActivityStep">
    /// Receives the LEAD's own structured steps — its read/scan tool calls (Started/Finished, keyed by a
    /// 0-based index) plus its interim narration (Note) — rendered as the message's structured activity feed,
    /// the same one a single-agent run uses. Must marshal to the UI thread.
    /// </param>
    /// <param name="onAnswer">Receives the lead's final plain-text summary. Must marshal to the UI thread.</param>
    /// <param name="onDelegation">
    /// Receives structured per-delegation updates (start/activity/finish, keyed by a 0-based index) so the UI
    /// can render a hierarchical per-delegation card list. Must marshal to the UI thread.
    /// </param>
    /// <param name="onPlan">
    /// Receives the LEAD's plan/phases (its <c>update_plan</c> tool) for the message's plan card; the Lead
    /// owns the phase plan. Delegated specialists' plans are suppressed. Must marshal to the UI thread.
    /// </param>
    /// <param name="approve">Asked to approve a single specialist tool call; returns false to skip it.</param>
    /// <param name="autoFlowPhases">When the lead works in phases: true advances automatically; false pauses at each phase boundary via <paramref name="phaseGate"/>.</param>
    /// <param name="phaseGate">Asked to continue past a phase boundary when <paramref name="autoFlowPhases"/> is false; returns false to stop the run.</param>
    Task RunAsync(
        Agent lead,
        IChatClient leadClient,
        string leadModel,
        Project project,
        IReadOnlyList<ChatMessage> conversation,
        string memoryBlock,
        bool memoryEnabled,
        string projectSkills,
        string thinkingDirective,
        SoftwareInstallPermission installPermission,
        AgentApprovalMode approval,
        IProgress<string> status,
        Action<ActivityUpdate> onActivityStep,
        Action<string> onAnswer,
        Action<DelegationUpdate> onDelegation,
        Action<PlanUpdate>? onPlan,
        Func<ToolApprovalRequest, Task<bool>> approve,
        bool autoFlowPhases,
        Func<PhaseGate, Task<bool>>? phaseGate,
        CancellationToken ct);
}
