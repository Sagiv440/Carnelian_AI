using System.Linq;
using System.Text;

namespace AI_Interface.ViewModels;

/// <summary>
/// Converts a parsed <see cref="TableData"/> into formats that paste as a <b>real table</b> elsewhere:
/// <see cref="ToHtml"/> (a <c>&lt;table&gt;</c> — Word / Google Docs / LibreOffice paste it as a table),
/// <see cref="ToTsv"/> (tab-separated — Excel / Sheets paste it as a grid), and <see cref="WrapCfHtml"/>
/// (the Windows clipboard "HTML Format" CF_HTML envelope around the HTML). Cell text keeps inline
/// formatting (bold/italic/code/strikethrough/links) by reusing <see cref="InlineMarkdown"/>. Pure — the
/// view-layer code-behind puts the results on the clipboard.
/// </summary>
internal static class TableExport
{
    /// <summary>An HTML <c>&lt;table&gt;</c> with a bold header row; cells render inline Markdown as HTML.</summary>
    public static string ToHtml(TableData table)
    {
        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse\">");

        sb.Append("<thead><tr>");
        foreach (var h in table.Header)
            sb.Append("<th>").Append(CellHtml(h)).Append("</th>");
        sb.Append("</tr></thead>");

        sb.Append("<tbody>");
        foreach (var row in table.Rows)
        {
            sb.Append("<tr>");
            for (var c = 0; c < table.ColumnCount; c++)
                sb.Append("<td>").Append(CellHtml(c < row.Count ? row[c] : "")).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    /// <summary>Tab-separated rows (header first); cells flattened to plain text.</summary>
    public static string ToTsv(TableData table)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join('\t', table.Header.Select(CellPlain)));
        foreach (var row in table.Rows)
        {
            sb.Append('\n');
            sb.Append(string.Join('\t', Enumerable.Range(0, table.ColumnCount)
                .Select(c => CellPlain(c < row.Count ? row[c] : ""))));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Wraps an HTML fragment in the CF_HTML envelope Windows' clipboard "HTML Format" requires (a header of
    /// byte offsets, then the fragment between sentinels). Offsets are UTF-8 byte counts; the fixed 8-digit
    /// fields keep the header length constant so the computed offsets stay valid.
    /// </summary>
    public static string WrapCfHtml(string fragmentHtml)
    {
        const string headerTemplate =
            "Version:0.9\r\n" +
            "StartHTML:{0:D8}\r\n" +
            "EndHTML:{1:D8}\r\n" +
            "StartFragment:{2:D8}\r\n" +
            "EndFragment:{3:D8}\r\n";
        const string preFragment = "<html><body><!--StartFragment-->";
        const string postFragment = "<!--EndFragment--></body></html>";

        var headerLen = Encoding.UTF8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
        var startFragment = headerLen + Encoding.UTF8.GetByteCount(preFragment);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragmentHtml);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(postFragment);

        return string.Format(headerTemplate, headerLen, endHtml, startFragment, endFragment)
               + preFragment + fragmentHtml + postFragment;
    }

    private static string CellHtml(string raw)
    {
        var sb = new StringBuilder();
        foreach (var span in InlineMarkdown.Parse(raw))
        {
            if (span.Href is { Length: > 0 } href && InlineMarkdown.IsAllowedLinkScheme(href))
            {
                sb.Append("<a href=\"").Append(Escape(href)).Append("\">").Append(Escape(span.Text)).Append("</a>");
                continue;
            }
            var (open, close) = span.Style switch
            {
                InlineStyle.Bold => ("<b>", "</b>"),
                InlineStyle.Italic => ("<i>", "</i>"),
                InlineStyle.BoldItalic => ("<b><i>", "</i></b>"),
                InlineStyle.Code => ("<code>", "</code>"),
                InlineStyle.Strikethrough => ("<s>", "</s>"),
                _ => ("", "")
            };
            sb.Append(open).Append(Escape(span.Text)).Append(close);
        }
        return sb.ToString();
    }

    private static string CellPlain(string raw) =>
        string.Concat(InlineMarkdown.Parse(raw).Select(s => s.Text));

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
