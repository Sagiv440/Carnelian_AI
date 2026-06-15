using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IDocumentService"/>: Word via the OpenXML SDK, PDF via QuestPDF. Content is treated
/// as light markdown — lines starting with <c>#</c>/<c>##</c>/<c>###</c> become headings, <c>- </c>/<c>* </c>
/// become bullets, blank lines become spacing, everything else is a paragraph. (The <c>W</c> alias keeps
/// OpenXML's <c>Document</c> distinct from QuestPDF's <c>Document</c>.)
/// </summary>
public sealed class DocumentService : IDocumentService
{
    static DocumentService()
    {
        // QuestPDF requires a license to be declared before first use; Community is free for individuals
        // and small businesses (https://www.questpdf.com/license/).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private enum Block { Heading1, Heading2, Heading3, Bullet, Paragraph, Blank, Table }

    // Matches inline markdown: ***both***, **bold**, *italic*, ~~strike~~, `code`, [label](url), and bare URLs.
    // Named groups let both the plain-text stripper (Word) and the rich renderer (PDF hyperlinks) reuse one pattern.
    private static readonly Regex InlineMarkdownPattern = new(
        @"(?<bi>\*\*\*(?<bitext>.+?)\*\*\*)" +
        @"|(?<b>\*\*(?<btext>.+?)\*\*)" +
        @"|(?<i>\*(?<itext>.+?)\*)" +
        @"|(?<s>~~(?<stext>.+?)~~)" +
        @"|(?<c>`(?<ctext>.+?)`)" +
        @"|(?<link>\[(?<ltext>[^\]]*)\]\((?<lurl>[^)]*)\))" +
        @"|(?<url>https?://[^\s)]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Numeric citation markers like "[1]", "[1, 2]" or "[1][2]" (but NOT a "[label](url)" link). The leading
    // space is eaten too so "Dough [1]" becomes "Dough", not "Dough ".
    private static readonly Regex CitationPattern = new(
        @"\s?\[\d+(?:\s*,\s*\d+)*\](?!\()",
        RegexOptions.Compiled);

    private static string StripCitations(string content) => CitationPattern.Replace(content, "");

    // Plain-text fallback (Word): drop the markup, keep the visible text / link label.
    private static string StripInline(string text) =>
        InlineMarkdownPattern.Replace(text, m =>
            m.Groups["bi"].Success ? m.Groups["bitext"].Value :
            m.Groups["b"].Success ? m.Groups["btext"].Value :
            m.Groups["i"].Success ? m.Groups["itext"].Value :
            m.Groups["s"].Success ? m.Groups["stext"].Value :
            m.Groups["c"].Success ? m.Groups["ctext"].Value :
            m.Groups["link"].Success
                ? (m.Groups["ltext"].Value.Length > 0 ? m.Groups["ltext"].Value : m.Groups["lurl"].Value)
                : m.Value);   // bare URL: leave as-is

    private static IEnumerable<(Block Kind, string Text)> Parse(string content)
    {
        var lines = StripCitations(content).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) { yield return (Block.Blank, ""); continue; }

            // GFM table: a header row followed by a separator row — consume the whole block.
            if (line.Contains('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var sb = new StringBuilder();
                sb.Append(line).Append('\n');
                sb.Append(lines[++i]).Append('\n');
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && lines[i + 1].Contains('|'))
                    sb.Append(lines[++i]).Append('\n');
                yield return (Block.Table, sb.ToString().TrimEnd('\n'));
                continue;
            }

            // ATX heading: 1-6 leading '#' then a space. 4-6 render like H3, and the '#' markers are always
            // stripped (so "#### 1. Title" never shows the raw hashes).
            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#') hashes++;
            if (hashes is >= 1 and <= 6 && hashes < line.Length && line[hashes] == ' ')
            {
                var headingText = line[(hashes + 1)..].TrimStart();
                var kind = hashes == 1 ? Block.Heading1 : hashes == 2 ? Block.Heading2 : Block.Heading3;
                yield return (kind, headingText);
                continue;
            }

            if (line.StartsWith("- ") || line.StartsWith("* ")) { yield return (Block.Bullet, line[2..]); continue; }
            yield return (Block.Paragraph, line);
        }
    }

