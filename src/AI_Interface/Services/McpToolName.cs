using System;
using System.Linq;
using System.Text;

namespace AI_Interface.Services;

/// <summary>
/// Namespacing for MCP tools. A tool named <c>tool</c> discovered on server <c>id</c> is advertised to the
/// model as <c>mcp__&lt;id&gt;__&lt;tool&gt;</c> (Claude Code's convention) so MCP tools never collide with the
/// built-in agent tools or with each other across servers. Names are sanitized to the provider-allowed set
/// (<c>[A-Za-z0-9_-]</c>) and length-guarded to 64 chars (OpenAI's function-name limit; Anthropic/Gemini are
/// no stricter), so the same name is valid for every provider.
/// <para>
/// The server id is reduced to <c>[a-z0-9-]</c> — deliberately <b>no underscores</b> — so the first <c>"__"</c>
/// after the prefix always separates the id from the tool, even when the tool name itself contains underscores
/// (e.g. <c>create_issue</c>). Routing does NOT rely on re-parsing (length-guard truncation can lose the exact
/// tool name): <c>McpService</c> keeps a map from the produced name back to (server, real tool). This helper's
/// <see cref="TryParse"/> is for gating (<see cref="IsMcp"/>) and display only. Pure + deterministic (unit-tested).
/// </para>
/// </summary>
internal static class McpToolName
{
    public const string Prefix = "mcp__";
    public const string Separator = "__";
    internal const int MaxLength = 64;
    private const int MaxIdLength = 32;

    /// <summary>True when a tool name is in the MCP namespace (used to gate/route MCP calls).</summary>
    public static bool IsMcp(string? name) =>
        !string.IsNullOrEmpty(name) && name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Builds the namespaced, provider-safe tool name for a server tool. If the composed name exceeds
    /// <see cref="MaxLength"/>, the tool part is truncated and a short stable hash of the original tool name is
    /// appended so distinct long tools stay distinct.
    /// </summary>
    public static string Make(string serverId, string toolName)
    {
        var id = SanitizeId(serverId);
        var tool = SanitizeTool(toolName);
        var full = Prefix + id + Separator + tool;
        if (full.Length <= MaxLength)
            return full;

        var head = Prefix + id + Separator;
        var hash = ShortHash(toolName ?? "");
        var budget = MaxLength - head.Length - hash.Length - 1; // -1 for the '-' before the hash
        if (budget < 1) budget = 1;
        var truncated = tool.Length > budget ? tool[..budget] : tool;
        var result = head + truncated + "-" + hash;
        return result.Length > MaxLength ? result[..MaxLength] : result;
    }

    /// <summary>Best-effort split of a namespaced name into (serverId, toolName). For display/gating only.</summary>
    public static bool TryParse(string? name, out string serverId, out string toolName)
    {
        serverId = "";
        toolName = "";
        if (!IsMcp(name))
            return false;

        var rest = name!.Substring(Prefix.Length);
        var idx = rest.IndexOf(Separator, StringComparison.Ordinal);
        if (idx <= 0)
            return false;

        serverId = rest[..idx];
        toolName = rest[(idx + Separator.Length)..];
        return toolName.Length > 0;
    }

    /// <summary>Reduces a server name/id to a <c>[a-z0-9-]</c> slug (no underscores, capped, no leading/trailing '-').</summary>
    internal static string SanitizeId(string? s)
    {
        var slug = new string((s ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length > MaxIdLength)
            slug = slug[..MaxIdLength].Trim('-');
        return slug.Length == 0 ? "server" : slug;
    }

    /// <summary>Keeps a tool name within <c>[A-Za-z0-9_-]</c> (other chars → '_'); underscores are preserved.</summary>
    private static string SanitizeTool(string? s)
    {
        var t = new string((s ?? "").Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
        return t.Length == 0 ? "tool" : t;
    }

    /// <summary>A short, deterministic, filename-safe hash (FNV-1a → base36), 6 chars. No RNG (resume-safe).</summary>
    private static string ShortHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619;
            }
            const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            var sb = new StringBuilder(6);
            for (var i = 0; i < 6; i++)
            {
                sb.Append(alphabet[(int)(h % 36)]);
                h = h / 36 == 0 ? 2166136261 : h / 36;
            }
            return sb.ToString();
        }
    }
}
