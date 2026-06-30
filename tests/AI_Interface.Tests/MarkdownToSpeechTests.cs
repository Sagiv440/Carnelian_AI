using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>Unit tests for <see cref="MarkdownToSpeech.Render"/> — TTS rendering with comma pauses.</summary>
public sealed class MarkdownToSpeechTests
{
    // ── pause insertion ────────────────────────────────────────────────────────

    [Fact]
    public void Bold_InMiddleOfSentence_GetsCommasPausesBothSides()
    {
        // "Hello **world** there" → "Hello, world, there"
        var result = MarkdownToSpeech.Render("Hello **world** there");
        Assert.Equal("Hello, world, there", result);
    }

    [Fact]
    public void Bold_AtStartOfSentence_GetsTrailingPauseOnly()
    {
        // "**Key point:** rest" — no leading comma before first word
        var result = MarkdownToSpeech.Render("**Key point:** rest");
        Assert.Contains("Key point:", result);
        Assert.False(result.StartsWith(","), $"Result should not start with a comma but was: {result}");
        // comma (pause) appears after bold span
        Assert.Contains(",", result);
    }

    [Fact]
    public void BoldItalic_AlsoGetsPauses()
    {
        var result = MarkdownToSpeech.Render("Start ***crucial*** end");
        Assert.Contains(",", result);
        Assert.Contains("crucial", result);
        Assert.DoesNotContain("***", result);
    }

    [Fact]
    public void Bold_AlreadyEndingWithPunctuation_DoesNotDoublePunctuate()
    {
        // "**Done.**" → "Done.," would be ugly — the period should satisfy the pause
        var result = MarkdownToSpeech.Render("**Done.**");
        Assert.DoesNotContain(".,", result);
        Assert.Contains("Done.", result);
    }

    [Fact]
    public void Bold_AdjacentBoldSpans_NoDuplicateCommas()
    {
        // "**A** **B**" — two consecutive bold spans
        var result = MarkdownToSpeech.Render("**A** **B**");
        Assert.DoesNotContain(",,", result);
        Assert.Contains("A", result);
        Assert.Contains("B", result);
    }

    // ── markdown still stripped (same guarantees as MarkdownPlainText) ─────────

    [Fact]
    public void Italic_IsStripped_NoPauseAdded()
    {
        // Italic gets no special pause — just stripped
        var result = MarkdownToSpeech.Render("*soft*");
        Assert.Equal("soft", result);
    }

    [Fact]
    public void Headings_AreStripped_NoHashes()
    {
        var result = MarkdownToSpeech.Render("## My Section");
        Assert.DoesNotContain("#", result);
        Assert.Contains("My Section", result);
    }

    [Fact]
    public void Links_CollapseToLabel()
    {
        Assert.Equal("click here", MarkdownToSpeech.Render("[click here](https://example.com)"));
    }

    [Fact]
    public void NullAndEmpty_ReturnSafeEmptyString()
    {
        Assert.Equal("", MarkdownToSpeech.Render(null));
        Assert.Equal("", MarkdownToSpeech.Render(""));
    }

    // ── realistic reply ────────────────────────────────────────────────────────

    [Fact]
    public void RealisticReply_BoldPausesInserted_NoRawMarkers()
    {
        const string md =
            "## Summary\n\n" +
            "**Key point:** use *this* approach.\n\n" +
            "1. **First** do the thing.\n" +
            "2. Then clean up.";

        var result = MarkdownToSpeech.Render(md);

        Assert.DoesNotContain("##", result);
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*", result);
        Assert.Contains("Key point:", result);
        Assert.Contains("First", result);
        // At least one comma pause was inserted for bold text
        Assert.Contains(",", result);
    }
}
