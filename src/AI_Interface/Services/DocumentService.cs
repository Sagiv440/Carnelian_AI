using System;
using System.Collections.Generic;
using System.Linq;
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

    private enum Block { Heading1, Heading2, Heading3, Bullet, Paragraph, Blank }

    private static IEnumerable<(Block Kind, string Text)> Parse(string content)
    {
        foreach (var raw in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) { yield return (Block.Blank, ""); continue; }
            if (line.StartsWith("### ")) { yield return (Block.Heading3, line[4..]); continue; }
            if (line.StartsWith("## ")) { yield return (Block.Heading2, line[3..]); continue; }
            if (line.StartsWith("# ")) { yield return (Block.Heading1, line[2..]); continue; }
            if (line.StartsWith("- ") || line.StartsWith("* ")) { yield return (Block.Bullet, line[2..]); continue; }
            yield return (Block.Paragraph, line);
        }
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
            body.AppendChild(WordParagraph(kind, text));
            count++;
        }
        return count;
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

        var display = kind == Block.Bullet ? "•  " + text : text;
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
                            case Block.Heading1: col.Item().Text(text).FontSize(20).Bold(); break;
                            case Block.Heading2: col.Item().Text(text).FontSize(16).Bold(); break;
                            case Block.Heading3: col.Item().Text(text).FontSize(13).Bold(); break;
                            case Block.Bullet: col.Item().Text("•  " + text); break;
                            default: col.Item().Text(text); break;
                        }
                    }
                });
            });
        }).GeneratePdf(fullPath);

        return blocks.Count(b => b.Kind != Block.Blank);
    }
}
