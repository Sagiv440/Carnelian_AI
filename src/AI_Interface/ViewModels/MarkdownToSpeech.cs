using System.Collections.Generic;
using System.Text;

namespace AI_Interface.ViewModels;

/// <summary>
/// Renders Markdown to speech-friendly plain text for Piper / espeak-ng.
/// Like <see cref="MarkdownPlainText"/> but inserts comma pauses around bold spans so
/// emphasized content sounds naturally set apart when read aloud —
/// e.g. <c>Hello **world** there</c> → <c>Hello, world, there</c>.
/// The copy button still uses <see cref="MarkdownPlainText.Render"/>; this class is TTS-only.
/// </summary>
internal static class MarkdownToSpeech
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
                    blocks.Add(part.Text);           // verbatim — don't alter code for TTS
                    break;

                case SegmentKind.Table:
                    FlushList();
                    blocks.Add(TableExport.ToTsv(MarkdownSegmenter.ParseTable(part.Text)));
                    break;

                case SegmentKind.Divider:
                    FlushList();
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

    private static string Inline(string text)
    {
        var sb = new StringBuilder();
        foreach (var span in InlineMarkdown.Parse(text))
        {
            bool emphasized = span.Style is InlineStyle.Bold or InlineStyle.BoldItalic;
            if (emphasized)
                InsertPauseBefore(sb);
            sb.Append(span.Text);
            if (emphasized)
                AppendPauseAfter(sb);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Inserts a comma BEFORE any trailing space so the space ends up after the pause marker:
    /// "Hello " → "Hello, " rather than "Hello ," or "Hello,  " (double space).
    /// </summary>
    private static void InsertPauseBefore(StringBuilder sb)
    {
        if (sb.Length == 0)
            return;
        // Walk back past trailing spaces to find the real last character.
        int insertAt = sb.Length;
        while (insertAt > 0 && sb[insertAt - 1] == ' ')
            insertAt--;
        if (insertAt == 0)
            return;
        var last = sb[insertAt - 1];
        if (last is not (',' or '.' or '!' or '?' or ';'))
            sb.Insert(insertAt, ',');
    }

    /// <summary>
    /// Appends a comma pause after bold text.
    /// The following span's own leading space provides the separator — no extra space added here.
    /// </summary>
    private static void AppendPauseAfter(StringBuilder sb)
    {
        if (sb.Length == 0)
            return;
        var last = sb[sb.Length - 1];
        if (last is not (',' or '.' or '!' or '?' or ';'))
            sb.Append(',');
    }
}
