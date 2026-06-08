using System.Collections.Generic;

namespace AI_Interface.Models;

/// <summary>A built-in skill pack: a small reusable behaviour instruction selectable per <see cref="Agent"/>.</summary>
public sealed record SkillPack(string Id, string Name, string Content);

/// <summary>
/// The embedded catalog of built-in skill packs. An <see cref="Agent"/>'s <see cref="Agent.Skills"/> list
/// stores pack <see cref="SkillPack.Id"/>s (alongside any project <c>SKILL.md</c> names); selected packs'
/// <see cref="SkillPack.Content"/> is appended to the system prompt by <c>AgentPromptBuilder</c> in every
/// mode. Project skills, by contrast, only apply in Project mode (and only when the project is open).
/// </summary>
public static class SkillCatalog
{
    /// <summary>The curated built-in packs, in display order.</summary>
    public static readonly IReadOnlyList<SkillPack> BuiltIn = new[]
    {
        new SkillPack(
            "cited-research",
            "Cited research",
            "Back factual or time-sensitive claims with sources and cite them inline using bracketed " +
            "numbers like [1], [2], with a short numbered source list at the end. Separate what the sources " +
            "actually support from your own inference, and say when you are unsure or evidence is missing."),

        new SkillPack(
            "concise",
            "Concise",
            "Be terse. Lead with the answer, drop filler, pleasantries, and restating the question. Prefer " +
            "short sentences and tight bullet lists over prose. Only add detail the user explicitly asked for."),

        new SkillPack(
            "careful-coding",
            "Careful coding",
            "When working with code: read the relevant files before editing, make the smallest correct change, " +
            "and keep diffs minimal. Match the surrounding style and conventions. Call out anything risky " +
            "(data loss, breaking changes, security) before doing it, and note assumptions you had to make."),

        new SkillPack(
            "step-by-step",
            "Step by step",
            "Before acting on a non-trivial task, outline a short numbered plan of the steps you intend to take, " +
            "then carry them out in order. Adjust the plan if you learn something that changes it, and finish " +
            "with a brief summary of what you did.")
    };

    /// <summary>Returns the built-in pack with this id, or null if the id is unknown (e.g. a project-skill name).</summary>
    public static SkillPack? Find(string id)
    {
        foreach (var pack in BuiltIn)
            if (string.Equals(pack.Id, id, System.StringComparison.OrdinalIgnoreCase))
                return pack;
        return null;
    }

    /// <summary>True when the given id names a built-in pack (vs a project <c>SKILL.md</c> name).</summary>
    public static bool IsBuiltInId(string id) => Find(id) is not null;
}
