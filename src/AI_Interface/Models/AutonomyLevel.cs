namespace AI_Interface.Models;

/// <summary>
/// How much an agent may act on its own. Maps onto the project-agent loop's approval mode, step
/// budget, and an optional planning pass (Phase 3 — carried by <see cref="Agent"/> but not yet wired).
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
