namespace AI_Interface.Services;

/// <summary>Reads and writes a project's AI_DOCS.md handbook (.AI/AI_DOCS.md) for the Project-mode agent to follow.</summary>
public interface IProjectDocsService
{
    /// <summary>The project handbook text (.AI/AI_DOCS.md), or "" when absent/unreadable. Best-effort; never throws.</summary>
    string Load(string projectDirectory);

    /// <summary>
    /// Overwrites the project handbook (.AI/AI_DOCS.md) with <paramref name="content"/>, creating the .AI
    /// folder if missing, and returns the absolute path written. Unlike <see cref="Load"/> this does NOT
    /// swallow errors — exceptions propagate so the calling tool can surface a write failure to the agent.
    /// </summary>
    string Save(string projectDirectory, string content);
}
