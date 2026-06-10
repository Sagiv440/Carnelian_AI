using System;
using System.Collections.Generic;
using System.Linq;

namespace AI_Interface.ViewModels;

/// <summary>
/// Pure helpers for the composer's slash-command palette: when to open it, the query the user has typed, and
/// which commands match. Kept free of UI/VM state so it's directly unit-testable.
/// </summary>
internal static class SlashMenu
{
    /// <summary>
    /// The menu is open while the input is a slash-command token: it starts with '/' and has no whitespace
    /// yet (typing a space — i.e. starting a real message — closes it). "/" alone opens it (lists everything).
    /// </summary>
    public static bool ShouldOpen(string? input)
    {
        if (string.IsNullOrEmpty(input) || input[0] != '/')
            return false;
        for (var i = 1; i < input.Length; i++)
            if (char.IsWhiteSpace(input[i]))
                return false;
        return true;
    }

    /// <summary>The lowercased query after the leading '/' (e.g. "/Comp" → "comp"). "" when not a slash token.</summary>
    public static string ExtractQuery(string? input) =>
        string.IsNullOrEmpty(input) || input[0] != '/' ? "" : input[1..].ToLowerInvariant();

    /// <summary>
    /// Commands whose name matches the query — prefix matches first (stable), then other substring matches.
    /// An empty query returns every command in its given order. Null-safe.
    /// </summary>
    public static List<SlashCommand> Filter(IReadOnlyList<SlashCommand>? commands, string? query)
    {
        if (commands is null || commands.Count == 0)
            return new List<SlashCommand>();

        query = (query ?? "").Trim().ToLowerInvariant();
        if (query.Length == 0)
            return commands.ToList();

        var prefix = new List<SlashCommand>();
        var contains = new List<SlashCommand>();
        foreach (var c in commands)
        {
            var name = c.Name.ToLowerInvariant();
            if (name.StartsWith(query, StringComparison.Ordinal))
                prefix.Add(c);
            else if (name.Contains(query, StringComparison.Ordinal))
                contains.Add(c);
        }
        prefix.AddRange(contains);
        return prefix;
    }
}
