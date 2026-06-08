using System.Text;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Composes the system prompt for a turn from the active <see cref="Agent"/>'s persona plus the mode's
/// own base/task instructions and the Thinking directive. This replaces the former hard-coded
/// <c>ChatSystemPrompt</c> constant and gives every mode the chosen agent's voice.
/// Order: agent persona → base/task instructions → thinking directive (sandbox rules are appended by
/// <c>ProjectAgentService</c> for Project mode). Later phases add a memory block and skills text.
/// </summary>
public static class AgentPromptBuilder
{
    /// <summary>
    /// Builds a system prompt: the agent's persona (if any) on top, then the mode's base instructions,
    /// then the Thinking directive. Either piece may be empty.
    /// </summary>
    public static string Compose(Agent? agent, string baseInstructions, string thinkingDirective = "")
    {
        var sb = new StringBuilder();

        var persona = agent?.Persona?.Trim();
        if (!string.IsNullOrEmpty(persona))
        {
            sb.Append(persona);
            if (!string.IsNullOrWhiteSpace(baseInstructions))
                sb.Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(baseInstructions))
            sb.Append(baseInstructions.Trim());

        if (!string.IsNullOrEmpty(thinkingDirective))
            sb.Append(thinkingDirective); // already prefixed with its own blank line

        return sb.ToString();
    }

    /// <summary>The agent persona block prepended to a service-owned system prompt (Project mode). Empty when none.</summary>
    public static string PersonaPrefix(Agent? agent)
    {
        var persona = agent?.Persona?.Trim();
        return string.IsNullOrEmpty(persona) ? "" : persona + "\n\n";
    }
}
