namespace AI_Interface.Models;

/// <summary>Where an <see cref="Agent"/> comes from, which also drives where (if anywhere) it persists.</summary>
public enum AgentScope
{
    /// <summary>A built-in roster entry: embedded in the app, read-only, never written to disk.</summary>
    BuiltIn,

    /// <summary>A user-created agent stored in the app-data folder, available in every session.</summary>
    Global,

    /// <summary>A user-created agent stored under a project's <c>.AI/agents</c>, available only with that project open.</summary>
    Project
}
