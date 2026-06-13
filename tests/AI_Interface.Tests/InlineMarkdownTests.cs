using System.Linq;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="InlineMarkdown.Parse"/> — the pure tokenizer that turns a prose string into
/// styled runs (**bold** / *italic* / ***both*** / `code`), conservatively leaving identifiers with
/// underscores and stray asterisks literal.
/// </summary>
public sealed class InlineMarkdownTests
{
    private static InlineSpan One(string text)
    {
        var spans = InlineMarkdown.Parse(text);
        return Assert.Single(spans);
    }

    [Fact]
    public void NullOrEmpty_ReturnsNoSpans()
    {
        Assert.Empty(InlineMarkdown.Parse(null));
        Assert.Empty(InlineMarkdown.Parse(""));
    }

    [Fact]
    public void PlainText_IsASingleNormalSpan()
    {
        var span = One("just some prose.");
        Assert.Equal("just some prose.", span.Text);
        Assert.Equal(InlineStyle.Normal, span.Style);
    }

    [Fact]
    public void Bold_IsExtracted_WithoutTheAsterisks()
    {
        var span = One("**Task Brief:**");
        Assert.Equal("Task Brief:", span.Text);
        Assert.Equal(InlineStyle.Bold, span.Style);
    }

    [Fact]
    public void Italic_AndBoldItalic_AndCode()
    {
        Assert.Equal(InlineStyle.Italic, One("*hi*").Style);
        Assert.Equal(InlineStyle.BoldItalic, One("***hi***").Style);

        var code = One("`assistant`");
        Assert.Equal("assistant", code.Text);
        Assert.Equal(InlineStyle.Code, code.Style);
    }

    [Fact]
    public void MixedLine_SplitsIntoOrderedRuns()
    {
        var spans = InlineMarkdown.Parse("Hello **world** and `x` done").ToList();

        Assert.Collection(spans,
            s => { Assert.Equal("Hello ", s.Text); Assert.Equal(InlineStyle.Normal, s.Style); },
            s => { Assert.Equal("world", s.Text); Assert.Equal(InlineStyle.Bold, s.Style); },
            s => { Assert.Equal(" and ", s.Text); Assert.Equal(InlineStyle.Normal, s.Style); },
            s => { Assert.Equal("x", s.Text); Assert.Equal(InlineStyle.Code, s.Style); },
            s => { Assert.Equal(" done", s.Text); Assert.Equal(InlineStyle.Normal, s.Style); });
    }

    [Theory]
    [InlineData("run_command")]          // single underscore — identifier, not italic
    [InlineData("a_b_c")]                // multiple underscores — still not emphasis
    [InlineData(".AI/AI_DOCS.md")]
    public void Underscores_AreNeverEmphasis(string text)
    {
        var span = One(text);
        Assert.Equal(text, span.Text);
        Assert.Equal(InlineStyle.Normal, span.Style);
    }

    [Fact]
    public void SpacePaddedAsterisks_StayLiteral()
    {
        // "2 * 3" (multiplication) and "** bold **" (padded) must not become emphasis.
        Assert.Equal(InlineStyle.Normal, One("2 * 3 = 6").Style);
        Assert.Equal(InlineStyle.Normal, One("** bold **").Style);
    }

    [Fact]
    public void UnmatchedDelimiters_StayLiteral()
    {
        Assert.Equal("a * b", One("a * b").Text);
        Assert.Equal("`unclosed", One("`unclosed").Text);
        Assert.Equal("**unclosed bold", One("**unclosed bold").Text);
    }
}
