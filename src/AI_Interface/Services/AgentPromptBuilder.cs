using System.Text;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Composes the system prompt for a turn from the active <see cref="Agent"/>'s persona and selected
/// built-in skill packs, plus the mode's own base/task instructions and the Thinking directive. This
/// replaces the former hard-coded <c>ChatSystemPrompt</c> constant and gives every mode the chosen
/// agent's voice and skills.
/// Order: agent persona → base/task instructions → built-in skills → thinking directive (sandbox rules
/// and project SKILL.md are appended by <c>ProjectAgentService</c> / the VM for Project mode). The agent's
/// <i>built-in</i> skill packs apply in every mode; project skills apply only in Project mode.
/// </summary>
public static class AgentPromptBuilder
{
    /// <summary>
    /// Builds a Chat-style system prompt: the agent's persona (if any) on top, then the mode's base
    /// instructions, then the agent's built-in skill packs, then the Thinking directive. Any piece may be empty.
    /// </summary>
    public static string Compose(Agent? agent, string baseInstructions, string thinkingDirective = "")
    {
        var sb = new StringBuilder();

        var persona = agent?.Persona?.Trim();
        if (!string.IsNullOrEmpty(persona))
            sb.Append(persona);

        if (!string.IsNullOrWhiteSpace(baseInstructions))
        {
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(baseInstructions.Trim());
        }

        sb.Append(SkillsBlock(agent)); // already prefixed with its own blank line (or empty)

        if (!string.IsNullOrEmpty(thinkingDirective))
            sb.Append(thinkingDirective); // already prefixed with its own blank line

        return sb.ToString();
    }

    /// <summary>
    /// The agent persona + built-in skills block prepended to a service-owned system prompt (Web / Deep /
    /// Project modes). Empty when the agent has neither a persona nor selected built-in packs.
    /// </summary>
    public static string PersonaPrefix(Agent? agent)
    {
        var sb = new StringBuilder();

        var persona = agent?.Persona?.Trim();
        if (!string.IsNullOrEmpty(persona))
            sb.Append(persona);

        var skills = SkillsBlock(agent);
        if (skills.Length > 0)
            sb.Append(skills); // leads with its own blank line

        return sb.Length == 0 ? "" : sb.ToString() + "\n\n";
    }

    /// <summary>
    /// The combined text of the agent's selected <b>built-in</b> skill packs, each prefixed with its name,
    /// led by a blank line so it slots after preceding content. Empty when no built-in packs are selected.
    /// (Project <c>SKILL.md</c> selections are resolved separately, in Project mode only.)
    /// </summary>
    public static string SkillsBlock(Agent? agent)
    {
        if (agent?.Skills is not { Count: > 0 } ids)
            return "";

        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            var pack = SkillCatalog.Find(id);
            if (pack is null)
                continue; // a non-built-in id is a project SKILL name, handled elsewhere
            sb.Append("\n\n");
            sb.Append($"Skill — {pack.Name}: {pack.Content}");
        }
        return sb.ToString();
    }
}
