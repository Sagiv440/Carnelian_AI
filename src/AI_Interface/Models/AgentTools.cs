namespace AI_Interface.Models;

/// <summary>
/// Per-tool allow-list for an <see cref="Agent"/>, mirroring the project-agent tools. The agent only
/// advertises the tools it is permitted to use. Phase 2 wires this into <c>ProjectAgentService</c>;
/// for now it is carried on the agent profile (persisted) but not yet enforced.
/// </summary>
public sealed class AgentTools
{
    /// <summary>May read files / list directories (always safe; on by default).</summary>
    public bool ReadFiles { get; set; } = true;

    /// <summary>May create or overwrite files and folders.</summary>
    public bool WriteFiles { get; set; } = true;

    /// <summary>May delete files and folders.</summary>
    public bool DeleteFiles { get; set; } = true;

    /// <summary>May run terminal commands in the project directory.</summary>
    public bool RunCommands { get; set; } = true;

    /// <summary>May install software machine-wide (still gated by the global software-install permission).</summary>
    public bool InstallSoftware { get; set; }
}
