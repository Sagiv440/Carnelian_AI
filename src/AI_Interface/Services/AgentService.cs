using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IAgentService"/>. Holds an embedded built-in roster and reads/writes user-created
/// agents as one JSON file per agent: global customs under <c>&lt;app-data&gt;/AI_Interface/agents</c>
/// and project customs under <c>&lt;projectDir&gt;/.AI/agents</c>. All file I/O is best-effort (a failed
/// read/write must never crash the app), matching <see cref="SettingsService"/> / <see cref="ChatHistoryService"/>.
/// </summary>
public sealed class AgentService : IAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _globalDir;

    /// <summary>Embedded, read-only roster (never written to disk).</summary>
    private readonly IReadOnlyList<Agent> _builtIns = BuildBuiltIns();

    public AgentService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Interface");
        _globalDir = Path.Combine(appData, "agents");
    }

    public Agent Default => _builtIns[0]; // "assistant"

    public IReadOnlyList<Agent> ListAgents(string? projectDir)
    {
        // De-dupe by id with project overriding global overriding built-in.
        var byId = new Dictionary<string, Agent>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in _builtIns)
            byId[agent.Id] = agent;

        foreach (var agent in LoadFrom(_globalDir, AgentScope.Global))
            byId[agent.Id] = agent;

        if (!string.IsNullOrWhiteSpace(projectDir))
            foreach (var agent in LoadFrom(ProjectDir(projectDir), AgentScope.Project))
                byId[agent.Id] = agent;

        // Built-ins first (in their seed order), then customs alphabetically.
        var order = _builtIns.Select((a, i) => (a.Id, i)).ToDictionary(t => t.Id, t => t.i, StringComparer.OrdinalIgnoreCase);
        return byId.Values
            .OrderBy(a => order.TryGetValue(a.Id, out var i) ? i : int.MaxValue)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Agent? Get(string id, string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return ListAgents(projectDir).FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveCustom(Agent agent, string? projectDir)
    {
        if (agent is null || string.IsNullOrWhiteSpace(agent.Id) || IsBuiltInId(agent.Id))
            return; // built-ins are read-only

        var (dir, scope) = agent.Scope == AgentScope.Project && !string.IsNullOrWhiteSpace(projectDir)
            ? (ProjectDir(projectDir!), AgentScope.Project)
            : (_globalDir, AgentScope.Global);

        agent.IsBuiltIn = false;
        agent.Scope = scope;

        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, agent.Id + ".json"), JsonSerializer.Serialize(agent, JsonOptions));
        }
        catch
        {
            // Best-effort: a failed save must not crash the app.
        }
    }

    public void DeleteCustom(string id, string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(id) || IsBuiltInId(id))
            return; // built-ins are read-only

        TryDelete(Path.Combine(_globalDir, id + ".json"));
        if (!string.IsNullOrWhiteSpace(projectDir))
            TryDelete(Path.Combine(ProjectDir(projectDir), id + ".json"));
    }

    // ---- storage helpers -------------------------------------------------------------------

    private static string ProjectDir(string projectDir) => Path.Combine(projectDir, ".AI", "agents");

    private bool IsBuiltInId(string id) =>
        _builtIns.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Agent> LoadFrom(string dir, AgentScope scope)
    {
        var agents = new List<Agent>();
        try
        {
            if (!Directory.Exists(dir))
                return agents;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var agent = JsonSerializer.Deserialize<Agent>(File.ReadAllText(file));
                    if (agent is null || string.IsNullOrWhiteSpace(agent.Id))
                        continue;
                    agent.IsBuiltIn = false;
                    agent.Scope = scope; // the folder it sits in is authoritative
                    agents.Add(agent);
                }
                catch
                {
                    // Skip a single unreadable/corrupt agent file rather than failing the whole load.
                }
            }
        }
        catch
        {
            // Best-effort: an unreadable directory yields no customs.
        }
        return agents;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    // ---- built-in roster -------------------------------------------------------------------

    private static IReadOnlyList<Agent> BuildBuiltIns() => new[]
    {
        new Agent
        {
            Id = "assistant",
            Name = "Assistant",
            Glyph = "🤖",
            IsBuiltIn = true,
            Scope = AgentScope.BuiltIn,
            Autonomy = AutonomyLevel.Guided,
            Persona =
                "You are a helpful, neutral general-purpose assistant. Be accurate, clear, and concise. " +
                "Give direct answers, admit uncertainty, and keep a friendly, professional tone."
        },
        new Agent
        {
            Id = "researcher",
            Name = "Researcher",
            Glyph = "🔬",
            IsBuiltIn = true,
            Scope = AgentScope.BuiltIn,
            Autonomy = AutonomyLevel.Guided,
            Persona =
                "You are a meticulous research assistant. Favour evidence over speculation, prefer up-to-date " +
                "web sources when a question is factual or time-sensitive, and cite claims inline with bracketed " +
                "numbers like [1]. Distinguish what the sources support from your own inference, and call out gaps."
        },
        new Agent
        {
            Id = "code-buddy",
            Name = "Code Buddy",
            Glyph = "👨‍💻",
            IsBuiltIn = true,
            Scope = AgentScope.BuiltIn,
            Autonomy = AutonomyLevel.Guided,
            Persona =
                "You are a careful senior software engineer. Read before you change, make the smallest correct " +
                "change, and explain your reasoning briefly. Prefer idiomatic, well-tested code; flag risks, edge " +
                "cases, and assumptions. When unsure about intent, ask a short clarifying question before acting."
        },
        new Agent
        {
            Id = "autopilot",
            Name = "Autopilot",
            Glyph = "🚀",
            IsBuiltIn = true,
            Scope = AgentScope.BuiltIn,
            Autonomy = AutonomyLevel.Autonomous,
            Persona =
                "You are an autonomous builder. Take a goal, break it into steps, and drive it to completion with " +
                "minimal back-and-forth. Be decisive and bias toward action, but never destroy work you can't " +
                "recover and summarize what you did at the end."
        }
    };
}
