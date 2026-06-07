namespace AI_Interface.Models;

/// <summary>
/// How the project agent is allowed to install software machine-wide (package managers / system-wide
/// installs). Chosen in Settings → Project.
/// </summary>
public enum SoftwareInstallPermission
{
    /// <summary>No permission: the install tool is withheld and system install commands are refused.</summary>
    Never,

    /// <summary>The agent may install software, but every install is confirmed first — even under Auto-run.</summary>
    Ask,

    /// <summary>The agent may install software, gated only by the normal tool-approval mode.</summary>
    Allow
}
