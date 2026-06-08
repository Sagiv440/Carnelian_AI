using System;
using System.Collections.Generic;
using System.Text;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Reads/writes the persistent-memory store as a portable Markdown file: a list of <c>- </c> bullets,
/// one remembered fact each, with optional <c>&lt;!-- source · date --&gt;</c> metadata tucked into a
/// trailing HTML comment so the file is safe to read, edit, and move between tools by hand. A plain
/// bullet with no comment loads as a fact with empty metadata; non-bullet lines (the heading, blank
/// lines, the how-to comment) are ignored. Mirrors <see cref="AgentMarkdown"/>'s portability goals.
/// </summary>
public static class MemoryMarkdown
{
    public const string Extension = ".md";

    /// <summary>Separates a fact's source from its date inside the trailing metadata comment.</summary>
    private const char MetaSeparator = '·';

    private const string Header =
        "# Memory\n\n" +
        "<!-- Persistent notes the assistant remembers across sessions. One fact per \"- \" bullet.\n" +
        "     Safe to edit by hand; the trailing <!-- ... --> on each line is optional metadata. -->\n\n";

    public static string Serialize(IReadOnlyList<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(Header);
        foreach (var e in entries)
        {
            sb.Append("- ").Append(Clean(e.Text));
            var meta = Meta(e);
            if (meta.Length > 0)
                sb.Append("  <!-- ").Append(meta).Append(" -->");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static List<MemoryEntry> Parse(string text)
    {
        var list = new List<MemoryEntry>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal))
                continue; // headings, blanks, and the how-to comment are not facts

            var body = line[2..].Trim();
            var source = "";
            var date = "";

            // A trailing <!-- source · date --> is optional metadata, not part of the fact.
            var open = body.LastIndexOf("<!--", StringComparison.Ordinal);
            if (open >= 0)
            {
                var close = body.IndexOf("-->", open, StringComparison.Ordinal);
                if (close > open)
                {
                    var meta = body[(open + 4)..close].Trim();
                    body = body[..open].Trim();

                    var sep = meta.IndexOf(MetaSeparator);
                    if (sep >= 0)
                    {
                        source = meta[..sep].Trim();
                        date = meta[(sep + 1)..].Trim();
                    }
                    else
                    {
                        source = meta;
                    }
                }
            }

            if (body.Length > 0)
                list.Add(new MemoryEntry(body, source, date));
        }
        return list;
    }

    private static string Meta(MemoryEntry e)
    {
        var hasSource = !string.IsNullOrWhiteSpace(e.Source);
        var hasDate = !string.IsNullOrWhiteSpace(e.CreatedAtIso);
        if (hasSource && hasDate)
            return $"{Clean(e.Source)} {MetaSeparator} {Clean(e.CreatedAtIso)}";
        if (hasSource)
            return Clean(e.Source);
        if (hasDate)
            return Clean(e.CreatedAtIso);
        return "";
    }

    /// <summary>Keep a fact on a single line so the bullet format never breaks.</summary>
    private static string Clean(string? s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
}
