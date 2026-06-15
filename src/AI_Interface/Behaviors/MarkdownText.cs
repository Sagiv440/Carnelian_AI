using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using AI_Interface.ViewModels;

namespace AI_Interface.Behaviors;

/// <summary>
/// Attached property that renders a Markdown <b>prose</b> string into a <see cref="TextBlock"/>'s
/// <see cref="TextBlock.Inlines"/> with inline formatting — <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>,
/// <c>~~strikethrough~~</c>, and clickable <c>[text](url)</c> links — instead of showing the raw symbols.
/// Set <c>beh:MarkdownText.Text="{Binding ...}"</c> in place of the usual <c>Text</c> binding; it re-parses
/// on every change (so a streaming reply updates in place). The parse itself is the pure, unit-tested
/// <see cref="InlineMarkdown"/>; this class is only the thin Avalonia glue (hence it lives in the view layer).
/// </summary>
public static class MarkdownText
{
    private static readonly FontFamily CodeFont = new("Cascadia Code,Consolas,monospace");
    private static readonly char[] InlineMarkers = { '*', '`', '~', '[' };

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(MarkdownText));

    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);
    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);

    static MarkdownText()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) => Apply(tb, e.NewValue as string));
    }

    private static void Apply(TextBlock tb, string? text)
    {
        var inlines = tb.Inlines;
        if (inlines is null)
        {
            inlines = new InlineCollection();
            tb.Inlines = inlines;
        }
        inlines.Clear();

        // Fast path: most prose has no inline markers — skip the parse and emit one plain run. This also
        // avoids re-tokenising (and, for links, recreating the embedded control) on every streamed delta.
        if (string.IsNullOrEmpty(text) || text.IndexOfAny(InlineMarkers) < 0)
        {
            if (!string.IsNullOrEmpty(text))
                inlines.Add(new Run(text));
            ApplyFlowDirection(tb, text);
            return;
        }

        foreach (var span in InlineMarkdown.Parse(text))
        {
            // A link only renders as a clickable control when its scheme is safe (http/https/mailto);
            // otherwise it falls through and renders as plain label text.
            if (span.Href is { Length: > 0 } href && InlineMarkdown.IsAllowedLinkScheme(href))
            {
                inlines.Add(BuildLink(tb, span.Text, href));
                continue;
            }

            var run = new Run(span.Text);
            switch (span.Style)
            {
                case InlineStyle.Bold:
                    run.FontWeight = FontWeight.Bold;
                    break;
                case InlineStyle.Italic:
                    run.FontStyle = FontStyle.Italic;
                    break;
                case InlineStyle.BoldItalic:
                    run.FontWeight = FontWeight.Bold;
                    run.FontStyle = FontStyle.Italic;
                    break;
                case InlineStyle.Code:
                    run.FontFamily = CodeFont;
                    break;
                case InlineStyle.Strikethrough:
                    run.TextDecorations = TextDecorations.Strikethrough;
                    break;
            }
            inlines.Add(run);
        }

        ApplyFlowDirection(tb, text);
    }

    /// <summary>
    /// Sets <see cref="TextBlock.FlowDirection"/> (and the parent <see cref="Grid"/>'s direction for
    /// list items) based on the first strong directional character in the text. Keeping this inside the
    /// behavior avoids a separate XAML binding that would fire <see cref="Avalonia.AvaloniaProperty"/>
    /// change notifications and interfere with Inline rendering on Avalonia 12's SelectableTextBlock.
    /// </summary>
    private static void ApplyFlowDirection(TextBlock tb, string? text)
    {
        var dir = ViewModels.RtlHelper.IsRtl(text)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        tb.FlowDirection = dir;
        // List-item SelectableTextBlock lives inside a two-column Grid (bullet + text).
        // Setting the Grid's direction flips the bullet marker to the correct side for RTL.
        if (tb.Parent is Grid g)
            g.FlowDirection = dir;
    }

    /// <summary>A clickable, accent-coloured, underlined link hosted in the text flow.</summary>
    private static InlineUIContainer BuildLink(TextBlock host, string label, string href)
    {
        var brush = host.TryFindResource("AppAccentBrush", out var res) && res is IBrush b
            ? b
            : Brushes.SteelBlue;

        var link = new TextBlock
        {
            Text = label,
            Foreground = brush,
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(link, href);
        // No detach needed: the handler's only root is this TextBlock, which is dropped on the next
        // inlines.Clear() (so it's GC'd) — re-subscribing per rebuild does not leak.
        link.PointerPressed += (_, _) => OpenUrl(href);
        return new InlineUIContainer(link);
    }

    private static void OpenUrl(string url)
    {
        if (!InlineMarkdown.IsAllowedLinkScheme(url))
            return; // defence in depth — only http/https/mailto links are launchable
        try
        {
            // UseShellExecute lets the OS pick the default browser (mirrors MainWindowViewModel.OpenUrl).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening a link should never crash the app.
        }
    }
}
