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

    // -------------------------------------------------------------------------
    // TTS-specific coverage: verifies that SpeakMessage produces text that Piper
    // can read aloud without any raw markdown symbols being spoken.
    // Each test maps to one token type so a regression is easy to pinpoint.
    // -------------------------------------------------------------------------

    [Fact]
    public void Tts_BoldMarkers_AreStripped()
    {
        Assert.Equal("hello", MarkdownPlainText.Render("**hello**"));
    }

    [Fact]
    public void Tts_ItalicMarkers_AreStripped()
    {
        Assert.Equal("world", MarkdownPlainText.Render("*world*"));
    }

    [Fact]
    public void Tts_InlineCode_StripsBackticks()
    {
        Assert.Equal("code", MarkdownPlainText.Render("`code`"));
    }

    /// <summary>All six ATX heading levels should have their leading '#' characters removed.</summary>
    [Theory]
    [InlineData("# Title",        "Title")]
    [InlineData("## Title",       "Title")]
    [InlineData("### Title",      "Title")]
    [InlineData("#### Title",     "Title")]
    [InlineData("##### Title",    "Title")]
    [InlineData("###### Title",   "Title")]
    public void Tts_AtxHeading_HashesDropped(string input, string expected)
    {
        Assert.Equal(expected, MarkdownPlainText.Render(input));
    }

    [Fact]
    public void Tts_Link_CollapsesToLabel()
    {
        Assert.Equal("click here", MarkdownPlainText.Render("[click here](https://example.com)"));
    }

    [Fact]
    public void Tts_Strikethrough_IsStripped()
    {
        Assert.Equal("deleted", MarkdownPlainText.Render("~~deleted~~"));
    }

    /// <summary>Null and empty strings must both return "" so the Piper call is safe (no NRE, no crash).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Tts_NullOrEmpty_ReturnsSafeEmptyString(string? input)
    {
        Assert.Equal("", MarkdownPlainText.Render(input));
    }

    [Fact]
    public void Tts_RealisticReply_ProducesCleanSpeechText()
    {
        // Matches the exact example from the SpeakMessage fix description.
        const string md = "## Summary\n\n**Key point:** use *this* approach.";

        var result = MarkdownPlainText.Render(md);

        // No raw markdown symbols left that Piper would read aloud.
        Assert.DoesNotContain("#",  result);
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*",  result);
        Assert.DoesNotContain("`",  result);

        // The meaningful words survive.
        Assert.Contains("Summary",    result);
        Assert.Contains("Key point:", result);
        Assert.Contains("use",        result);
        Assert.Contains("this",       result);
        Assert.Contains("approach",   result);
    }
}
