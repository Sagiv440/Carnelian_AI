using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AI_Interface.ViewModels;

/// <summary>
/// Renders a Markdown message to clean <b>plain text</b> — the same content shown on screen but with the
/// formatting markers removed: <c>**bold**</c>/<c>*italic*</c>/<c>`code`</c>/<c>~~strike~~</c> → their text,
/// <c>[label](url)</c> → <c>label</c>, ATX <c>#</c> heading markers dropped, list bullets kept as "• " /
/// "1.", fenced code shown without the ``` fences, and tables flattened to tab-separated rows. Used by the
/// message "Copy" button so a paste lands as readable prose, not raw symbols. Pure — reuses the unit-tested
/// <see cref="MarkdownSegmenter"/>, <see cref="InlineMarkdown"/>, and <see cref="TableExport"/>.
/// </summary>
internal static class MarkdownPlainText
{
    public static string Render(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var blocks = new List<string>();
        var list = new List<string>();

        void FlushList()
        {
            if (list.Count == 0) return;
            blocks.Add(string.Join("\n", list));
            list.Clear();
        }

        foreach (var part in MarkdownSegmenter.Parse(text))
        {
            switch (part.Kind)
            {
                case SegmentKind.Bullet:
                case SegmentKind.Numbered:
                    list.Add($"{part.Marker} {Inline(part.Text)}");
                    break;

                case SegmentKind.Code:
                    FlushList();
                    blocks.Add(part.Text);           // verbatim — don't strip inside code
                    break;

                case SegmentKind.Table:
                    FlushList();
                    blocks.Add(TableExport.ToTsv(MarkdownSegmenter.ParseTable(part.Text)));
                    break;

                case SegmentKind.Divider:
                    FlushList();                     // the rule itself carries no text — drop it
                    break;

                default:                             // Paragraph + headings
                    FlushList();
                    blocks.Add(Inline(part.Text));
                    break;
            }
        }
        FlushList();

        return string.Join("\n\n", blocks).Trim();
    }

    /// <summary>Strips inline markers from a (possibly multi-line) prose string, keeping link labels.</summary>
    private static string Inline(string text) =>
        string.Concat(InlineMarkdown.Parse(text).Select(s => s.Text));
}
