namespace AI_Interface.Services;

/// <summary>Reads a project's AI_DOCS.md handbook (.AI/AI_DOCS.md) for the Project-mode agent to follow.</summary>
public interface IProjectDocsService
{
    /// <summary>The project handbook text (.AI/AI_DOCS.md), or "" when absent/unreadable. Best-effort; never throws.</summary>
    string Load(string projectDirectory);
}
