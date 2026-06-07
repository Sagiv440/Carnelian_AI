using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IProjectSkillService"/>. Walks the project tree (bounded in depth, file count and
/// size, skipping heavy build/VCS folders) collecting skill files. A file counts as a skill when it is
/// named <c>SKILL.md</c>, ends with <c>.skill.md</c>, or is a markdown file inside a folder named
/// "skills" (so both this app's <c>.AI/skills</c> and the <c>.claude/skills</c> convention are picked up).
/// </summary>
public sealed class ProjectSkillService : IProjectSkillService
{
    private static readonly string[] SkipDirs =
    {
        ".git", ".AI", "node_modules", "bin", "obj", ".vs", ".idea",
        "dist", "build", "out", "packages", "venv", ".venv", "target", ".next"
    };

    private const int MaxSkills = 24;
    private const int MaxCharsPerSkill = 8000;
    private const int MaxDepth = 6;

    public IReadOnlyList<ProjectSkill> Load(string projectDirectory)
    {
        var skills = new List<ProjectSkill>();
        try
        {
            if (!string.IsNullOrWhiteSpace(projectDirectory) && Directory.Exists(projectDirectory))
                Walk(projectDirectory, 0, skills);
        }
        catch
        {
            // Best-effort: a failed scan must not block opening a project.
        }
        return skills;
    }

    private static void Walk(string dir, int depth, List<ProjectSkill> skills)
    {
        if (skills.Count >= MaxSkills || depth > MaxDepth)
            return;

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(dir, "*.md")))
        {
            if (skills.Count >= MaxSkills)
                return;
            if (!IsSkillFile(file))
                continue;
            try
            {
                var content = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(content))
                    continue;
                if (content.Length > MaxCharsPerSkill)
                    content = content[..MaxCharsPerSkill] + "\n…(truncated)";
                skills.Add(new ProjectSkill(SkillName(file), content.Trim()));
            }
            catch
            {
                // Skip an unreadable file.
            }
        }

        foreach (var sub in SafeEnumerate(() => Directory.EnumerateDirectories(dir)))
        {
            var name = Path.GetFileName(sub);
            if (SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            Walk(sub, depth + 1, skills);
            if (skills.Count >= MaxSkills)
                return;
        }
    }

    private static bool IsSkillFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase))
            return true;
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return parent.Equals("skills", StringComparison.OrdinalIgnoreCase);
    }

    private static string SkillName(string path)
    {
        var file = Path.GetFileName(path);
        if (file.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            // SKILL.md files live in a folder named after the skill.
            var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            return string.IsNullOrEmpty(folder) ? "SKILL" : folder;
        }
        if (file.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase))
            return file[..^".skill.md".Length];
        return Path.GetFileNameWithoutExtension(file);
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try { return enumerate().ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
