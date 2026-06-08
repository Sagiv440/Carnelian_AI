using System.Collections.Generic;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// The agent registry: a built-in seed roster plus user-created agents stored globally (app-data) and
/// per-project (<c>&lt;project&gt;/.AI/agents</c>). Mirrors the best-effort JSON-store pattern of
/// <see cref="ISettingsService"/> / <see cref="IChatHistoryService"/>.
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// All available agents: built-in + global customs + (when <paramref name="projectDir"/> is given)
    /// project customs, de-duped by id with project overriding global overriding built-in.
    /// </summary>
    IReadOnlyList<Agent> ListAgents(string? projectDir);

    /// <summary>Resolves a single agent by id from the same sources, or null if not found.</summary>
    Agent? Get(string id, string? projectDir);

    /// <summary>Persists a custom agent (global → app-data; project → <c>.AI/agents</c>). Built-in ids are refused.</summary>
    void SaveCustom(Agent agent, string? projectDir);

    /// <summary>Deletes a custom agent file by id. Built-in ids are refused.</summary>
    void DeleteCustom(string id, string? projectDir);

    /// <summary>The fallback persona ("Assistant"), always present in the roster.</summary>
    Agent Default { get; }
}
