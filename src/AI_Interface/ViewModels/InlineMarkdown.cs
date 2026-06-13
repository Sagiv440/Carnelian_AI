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
    Code,
    Strikethrough
}

/// <summary>
/// One styled run of prose (the text, how to render it, and an optional link target). Pure data — no UI
/// types. When <see cref="Href"/> is non-null the run is a clickable link.
/// </summary>
public readonly record struct InlineSpan(string Text, InlineStyle Style, string? Href = null);

/// <summary>
/// A tiny, pure inline-Markdown tokenizer for the transcript's <b>prose</b> segments (fenced code blocks
/// are handled earlier by <see cref="MarkdownSegmenter"/>). It recognises <c>**bold**</c>, <c>*italic*</c>,
/// <c>***bold italic***</c> (asterisks only), <c>`inline code`</c>, <c>~~strikethrough~~</c>, and
/// <c>[text](url)</c> links; everything else stays literal.
/// <para>
/// Deliberately conservative to avoid false positives in this app's content: underscores are <b>not</b>
/// emphasis markers (so identifiers like <c>run_command</c> / <c>AI_DOCS.md</c> stay intact), and an
/// asterisk/tilde emphasis only matches when it isn't padded by whitespace (so "2 * 3" isn't italicised).
/// An unmatched delimiter is rendered as a literal character.
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
            // [label](url) — a clickable link.
            else if (c == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < n && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        var label = text.Substring(i + 1, closeBracket - i - 1);
                        var url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2).Trim();
                        if (label.Length > 0 && url.Length > 0)
                        {
                            FlushNormal();
                            spans.Add(new InlineSpan(label, InlineStyle.Normal, url));
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }
            }
            // ~~strikethrough~~
            else if (c == '~' && i + 1 < n && text[i + 1] == '~')
            {
                var close = text.IndexOf("~~", i + 2, System.StringComparison.Ordinal);
                if (close > i + 2 &&
                    !char.IsWhiteSpace(text[i + 2]) &&
                    !char.IsWhiteSpace(text[close - 1]))
                {
                    FlushNormal();
                    spans.Add(new InlineSpan(text.Substring(i + 2, close - (i + 2)), InlineStyle.Strikethrough));
                    i = close + 2;
                    continue;
                }
            }
            // Bare URL (http://… / https://…) — auto-linked so it's clickable without [..](..) syntax.
            else if (c == 'h' && TryReadBareUrl(text, i, out var urlLen))
            {
                FlushNormal();
                var url = text.Substring(i, urlLen);
                spans.Add(new InlineSpan(url, InlineStyle.Normal, url));
                i += urlLen;
                continue;
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

    /// <summary>
    /// True for link targets that are safe to launch from <b>model-supplied</b> text — only absolute
    /// <c>http</c>/<c>https</c>/<c>mailto</c> URLs. Blocks <c>file:</c>, <c>javascript:</c>, custom
    /// protocol handlers, and relative strings, so a clicked link can't open a local file / handler.
    /// </summary>
    public static bool IsAllowedLinkScheme(string? href) =>
        System.Uri.TryCreate(href, System.UriKind.Absolute, out var uri) &&
        (uri.Scheme == System.Uri.UriSchemeHttp ||
         uri.Scheme == System.Uri.UriSchemeHttps ||
         uri.Scheme == System.Uri.UriSchemeMailto);

    /// <summary>
    /// If a bare <c>http://</c>/<c>https://</c> URL starts at <paramref name="i"/>, sets its length (with
    /// trailing sentence punctuation and a closing bracket/quote excluded) and returns true.
    /// </summary>
    private static bool TryReadBareUrl(string text, int i, out int length)
    {
        length = 0;
        var scheme = StartsWith(text, i, "https://") ? 8 : StartsWith(text, i, "http://") ? 7 : 0;
        if (scheme == 0)
            return false;

        var j = i + scheme;
        while (j < text.Length && !char.IsWhiteSpace(text[j]) &&
               text[j] is not ('<' or '>' or '"' or '\'' or '`' or ']' or ')' or '(' or '['))
            j++;

        // Don't swallow trailing sentence punctuation (e.g. "see https://x.com.").
        while (j > i + scheme && text[j - 1] is '.' or ',' or ';' or ':' or '!' or '?')
            j--;

        length = j - i;
        return length > scheme; // require at least one host character after the scheme
    }

    private static bool StartsWith(string text, int i, string prefix) =>
        i + prefix.Length <= text.Length && string.CompareOrdinal(text, i, prefix, 0, prefix.Length) == 0;

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
