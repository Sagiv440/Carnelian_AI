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

    [Fact]
    public void Strikethrough_IsExtracted()
    {
        var span = One("~~gone~~");
        Assert.Equal("gone", span.Text);
        Assert.Equal(InlineStyle.Strikethrough, span.Style);

        // Space-padded tildes stay literal.
        Assert.Equal(InlineStyle.Normal, One("a ~~ b").Style);
    }

    [Fact]
    public void Link_CapturesLabelAndHref()
    {
        var span = One("[Google](https://google.com)");
        Assert.Equal("Google", span.Text);
        Assert.Equal("https://google.com", span.Href);
    }

    [Fact]
    public void Link_InAParagraph_SplitsAroundIt()
    {
        var spans = InlineMarkdown.Parse("see [docs](http://x) now").ToList();
        Assert.Collection(spans,
            s => { Assert.Equal("see ", s.Text); Assert.Null(s.Href); },
            s => { Assert.Equal("docs", s.Text); Assert.Equal("http://x", s.Href); },
            s => { Assert.Equal(" now", s.Text); Assert.Null(s.Href); });
    }

    [Fact]
    public void NonLinkBrackets_StayLiteral()
    {
        // No "(url)" follows → not a link.
        var span = One("an [array] index");
        Assert.Equal("an [array] index", span.Text);
        Assert.Null(span.Href);
    }

    [Fact]
    public void EmptyLabelOrHref_StaysLiteral()
    {
        Assert.Null(One("[](foo)").Href);    // empty label → not a link
        Assert.Null(One("[label]()").Href);  // empty href → not a link
    }

    [Fact]
    public void BareUrl_IsAutoLinked()
    {
        var span = One("https://example.com/page");
        Assert.Equal("https://example.com/page", span.Text);
        Assert.Equal("https://example.com/page", span.Href);
    }

    [Fact]
    public void BareUrl_InASentence_ExcludesTrailingPunctuation()
    {
        var spans = InlineMarkdown.Parse("see https://example.com.").ToList();
        Assert.Collection(spans,
            s => { Assert.Equal("see ", s.Text); Assert.Null(s.Href); },
            s => { Assert.Equal("https://example.com", s.Text); Assert.Equal("https://example.com", s.Href); },
            s => { Assert.Equal(".", s.Text); Assert.Null(s.Href); });
    }

    [Fact]
    public void PlainWordStartingWithH_IsNotALink()
    {
        var span = One("however this is fine");
        Assert.Equal("however this is fine", span.Text);
        Assert.Null(span.Href);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:a@b.com", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedLinkScheme_OnlyHttpHttpsMailto(string? href, bool allowed) =>
        Assert.Equal(allowed, InlineMarkdown.IsAllowedLinkScheme(href));
}
