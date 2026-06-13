using System.Collections.Generic;
using System.Text;

namespace AI_Interface.ViewModels;

/// <summary>How a run of prose text should be styled when rendered as inline Markdown.</summary>
public enum InlineStyle
{
    Normal,
    Bold,
    Italic,
    BoldItalic,
    Code
}

/// <summary>One styled run of prose (the text plus how to render it). Pure data — no UI types.</summary>
public readonly record struct InlineSpan(string Text, InlineStyle Style);

/// <summary>
/// A tiny, pure inline-Markdown tokenizer for the transcript's <b>prose</b> segments (fenced code blocks
/// are handled earlier by <see cref="MarkdownSegmenter"/>). It recognises <c>**bold**</c>, <c>*italic*</c>,
/// <c>***bold italic***</c> (asterisks only) and <c>`inline code`</c>, and leaves everything else literal.
/// <para>
/// Deliberately conservative to avoid false positives in this app's content: underscores are <b>not</b>
/// emphasis markers (so identifiers like <c>run_command</c> / <c>AI_DOCS.md</c> stay intact), and an
/// asterisk emphasis only matches when it isn't padded by whitespace (so "2 * 3" isn't italicised). An
/// unmatched delimiter is rendered as a literal character.
/// </para>
/// </summary>
public static class InlineMarkdown
{
    public static IReadOnlyList<InlineSpan> Parse(string? text)
    {
        var spans = new List<InlineSpan>();
        if (string.IsNullOrEmpty(text))
            return spans;

        var buffer = new StringBuilder();
        var i = 0;
        var n = text.Length;

        void FlushNormal()
        {
            if (buffer.Length > 0)
            {
                spans.Add(new InlineSpan(buffer.ToString(), InlineStyle.Normal));
                buffer.Clear();
            }
        }

        while (i < n)
        {
            var c = text[i];

            // `inline code` — content is literal (no nested emphasis), needs a closing backtick.
            if (c == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i + 1) // require at least one char between the ticks
                {
                    FlushNormal();
                    spans.Add(new InlineSpan(text.Substring(i + 1, close - i - 1), InlineStyle.Code));
                    i = close + 1;
                    continue;
                }
            }
            // *italic* / **bold** / ***both*** — asterisks only.
            else if (c == '*')
            {
                var run = 1;
                while (i + run < n && text[i + run] == '*')
                    run++;
                var delim = run >= 3 ? 3 : run;

                var contentStart = i + delim;
                var close = FindClosingAsterisks(text, contentStart, delim);
                if (close > contentStart &&
                    !char.IsWhiteSpace(text[contentStart]) &&   // no "** text" (space after opener)
                    !char.IsWhiteSpace(text[close - 1]))         // no "text **" (space before closer)
                {
                    FlushNormal();
                    var inner = text.Substring(contentStart, close - contentStart);
                    var style = delim switch
                    {
                        3 => InlineStyle.BoldItalic,
                        2 => InlineStyle.Bold,
                        _ => InlineStyle.Italic
                    };
                    spans.Add(new InlineSpan(inner, style));
                    i = close + delim;
                    continue;
                }
            }

            buffer.Append(c);
            i++;
        }

        FlushNormal();
        return spans;
    }

    /// <summary>Index of a closing run of at least <paramref name="delim"/> asterisks, or -1 if none.</summary>
    private static int FindClosingAsterisks(string text, int start, int delim)
    {
        var i = start;
        while (i < text.Length)
        {
            if (text[i] == '*')
            {
                var run = 1;
                while (i + run < text.Length && text[i + run] == '*')
                    run++;
                if (run >= delim)
                    return i;
                i += run;
            }
            else
            {
                i++;
            }
        }
        return -1;
    }
}
