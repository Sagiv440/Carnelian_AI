using System.Collections.Generic;
using System.Text;
using static System.StringComparison;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AI_Interface.ViewModels;

/// <summary>The kind of block a <see cref="MessageSegment"/> renders.</summary>
public enum SegmentKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Bullet,
    Numbered,
    Divider,
    Code
}

/// <summary>
/// One rendered block of a message: a paragraph, a heading, a list item, a horizontal divider, or a fenced
/// code/command block. Prose blocks (paragraph/heading/list item) render with inline Markdown; code renders
/// as a monospace bubble with a language label + copy button. <see cref="Text"/> is observable so a
/// streaming block grows in place (no container churn).
/// </summary>
public sealed partial class MessageSegment : ObservableObject
{
    /// <summary>What this block is.</summary>
    public SegmentKind Kind { get; }

    /// <summary>Info string after the opening fence (e.g. <c>bash</c>); empty if not code.</summary>
    public string Language { get; }

    /// <summary>List-item marker shown in the gutter ("•" for bullets, "1." for numbered); else empty.</summary>
    public string Marker { get; }

    [ObservableProperty]
    private string _text;

    public MessageSegment(SegmentKind kind, string text, string language = "", string marker = "")
    {
        Kind = kind;
        _text = text;
        Language = language;
        Marker = marker;
    }

    public bool IsCode => Kind == SegmentKind.Code;
    public bool IsParagraph => Kind == SegmentKind.Paragraph;
    public bool IsHeading => Kind is SegmentKind.Heading1 or SegmentKind.Heading2 or SegmentKind.Heading3;
    public bool IsListItem => Kind is SegmentKind.Bullet or SegmentKind.Numbered;
    public bool IsDivider => Kind == SegmentKind.Divider;

    /// <summary>Font size for a heading block (largest for H1); the base size for everything else.</summary>
    public double HeadingFontSize => Kind switch
    {
        SegmentKind.Heading1 => 21,
        SegmentKind.Heading2 => 17.5,
        SegmentKind.Heading3 => 15,
        _ => 14
    };

    /// <summary>Header label for a code bubble — the language, or "code" when unspecified.</summary>
    public string LanguageLabel => string.IsNullOrWhiteSpace(Language) ? "code" : Language;
}

/// <summary>
/// Splits message text into block-level parts for rendering: paragraphs, ATX headings (<c># …</c>),
/// bullet/numbered list items, horizontal dividers (<c>---</c>), and fenced (triple-backtick) code blocks.
/// An unclosed fence (mid-stream) is treated as code so a block bubbles up as soon as it starts. Inline
/// styling within a block (bold/italic/code/links/strikethrough) is applied later by <see cref="InlineMarkdown"/>.
/// </summary>
internal static class MarkdownSegmenter
{
    public readonly record struct Part(SegmentKind Kind, string Text, string Language = "", string Marker = "");

    public static List<Part> Parse(string? text)
    {
        var parts = new List<Part>();
        if (string.IsNullOrEmpty(text))
            return parts;

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var paragraph = new StringBuilder();
        var code = new StringBuilder();
        var inCode = false;
        var language = "";

        void FlushParagraph()
        {
            var t = paragraph.ToString().Trim('\n');
            if (t.Trim().Length > 0)
                parts.Add(new Part(SegmentKind.Paragraph, t));
            paragraph.Clear();
        }

        void FlushCode()
        {
            parts.Add(new Part(SegmentKind.Code, code.ToString().Trim('\n'), language));
            code.Clear();
        }

        foreach (var line in lines)
        {
            var trimmedStart = line.TrimStart();

            // Fenced code: everything between ``` lines is verbatim.
            if (trimmedStart.StartsWith("```", Ordinal))
            {
                if (!inCode)
                {
                    FlushParagraph();
                    inCode = true;
                    var info = trimmedStart.TrimStart('`').Trim();
                    language = info.Length == 0 ? "" : info.Split(new[] { ' ', '\t' }, 2)[0];
                }
                else
                {
                    FlushCode();
                    inCode = false;
                    language = "";
                }
                continue;
            }

            if (inCode)
            {
                code.Append(line).Append('\n');
                continue;
            }

            var trimmed = line.Trim();

            // Blank line ends the current paragraph.
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // Horizontal divider: a line of only -, *, or _ (>= 3), spaces allowed.
            if (IsDivider(trimmed))
            {
                FlushParagraph();
                parts.Add(new Part(SegmentKind.Divider, ""));
                continue;
            }

            // ATX heading: 1-6 '#' then a space.
            var heading = HeadingLevel(trimmedStart);
            if (heading > 0)
            {
                FlushParagraph();
                var content = trimmedStart[heading..].Trim();
                var kind = heading == 1 ? SegmentKind.Heading1 : heading == 2 ? SegmentKind.Heading2 : SegmentKind.Heading3;
                parts.Add(new Part(kind, content));
                continue;
            }

            // Bullet list: -, *, or + then a space.
            if (trimmedStart.Length >= 2 && (trimmedStart[0] is '-' or '*' or '+') && char.IsWhiteSpace(trimmedStart[1]))
            {
                FlushParagraph();
                parts.Add(new Part(SegmentKind.Bullet, trimmedStart[2..].Trim(), Marker: "•"));
                continue;
            }

            // Numbered list: digits then '.' or ')' then a space.
            if (TryNumbered(trimmedStart, out var marker, out var itemText))
            {
                FlushParagraph();
                parts.Add(new Part(SegmentKind.Numbered, itemText, Marker: marker));
                continue;
            }

            // Otherwise it's (a line of) a paragraph.
            paragraph.Append(line).Append('\n');
        }

        if (inCode)
            FlushCode(); // unclosed fence while streaming — render the partial block now
        else
            FlushParagraph();

        return parts;
    }

    /// <summary>Number of leading '#'s (1-6) if the line is an ATX heading ("# text"), else 0.</summary>
    private static int HeadingLevel(string line)
    {
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
            hashes++;
        if (hashes is >= 1 and <= 6 && hashes < line.Length && char.IsWhiteSpace(line[hashes]))
            return hashes;
        return 0;
    }

    /// <summary>True when the line is a horizontal rule: only -, *, or _ (one kind), >= 3, spaces allowed.</summary>
    private static bool IsDivider(string trimmed)
    {
        var marker = '\0';
        var count = 0;
        foreach (var ch in trimmed)
        {
            if (ch == ' ' || ch == '\t')
                continue;
            if (ch is not ('-' or '*' or '_'))
                return false;
            if (marker == '\0')
                marker = ch;
            else if (ch != marker)
                return false;
            count++;
        }
        return count >= 3;
    }

    /// <summary>Parses "12. text" / "3) text" into a "12." marker and the item text.</summary>
    private static bool TryNumbered(string line, out string marker, out string text)
    {
        marker = "";
        text = "";
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
            digits++;
        if (digits == 0 || digits + 1 >= line.Length)
            return false;
        if (line[digits] is not ('.' or ')'))
            return false;
        if (!char.IsWhiteSpace(line[digits + 1]))
            return false;
        marker = line[..digits] + ".";
        text = line[(digits + 2)..].Trim();
        return true;
    }
}
