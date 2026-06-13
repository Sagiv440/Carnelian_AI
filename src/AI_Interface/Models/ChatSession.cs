using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AI_Interface.Models;

/// <summary>One persisted turn of a conversation (role + text, plus web sources and the agent activity log).</summary>
public sealed class ChatTurn
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChatRole Role { get; set; }

    public string Text { get; set; } = "";

    /// <summary>Model that produced this turn (assistant turns only); restores the header on reopen.</summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Web sources backing this turn (Web Search / Deep Research). Restored as the clickable "Sources"
    /// list on reopen. Page <see cref="SearchResult.Content"/> is intentionally not persisted (too large).
    /// </summary>
    public List<SearchResult>? Sources { get; set; }

    /// <summary>
    /// A Thinking turn's reasoning (or, for an older saved chat, the flattened agent activity log). Restored
    /// into the message's monospace "Activity" disclosure on reopen — but only shown when no structured
    /// <see cref="Activities"/>/<see cref="Delegations"/> were saved (those render the same cards instead).
    /// </summary>
    public string? Work { get; set; }

    /// <summary>
    /// The main agent's plan for this turn (the <c>update_plan</c> checklist / phases). Restored as the plan
    /// card on reopen. Null when the turn had no plan.
    /// </summary>
    public PlanTurn? Plan { get; set; }

    /// <summary>
    /// A single-agent project run's structured activity feed (one entry per tool call / interim note).
    /// Restored as the activity rows on reopen. Null when the turn produced none.
    /// </summary>
    public List<ActivityTurn>? Activities { get; set; }

    /// <summary>
    /// An orchestrator (lead) run's delegated subtasks — each subagent's brief, its own activity feed, and its
    /// final output. Restored as the per-delegation cards on reopen. Null when the turn delegated nothing.
    /// </summary>
    public List<DelegationTurn>? Delegations { get; set; }
}

/// <summary>Persisted form of one plan step (<see cref="PlanStep"/>) — text + status.</summary>
public sealed class PlanStepTurn
{
    public string Text { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlanStepStatus Status { get; set; }
}

/// <summary>Persisted form of one named plan phase (<see cref="PlanPhase"/>).</summary>
public sealed class PlanPhaseTurn
{
    public string Name { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlanStepStatus Status { get; set; }

    public List<PlanStepTurn> Steps { get; set; } = new();
}

/// <summary>Persisted form of the agent's plan (<see cref="PlanUpdate"/>): either flat steps or named phases.</summary>
public sealed class PlanTurn
{
    public List<PlanStepTurn> Steps { get; set; } = new();
    public List<PlanPhaseTurn> Phases { get; set; } = new();
}

/// <summary>Persisted form of one activity-feed row (a tool call or an interim note).</summary>
public sealed class ActivityTurn
{
    /// <summary>True for the model's interim narration (a muted note) rather than a tool row.</summary>
    public bool IsNote { get; set; }

    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";

    /// <summary>The note narration text (note rows only).</summary>
    public string Text { get; set; } = "";

    /// <summary>The tool's result text, shown in the expandable body.</summary>
    public string Result { get; set; } = "";

    /// <summary>True when the tool finished but failed (drives the ✗ status glyph on reopen).</summary>
    public bool Failed { get; set; }
}

/// <summary>Persisted form of one delegated subtask in an orchestrator run (subagent output + actions).</summary>
public sealed class DelegationTurn
{
    public string AgentName { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Task { get; set; } = "";

    /// <summary>The specialist's final answer (or an error string).</summary>
    public string Result { get; set; } = "";

    /// <summary>The specialist's own activity feed.</summary>
    public List<ActivityTurn> Activities { get; set; } = new();
}

/// <summary>A saved conversation shown in the sidebar chat log and persisted across runs.</summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppMode Mode { get; set; } = AppMode.Chat;

    public List<ChatTurn> Messages { get; set; } = new();
}
