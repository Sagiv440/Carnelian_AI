using System.Text;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>Unit tests for <see cref="TableExport"/> — HTML / TSV / CF_HTML rendering of a parsed table.</summary>
public sealed class TableExportTests
{
    private static TableData Sample() => MarkdownSegmenter.ParseTable(
        "| x | 0 | 1 |\n| --- | --- | --- |\n| 0 | 0 | 0 |\n| 1 | 0 | 1 |");

    [Fact]
    public void ToHtml_EmitsTableWithHeaderAndRows()
    {
        var html = TableExport.ToHtml(Sample());
        Assert.Contains("<table", html);
        Assert.Contains("<th>x</th>", html);
        Assert.Contains("<th>0</th>", html);
        Assert.Contains("<td>1</td>", html);
        Assert.EndsWith("</table>", html);
        // Two body rows.
        Assert.Equal(2, CountOccurrences(html, "<tr>") - 1); // minus the header row
    }

    [Fact]
    public void ToHtml_RendersInlineFormatting_AndEscapes()
    {
        var t = MarkdownSegmenter.ParseTable(
            "| Name | Note |\n|---|---|\n| **Bold** | a < b & c |\n| [link](https://x.com) | `code` |");
        var html = TableExport.ToHtml(t);
        Assert.Contains("<b>Bold</b>", html);
        Assert.Contains("a &lt; b &amp; c", html);          // HTML-escaped
        Assert.Contains("<a href=\"https://x.com\">link</a>", html);
        Assert.Contains("<code>code</code>", html);
    }

    [Fact]
    public void ToTsv_IsTabSeparated_WithHeaderFirst()
    {
        var tsv = TableExport.ToTsv(Sample());
        var lines = tsv.Split('\n');
        Assert.Equal(3, lines.Length);                       // header + 2 rows
        Assert.Equal("x\t0\t1", lines[0]);
        Assert.Equal("1\t0\t1", lines[2]);
    }

    [Fact]
    public void ToTsv_FlattensMarkdownToPlainText()
    {
        var t = MarkdownSegmenter.ParseTable("| A |\n|---|\n| **bold** |");
        var tsv = TableExport.ToTsv(t);
        Assert.Equal("A\nbold", tsv);                        // no ** markers
    }

    [Fact]
    public void WrapCfHtml_HasValidByteOffsets()
    {
        var html = TableExport.ToHtml(Sample());
        var cf = TableExport.WrapCfHtml(html);

        Assert.Contains("Version:0.9", cf);
        Assert.Contains("<!--StartFragment-->", cf);
        Assert.Contains("<!--EndFragment-->", cf);

        // The StartFragment/EndFragment byte offsets must bracket exactly the fragment HTML.
        var bytes = Encoding.UTF8.GetBytes(cf);
        var startFragment = ReadOffset(cf, "StartFragment:");
        var endFragment = ReadOffset(cf, "EndFragment:");
        var fragment = Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment);
        Assert.Equal(html, fragment);
    }

    private static int ReadOffset(string cf, string key)
    {
        var i = cf.IndexOf(key, System.StringComparison.Ordinal) + key.Length;
        return int.Parse(cf.Substring(i, 8));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
