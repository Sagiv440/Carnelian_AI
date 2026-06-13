using System.Collections.Generic;
using System.Text.Json;

namespace AI_Interface.Models;

// Domain-level abstractions for the project agent's tool-use loop. These stay free of the Ollama
// wire format (see OllamaDtos.cs); OllamaClient maps between the two.

/// <summary>A tool offered to the model: a name, a description, and a JSON-schema parameter object.</summary>
public sealed record AgentTool(string Name, string Description, JsonElement Parameters);

/// <summary>A tool invocation the model asked for. <paramref name="Arguments"/> is the raw JSON object.</summary>
public sealed record AgentToolCall(string Name, JsonElement Arguments);

/// <summary>One model turn during the agent loop: free-text content plus any tool calls it requested.</summary>
public sealed record AgentTurn(string Content, IReadOnlyList<AgentToolCall> ToolCalls);

/// <summary>A request for the user to approve (or deny) a single tool call before it executes.</summary>
public sealed record ToolApprovalRequest(string ToolName, string Summary, string Detail, bool IsDestructive);

/// <summary>
/// A request to continue past a phase boundary (when <c>AutoFlowPhases</c> is off): the agent just finished
/// <paramref name="CompletedPhase"/> and is about to start <paramref name="NextPhase"/>. The user approves
/// (true) to continue or declines (false) to stop the run.
/// </summary>
public sealed record PhaseGate(string CompletedPhase, string NextPhase);

/// <summary>One question in a clarification request: the question text plus a few concrete options to pick from.</summary>
public sealed record ClarificationQuestion(string Question, IReadOnlyList<string> Options);

/// <summary>
/// A clarification the agent asks the user mid-run (the <c>ask_user</c> tool): one or more
/// <paramref name="Questions"/>. The view shows a popup — a tab per question when there's more than one —
/// each with checkboxes + an "Other" free-text field; the combined answer is returned (null = dismissed).
/// </summary>
public sealed record UserClarificationRequest(IReadOnlyList<ClarificationQuestion> Questions)
{
    /// <summary>Single-question convenience (one subject).</summary>
    public UserClarificationRequest(string question, IReadOnlyList<string> options)
        : this(new[] { new ClarificationQuestion(question, options) }) { }
}
