namespace AI_Interface.Models;

/// <summary>
/// Per-tool allow-list for an <see cref="Agent"/>, mirroring the project-agent tools. The project agent
/// advertises (and runs) only the tools the active agent is allowed; see <c>ProjectAgentService</c>.
/// <para>
/// <b>Unrestricted vs explicit.</b> <see cref="AllowAll"/> defaults to <c>true</c>, meaning a freshly
/// created (or never-customized) agent — including the built-in <i>Assistant</i> — is <i>unrestricted</i>:
/// every tool is offered, so behaviour is unchanged from before per-agent permissions existed. As soon as
/// the user touches the permission checkboxes in the Agents editor, <see cref="AllowAll"/> flips to
/// <c>false</c> and the individual booleans become authoritative. Built-in seed agents set their own
/// explicit allow-lists (<see cref="AllowAll"/> <c>= false</c>) so they differ meaningfully.
/// </para>
/// <see cref="Allows"/> is the single place that resolves a tool group against this representation.
/// </summary>
public sealed class AgentTools
{
    /// <summary>
    /// When true the agent is unrestricted (all tools offered) and the individual flags below are ignored.
    /// Defaults to true so an un-customized agent keeps the original "all tools" behaviour; flipped off the
    /// moment a permission is edited (see <see cref="Restrict"/>).
    /// </summary>
    public bool AllowAll { get; set; } = true;

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

    /// <summary>
    /// May use tools from configured MCP servers (external services). On by default like the file/command
    /// groups; still gated per call by the approval mode (MCP tools reach outside the project) and by whether
    /// any MCP server is configured + enabled.
    /// </summary>
    public bool Mcp { get; set; } = true;

    /// <summary>
    /// Whether the agent may use the given tool group. When <see cref="AllowAll"/> is set (the default /
    /// unrestricted case) every group is permitted; otherwise the matching per-tool flag decides.
    /// </summary>
    public bool Allows(AgentToolGroup group)
    {
        if (AllowAll)
            return true;
        return group switch
        {
            AgentToolGroup.ReadFiles => ReadFiles,
            AgentToolGroup.WriteFiles => WriteFiles,
            AgentToolGroup.DeleteFiles => DeleteFiles,
            AgentToolGroup.RunCommands => RunCommands,
            AgentToolGroup.InstallSoftware => InstallSoftware,
            AgentToolGroup.Mcp => Mcp,
            _ => false
        };
    }

    /// <summary>
    /// Leaves "unrestricted" mode so the per-tool flags become authoritative. Called by the editor the
    /// first time the user toggles a permission, snapshotting today's effective state (all-on) so nothing
    /// the user hadn't explicitly changed is silently revoked.
    /// </summary>
    public void Restrict()
    {
        if (!AllowAll)
            return;
        AllowAll = false;
        ReadFiles = WriteFiles = DeleteFiles = RunCommands = Mcp = true;
        // InstallSoftware stays at its own value (off by default).
    }
}

/// <summary>The tool groups an <see cref="AgentTools"/> allow-list can gate (one per agent permission).</summary>
public enum AgentToolGroup
{
    ReadFiles,
    WriteFiles,
    DeleteFiles,
    RunCommands,
    InstallSoftware,
    Mcp
}
