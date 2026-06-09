namespace AI_Interface.Models;

// Structured progress events emitted by the orchestrator (the lead agent) for each subtask it delegates
// to a specialist. The UI groups them by Index into a per-delegation card; the lead's own reasoning still
// flows through the separate onActivity "work" channel.

/// <summary>The lifecycle stage of a single delegation.</summary>
public enum DelegationPhase
{
    /// <summary>The lead just delegated this subtask; the specialist is about to run.</summary>
    Started,

    /// <summary>One line of the specialist's activity log while it works.</summary>
    Activity,

    /// <summary>The specialist finished (or failed); <see cref="DelegationUpdate.Text"/> is the result/error.</summary>
    Finished
}

/// <summary>
/// One structured update about a delegated subtask. <paramref name="Index"/> is a 0-based per-run counter
/// so the UI maps every update to the right card; <paramref name="Text"/> is the activity line (Activity)
/// or the final answer/error (Finished), and empty for Started.
/// </summary>
public sealed record DelegationUpdate(DelegationPhase Phase, int Index, string AgentName, string Glyph, string Task, string Text);
