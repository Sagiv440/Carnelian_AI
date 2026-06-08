using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Reads/writes an <see cref="Agent"/> as a **Claude-Code-style Markdown file**: YAML-ish frontmatter
/// between <c>---</c> fences (name / description / tools / …) followed by the persona as the Markdown
/// <b>body</b>. This is the portable on-disk format — an agent file can be moved between this app and
/// other tools (e.g. dropped into <c>.claude/agents/</c>).
///
/// <para>Portability rules: a plain <c>.md</c> with no frontmatter loads with its whole text as the
/// persona; unknown frontmatter keys are ignored; missing keys take <see cref="Agent"/> defaults.
/// App-specific extras (<c>glyph</c>, <c>model</c>, <c>skills</c>, <c>autonomy</c>, <c>memory</c>,
/// <c>proactive</c>) are written as additional frontmatter that other tools simply skip.</para>
/// </summary>
public static class AgentMarkdown
{
    public const string Extension = ".md";

    public static string Serialize(Agent a)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Clean(a.Name)).Append('\n');
        if (!string.IsNullOrWhiteSpace(a.Description))
            sb.Append("description: ").Append(Clean(a.Description)).Append('\n');
        if (!string.IsNullOrWhiteSpace(a.Glyph))
            sb.Append("glyph: ").Append(Clean(a.Glyph)).Append('\n');
        if (!string.IsNullOrWhiteSpace(a.DefaultModel))
            sb.Append("model: ").Append(Clean(a.DefaultModel)).Append('\n');
        sb.Append("tools: ").Append(ToolsToString(a.Tools)).Append('\n');
        if (a.Skills.Count > 0)
            sb.Append("skills: ").Append(string.Join(", ", a.Skills)).Append('\n');
        sb.Append("autonomy: ").Append(a.Autonomy).Append('\n');
        sb.Append("memory: ").Append(a.MemoryEnabled ? "true" : "false").Append('\n');
        sb.Append("proactive: ").Append(a.Proactive ? "true" : "false").Append('\n');
        sb.Append("---\n\n");
        sb.Append((a.Persona ?? "").Trim()).Append('\n');
        return sb.ToString();
    }

    /// <param name="fallbackId">Used as the agent Id when the frontmatter has no explicit one (the file name without extension).</param>
    public static Agent Parse(string text, string fallbackId)
    {
        text = (text ?? "").Replace("\r\n", "\n");
        var fm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = text.Trim();

        // Frontmatter is an optional --- … --- block at the very top.
        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            var close = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (close > 0)
            {
                var block = text.Substring(4, close - 4);
                var afterFence = text.IndexOf('\n', close + 1);
                body = (afterFence >= 0 ? text[(afterFence + 1)..] : "").Trim();

                foreach (var line in block.Split('\n'))
                {
                    var idx = line.IndexOf(':');
                    if (idx <= 0)
                        continue;
                    var key = line[..idx].Trim();
                    var val = line[(idx + 1)..].Trim();
                    if (key.Length > 0)
                        fm[key] = val;
                }
            }
        }

        var idFromFm = Get(fm, "id");
        return new Agent
        {
            Id = !string.IsNullOrWhiteSpace(idFromFm) ? idFromFm! : fallbackId,
            Name = Get(fm, "name") ?? fallbackId,
            Description = Get(fm, "description") ?? "",
            Glyph = string.IsNullOrWhiteSpace(Get(fm, "glyph")) ? "🤖" : Get(fm, "glyph")!,
            DefaultModel = string.IsNullOrWhiteSpace(Get(fm, "model")) ? null : Get(fm, "model"),
            Persona = body,
            Skills = SplitList(Get(fm, "skills")),
            Tools = ParseTools(Get(fm, "tools")),
            Autonomy = ParseAutonomy(Get(fm, "autonomy")),
            MemoryEnabled = ParseBool(Get(fm, "memory"), true),
            Proactive = ParseBool(Get(fm, "proactive"), false)
        };
    }

    // ---- field helpers ----------------------------------------------------------------------

    private static string? Get(IReadOnlyDictionary<string, string> fm, string key) =>
        fm.TryGetValue(key, out var v) ? v : null;

    /// <summary>Strip newlines so a value never breaks the single-line frontmatter format.</summary>
    private static string Clean(string? s) => (s ?? "").Replace("\n", " ").Replace("\r", " ").Trim();

    private static List<string> SplitList(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool ParseBool(string? s, bool dflt) =>
        bool.TryParse(s, out var b) ? b : dflt;

    private static AutonomyLevel ParseAutonomy(string? s) =>
        Enum.TryParse<AutonomyLevel>(s, ignoreCase: true, out var a) ? a : AutonomyLevel.Guided;

    // ---- tools <-> token list ---------------------------------------------------------------

    private static string ToolsToString(AgentTools? t)
    {
        if (t is null || t.AllowAll)
            return "all";

        var tokens = new List<string>();
        if (t.Allows(AgentToolGroup.ReadFiles)) tokens.Add("read");
        if (t.Allows(AgentToolGroup.WriteFiles)) tokens.Add("write");
        if (t.Allows(AgentToolGroup.DeleteFiles)) tokens.Add("delete");
        if (t.Allows(AgentToolGroup.RunCommands)) tokens.Add("run");
        if (t.Allows(AgentToolGroup.InstallSoftware)) tokens.Add("install");
        return tokens.Count == 0 ? "none" : string.Join(", ", tokens);
    }

    /// <summary>
    /// Parses the <c>tools</c> frontmatter into an allow-list. "all" / missing ⇒ unrestricted. Otherwise
    /// each token sets a group; common Claude-Code tool names are mapped best-effort (Read→read,
    /// Write/Edit→write, Bash→run, …) so a foreign agent file degrades sensibly.
    /// </summary>
    private static AgentTools ParseTools(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv) || csv.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return new AgentTools { AllowAll = true };

        var tools = new AgentTools
        {
            AllowAll = false,
            ReadFiles = false, WriteFiles = false, DeleteFiles = false, RunCommands = false, InstallSoftware = false
        };

        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "read": case "read_file": case "readfiles": case "list": case "ls":
                    tools.ReadFiles = true; break;
                case "write": case "write_file": case "writefiles": case "edit": case "create": case "create_folder":
                    tools.WriteFiles = true; break;
                case "delete": case "delete_file": case "deletefiles": case "delete_folder":
                    tools.DeleteFiles = true; break;
                case "run": case "run_command": case "runcommands": case "bash": case "command": case "commands": case "shell":
                    tools.RunCommands = true; break;
                case "install": case "install_software": case "installsoftware":
                    tools.InstallSoftware = true; break;
                // unknown tokens (foreign tool names we don't have) are ignored
            }
        }
        return tools;
    }
}