    // ---- table parsing (shared by PDF + Word) ----------------------------------------------

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.Contains('|'))
            return false;
        var cells = SplitCells(trimmed);
        if (cells.Count == 0)
            return false;
        foreach (var raw in cells)
        {
            var c = raw.Trim();
            if (c.Length == 0) return false;
            var i = 0;
            if (c[i] == ':') i++;
            var dashStart = i;
            while (i < c.Length && c[i] == '-') i++;
            if (i == dashStart) return false;
            if (i < c.Length && c[i] == ':') i++;
            if (i != c.Length) return false;
        }
        return true;
    }

    private static List<string> SplitCells(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToList();
    }

    /// <summary>Header row + body rows from a raw table block (skips the separator line).</summary>
    private static (List<string> Header, List<List<string>> Rows) ParseTable(string raw)
    {
        var header = new List<string>();
        var rows = new List<List<string>>();
        var sepSeen = false;
        foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            if (!sepSeen && IsTableSeparator(line)) { sepSeen = true; continue; }
            var cells = SplitCells(line);
            if (header.Count == 0 && !sepSeen) header.AddRange(cells);
            else rows.Add(cells);
        }
        return (header, rows);
    }

    // ---- Word (.docx) ----------------------------------------------------------------------

    public int CreateWord(string fullPath, string content)
    {
        using var doc = WordprocessingDocument.Create(fullPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new W.Document();
        var body = new W.Body();
        main.Document.Append(body);

        var count = AppendBlocks(body, content);
        main.Document.Save();
        return count;
    }

    public int AppendWord(string fullPath, string content)
    {
        using var doc = WordprocessingDocument.Open(fullPath, true);
        var body = doc.MainDocumentPart?.Document?.Body
                   ?? throw new InvalidOperationException("Not a valid Word document.");
        var count = AppendBlocks(body, content);
        doc.MainDocumentPart!.Document.Save();
        return count;
    }

    public int ReplaceInWord(string fullPath, string find, string replace)
    {
        if (string.IsNullOrEmpty(find))
            return 0;

        using var doc = WordprocessingDocument.Open(fullPath, true);
        var body = doc.MainDocumentPart?.Document?.Body
                   ?? throw new InvalidOperationException("Not a valid Word document.");

        var replaced = 0;
        foreach (var text in body.Descendants<W.Text>())
        {
            if (!text.Text.Contains(find, StringComparison.Ordinal))
                continue;
            replaced += (text.Text.Length - text.Text.Replace(find, "").Length) / find.Length;
            text.Text = text.Text.Replace(find, replace);
        }

        if (replaced > 0)
            doc.MainDocumentPart!.Document.Save();
        return replaced;
    }

    private static int AppendBlocks(W.Body body, string content)
    {
        var count = 0;
        foreach (var (kind, text) in Parse(content))
        {
            if (kind == Block.Blank)
            {
                body.AppendChild(new W.Paragraph());
                continue;
            }
            if (kind == Block.Table)
            {
                body.AppendChild(WordTable(text));
                count++;
                continue;
            }
            body.AppendChild(WordParagraph(kind, text));
            count++;
        }
        return count;
    }

    /// <summary>Renders a Markdown table block as a real Word table with a bordered grid + bold header row.</summary>
    private static W.Table WordTable(string raw)
    {
        var (header, rows) = ParseTable(raw);
        var cols = header.Count;
        foreach (var r in rows) cols = Math.Max(cols, r.Count);

        var table = new W.Table();
        var borders = new W.TableBorders(
            new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 });
        table.AppendChild(new W.TableProperties(borders));

        table.AppendChild(WordRow(header, cols, bold: true));
        foreach (var r in rows)
            table.AppendChild(WordRow(r, cols, bold: false));
        return table;
    }

    private static W.TableRow WordRow(IReadOnlyList<string> cells, int cols, bool bold)
    {
        var row = new W.TableRow();
        for (var c = 0; c < cols; c++)
        {
            var runProps = new W.RunProperties();
            if (bold) runProps.Append(new W.Bold());
            var plain = StripInline(c < cells.Count ? cells[c] : "");
            var run = new W.Run(runProps, new W.Text(plain) { Space = SpaceProcessingModeValues.Preserve });
            row.AppendChild(new W.TableCell(new W.Paragraph(run)));
        }
        return row;
    }

    private static W.Paragraph WordParagraph(Block kind, string text)
    {
        var runProps = new W.RunProperties();
        switch (kind)
        {
            case Block.Heading1: runProps.Append(new W.Bold(), new W.FontSize { Val = "36" }); break; // 18pt
            case Block.Heading2: runProps.Append(new W.Bold(), new W.FontSize { Val = "30" }); break; // 15pt
            case Block.Heading3: runProps.Append(new W.Bold(), new W.FontSize { Val = "26" }); break; // 13pt
        }

        var plain = StripInline(text);
        var display = kind == Block.Bullet ? "•  " + plain : plain;
        var run = new W.Run(runProps, new W.Text(display) { Space = SpaceProcessingModeValues.Preserve });
        return new W.Paragraph(run);
    }

    // ---- PDF -------------------------------------------------------------------------------

    public int CreatePdf(string fullPath, string content)
    {
        var blocks = Parse(content).ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11));
                page.Content().Column(col =>
                {
                    col.Spacing(5);
                    foreach (var (kind, text) in blocks)
                    {
                        switch (kind)
                        {
                            case Block.Blank: col.Item().Height(6); break;
                            case Block.Heading1: col.Item().Text(StripInline(text)).FontSize(20).Bold(); break;
                            case Block.Heading2: col.Item().Text(StripInline(text)).FontSize(16).Bold(); break;
                            case Block.Heading3: col.Item().Text(StripInline(text)).FontSize(13).Bold(); break;
                            case Block.Bullet: col.Item().Text(t => { t.Span("•  "); RenderInline(t, text); }); break;
                            case Block.Table: col.Item().PaddingVertical(4).Element(e => RenderTable(e, text)); break;
                            default: col.Item().Text(t => RenderInline(t, text)); break;
                        }
                    }
                });
            });
        }).GeneratePdf(fullPath);

        return blocks.Count(b => b.Kind != Block.Blank);
    }

    /// <summary>
    /// Renders one prose line into a QuestPDF <see cref="TextDescriptor"/>: emphasis becomes styled spans,
    /// and <c>[label](url)</c> / bare URLs become clickable, accent-coloured, underlined hyperlinks.
    /// </summary>
    private static void RenderInline(TextDescriptor text, string line)
    {
        var pos = 0;
        foreach (Match m in InlineMarkdownPattern.Matches(line))
        {
            if (m.Index > pos)
                text.Span(line[pos..m.Index]);

            if (m.Groups["link"].Success)
            {
                var url = m.Groups["lurl"].Value;
                var label = m.Groups["ltext"].Value;
                text.Hyperlink(label.Length > 0 ? label : url, url).FontColor(Colors.Blue.Medium).Underline();
            }
            else if (m.Groups["url"].Success)
            {
                // A bare URL may swallow trailing sentence punctuation — peel it back off as plain text.
                var url = m.Groups["url"].Value;
                var trail = "";
                while (url.Length > 0 && url[^1] is '.' or ',' or ';' or ':' or '!' or '?')
                {
                    trail = url[^1] + trail;
                    url = url[..^1];
                }
                text.Hyperlink(url, url).FontColor(Colors.Blue.Medium).Underline();
                if (trail.Length > 0)
                    text.Span(trail);
            }
            else if (m.Groups["bi"].Success) text.Span(m.Groups["bitext"].Value).Bold().Italic();
            else if (m.Groups["b"].Success) text.Span(m.Groups["btext"].Value).Bold();
            else if (m.Groups["i"].Success) text.Span(m.Groups["itext"].Value).Italic();
            else if (m.Groups["s"].Success) text.Span(m.Groups["stext"].Value).Strikethrough();
            else if (m.Groups["c"].Success) text.Span(m.Groups["ctext"].Value).FontFamily(Fonts.Consolas);

            pos = m.Index + m.Length;
        }
        if (pos < line.Length)
            text.Span(line[pos..]);
    }

    /// <summary>Renders a Markdown table block as a QuestPDF table: equal-width columns, hairline borders,
    /// a shaded bold header row, and inline formatting / hyperlinks inside each cell.</summary>
    private static void RenderTable(IContainer container, string raw)
    {
        var (header, rows) = ParseTable(raw);
        var cols = header.Count;
        foreach (var r in rows) cols = Math.Max(cols, r.Count);
        if (cols == 0) return;

        container.Table(table =>
        {
            table.ColumnsDefinition(def =>
            {
                for (var c = 0; c < cols; c++) def.RelativeColumn();
            });

            for (var c = 0; c < cols; c++)
                Cell(table, c < header.Count ? header[c] : "", cols, isHeader: true);
            foreach (var r in rows)
                for (var c = 0; c < cols; c++)
                    Cell(table, c < r.Count ? r[c] : "", cols, isHeader: false);
        });

        static void Cell(TableDescriptor table, string text, int cols, bool isHeader)
        {
            table.Cell()
                .Border(0.5f).BorderColor(Colors.Grey.Medium)
                .Background(isHeader ? Colors.Grey.Lighten3 : Colors.White)
                .Padding(4)
                .Text(t =>
                {
                    if (isHeader) t.DefaultTextStyle(s => s.SemiBold());
                    RenderInline(t, text);
                });
        }
    }
}
