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
    /// instructions, then the agent's built-in skill packs, then the persistent-memory block, then the
    /// Thinking directive. Any piece may be empty.
    /// </summary>
    public static string Compose(Agent? agent, string baseInstructions, string thinkingDirective = "", string memoryBlock = "")
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

        if (!string.IsNullOrWhiteSpace(memoryBlock))
        {
            sb.Append("\n\n");
            sb.Append(memoryBlock.Trim());
        }

        if (!string.IsNullOrEmpty(thinkingDirective))
            sb.Append(thinkingDirective); // already prefixed with its own blank line

        return sb.ToString();
    }

    /// <summary>
    /// The agent persona + built-in skills + persistent-memory block prepended to a service-owned system
    /// prompt (Web / Deep / Project modes). Empty when the agent has no persona, no selected built-in
    /// packs, and there's nothing to remember.
    /// </summary>
    public static string PersonaPrefix(Agent? agent, string memoryBlock = "")
    {
        var sb = new StringBuilder();

        var persona = agent?.Persona?.Trim();
        if (!string.IsNullOrEmpty(persona))
            sb.Append(persona);

        var skills = SkillsBlock(agent);
        if (skills.Length > 0)
            sb.Append(skills); // leads with its own blank line

        if (!string.IsNullOrWhiteSpace(memoryBlock))
        {
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(memoryBlock.Trim());
        }

        return sb.Length == 0 ? "" : sb.ToString() + "\n\n";
    }

    /// <summary>
    /// The plan-then-execute directive added to the Project-agent system prompt when the global approval
    /// mode is <see cref="AgentApprovalMode.AutoRun"/>, so the agent outlines a short plan before acting and
    /// then summarizes. Empty for <see cref="AgentApprovalMode.ConfirmDestructive"/> /
    /// <see cref="AgentApprovalMode.ConfirmEverything"/> (no planning pass). Led by a blank line so it slots
    /// after preceding system-prompt content. This is a prompt directive only — it reuses the existing tool
    /// loop rather than adding a separate planning round.
    /// </summary>
    public static string PlanningDirective(AgentApprovalMode approval) => approval == AgentApprovalMode.AutoRun
        ? "\n\nWork autonomously: first outline a short numbered plan of the steps you'll take, then " +
          "execute it step by step (calling tools as needed), and finish with a brief summary of what you did."
        : "";

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
