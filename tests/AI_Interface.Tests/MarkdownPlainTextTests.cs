using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>Unit tests for <see cref="MarkdownPlainText.Render"/> — stripping markdown to clean plain text.</summary>
public sealed class MarkdownPlainTextTests
{
    [Fact]
    public void StripsInlineEmphasis_AndInlineCode()
    {
        Assert.Equal("Bold and italic and code.",
            MarkdownPlainText.Render("**Bold** and *italic* and `code`."));
    }

    [Fact]
    public void DropsHeadingHashes_KeepsText()
    {
        Assert.Equal("Title", MarkdownPlainText.Render("## Title"));
        Assert.Equal("Deep heading", MarkdownPlainText.Render("#### Deep heading"));
    }

    [Fact]
    public void Links_BecomeTheirLabel()
    {
        Assert.Equal("See the docs here.",
            MarkdownPlainText.Render("See [the docs](https://example.com) here."));
    }

    [Fact]
    public void Bullets_KeepAReadableMarker_NoAsterisks()
    {
        var outp = MarkdownPlainText.Render("* first\n* second");
        Assert.Equal("• first\n• second", outp);
    }

    [Fact]
    public void NumberedList_KeepsItsNumber()
    {
        Assert.Equal("1. Plan\n2. Execute", MarkdownPlainText.Render("1. **Plan**\n2. Execute"));
    }

    [Fact]
    public void FencedCode_KeepsContent_WithoutFencesOrBackticks()
    {
        var outp = MarkdownPlainText.Render("Run this:\n```bash\nsudo apt remove gimp\n```");
        Assert.Equal("Run this:\n\nsudo apt remove gimp", outp);
    }

    [Fact]
    public void Table_BecomesTabSeparatedRows_NoPipes()
    {
        var outp = MarkdownPlainText.Render("| A | B |\n|---|---|\n| 1 | 2 |");
        Assert.DoesNotContain("|", outp);
        Assert.Contains("A\tB", outp);
        Assert.Contains("1\t2", outp);
    }

    [Fact]
    public void RealReply_HasNoLeftoverMarkdownSymbols()
    {
        const string md =
            "**Uninstalling an Application**\n\n" +
            "1. **Check** the menu.\n" +
            "2. Use `sudo apt remove gimp`\n\n" +
            "* Removed config: `sudo rm -rf /usr/local/gimp/`\n";
        var outp = MarkdownPlainText.Render(md);

        Assert.DoesNotContain("**", outp);
        Assert.DoesNotContain("`", outp);
        Assert.Contains("Uninstalling an Application", outp);
        Assert.Contains("sudo apt remove gimp", outp);
        Assert.Contains("1. Check the menu.", outp);
    }
}
