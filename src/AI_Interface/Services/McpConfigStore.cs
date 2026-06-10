using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Loads per-project MCP servers from <c>&lt;project&gt;/.AI/mcp.json</c> — the same shape Claude Code uses
/// (<c>{ "mcpServers": { "&lt;name&gt;": { "command"|"url", "args", "env", "headers", … } } }</c>), so a
/// project can ship its own server set. Merged with the global servers by <see cref="McpService"/> (project
/// overrides global by id). <see cref="Parse"/> is pure (no I/O) and unit-tested; <see cref="Load"/> wraps it
/// with a best-effort file read.
/// </summary>
internal static class McpConfigStore
{
    internal const string FileName = "mcp.json";

    /// <summary>Reads <c>&lt;projectDir&gt;/.AI/mcp.json</c> (best-effort: missing/unreadable/invalid ⇒ empty).</summary>
    public static IReadOnlyList<McpServerConfig> Load(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
            return Array.Empty<McpServerConfig>();
        try
        {
            var path = Path.Combine(projectDir, ".AI", FileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Array.Empty<McpServerConfig>();
        }
        catch
        {
            return Array.Empty<McpServerConfig>();
        }
    }

    /// <summary>
    /// Parses the Claude-Code-style <c>mcp.json</c>. Tolerant — a malformed root or entry is skipped, never
    /// thrown on. Transport is taken from an explicit <c>"type"</c> (http/sse ⇒ HTTP) else inferred from
    /// whether a <c>url</c> or a <c>command</c> is present.
    /// </summary>
    internal static IReadOnlyList<McpServerConfig> Parse(string? json)
    {
        var list = new List<McpServerConfig>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return list; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("mcpServers", out var servers) ||
                servers.ValueKind != JsonValueKind.Object)
                return list;

            foreach (var entry in servers.EnumerateObject())
            {
                var name = entry.Name;
                var e = entry.Value;
                if (e.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(name))
                    continue;

                var cfg = new McpServerConfig
                {
                    Id = McpToolName.SanitizeId(name),
                    Name = name,
                    Enabled = GetBool(e, "enabled", true),
                    AutoApprove = GetBool(e, "autoApprove", false),
                    Command = GetString(e, "command") ?? "",
                    Url = GetString(e, "url") ?? "",
                    Args = GetStringList(e, "args"),
                    Env = GetStringMap(e, "env"),
                    Headers = GetStringMap(e, "headers")
                };

                var type = (GetString(e, "type") ?? "").Trim().ToLowerInvariant();
                cfg.Transport =
                    type is "http" or "sse" or "streamable-http" || (type.Length == 0 && cfg.Url.Length > 0)
                        ? McpTransport.Http
                        : McpTransport.Stdio;

                // Keep only entries that are actually runnable for their transport.
                if (cfg.Transport == McpTransport.Stdio ? cfg.Command.Length > 0 : cfg.Url.Length > 0)
                    list.Add(cfg);
            }
        }

        return list;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement e, string name, bool fallback)
    {
        if (!e.TryGetProperty(name, out var v))
            return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static List<string> GetStringList(JsonElement e, string name)
    {
        var list = new List<string>();
        if (e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(item.GetString() ?? "");
        return list;
    }

    private static Dictionary<string, string> GetStringMap(JsonElement e, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    map[p.Name] = p.Value.GetString() ?? "";
        return map;
    }
}
