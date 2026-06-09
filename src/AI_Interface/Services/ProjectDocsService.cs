using System.IO;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IProjectDocsService"/>. Reads the project handbook at <c>.AI/AI_DOCS.md</c> — the
/// app's equivalent of how Claude Code reads CLAUDE.md. The text is injected into the Project-mode agent's
/// system prompt (single agent, the Lead orchestrator, and its delegated specialists) and nowhere else.
/// Best-effort like <see cref="AgentService"/>/<see cref="ChatHistoryService"/>: never crashes the app.
/// </summary>
public sealed class ProjectDocsService : IProjectDocsService
{
    public const string FileName = "AI_DOCS.md";

    /// <summary>
    /// Upper bound on the injected handbook size. The text is added to the system prompt on every
    /// project-agent turn (and into each delegated specialist run), so an unbounded file would blow up the
    /// context window / cost. Mirrors <see cref="ProjectSkillService"/>'s per-file cap, with more headroom
    /// since a handbook is legitimately longer than a single skill.
    /// </summary>
    private const int MaxChars = 24000;

    private static string FilePath(string projectDirectory) =>
        Path.Combine(projectDirectory, ".AI", FileName);

    public string Load(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return "";
        try
        {
            var path = FilePath(projectDirectory);
            if (!File.Exists(path))
                return "";
            var text = File.ReadAllText(path).Trim();
            return text.Length > MaxChars
                ? text.Substring(0, MaxChars) + "\n…(AI_DOCS.md truncated)"
                : text;
        }
        catch
        {
            // Best-effort: a missing/unreadable handbook must not block opening a project.
            return "";
        }
    }
}
