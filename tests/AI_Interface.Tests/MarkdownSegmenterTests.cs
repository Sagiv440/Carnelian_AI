using System.Linq;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="MarkdownSegmenter.Parse"/> — splitting a reply into block-level parts:
/// paragraphs, headings, bullet/numbered list items, horizontal dividers, and fenced code blocks.
/// </summary>
public sealed class MarkdownSegmenterTests
{
    private static MarkdownSegmenter.Part Single(string text) => Assert.Single(MarkdownSegmenter.Parse(text));

    [Fact]
    public void Headings_MapToLevels_AndCapAtThree()
    {
        Assert.Equal(SegmentKind.Heading1, Single("# Title").Kind);
        Assert.Equal(SegmentKind.Heading2, Single("## Sub").Kind);
        Assert.Equal(SegmentKind.Heading3, Single("### Deep").Kind);
        Assert.Equal(SegmentKind.Heading3, Single("##### Deeper").Kind); // 4-6 # still render as H3

        var h = Single("##  Spaced Title  ");
        Assert.Equal("Spaced Title", h.Text); // marker + surrounding space stripped
    }

    [Fact]
    public void HashWithoutSpace_IsNotAHeading()
    {
        var p = Single("#NotAHeading");
        Assert.Equal(SegmentKind.Paragraph, p.Kind);
    }

    [Fact]
    public void Bullets_BecomeBulletParts_WithADotlessMarker()
    {
        var parts = MarkdownSegmenter.Parse("- first\n- second\n* third\n+ fourth");
        Assert.Equal(4, parts.Count);
        Assert.All(parts, p => Assert.Equal(SegmentKind.Bullet, p.Kind));
        Assert.All(parts, p => Assert.Equal("•", p.Marker));
        Assert.Equal(new[] { "first", "second", "third", "fourth" }, parts.Select(p => p.Text));
    }

    [Fact]
    public void NumberedItems_KeepTheirNumber_AsAMarker()
    {
        var parts = MarkdownSegmenter.Parse("1. Explore\n2. Implement\n3) Verify");
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.Equal(SegmentKind.Numbered, p.Kind));
        Assert.Equal(new[] { "1.", "2.", "3." }, parts.Select(p => p.Marker));
        Assert.Equal(new[] { "Explore", "Implement", "Verify" }, parts.Select(p => p.Text));
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    public void Dividers_AreRecognised(string line) =>
        Assert.Equal(SegmentKind.Divider, Single(line).Kind);

    [Fact]
    public void BoldLine_IsNotADivider()
    {
        // "***word***" has letters, so it's a paragraph (bold-italic handled inline), not a rule.
        Assert.Equal(SegmentKind.Paragraph, Single("***important***").Kind);
    }

    [Fact]
    public void ConsecutiveLines_AreOneParagraph_BlankLineSplitsThem()
    {
        var parts = MarkdownSegmenter.Parse("line one\nline two\n\nsecond para");
        Assert.Collection(parts,
            p => { Assert.Equal(SegmentKind.Paragraph, p.Kind); Assert.Equal("line one\nline two", p.Text); },
            p => { Assert.Equal(SegmentKind.Paragraph, p.Kind); Assert.Equal("second para", p.Text); });
    }

    [Fact]
    public void FencedCode_BecomesACodeBlock_WithLanguage()
    {
        var part = Single("```bash\necho hi\n```");
        Assert.Equal(SegmentKind.Code, part.Kind);
        Assert.Equal("bash", part.Language);
        Assert.Equal("echo hi", part.Text);
    }

    [Fact]
    public void MixedDocument_SplitsIntoOrderedBlocks()
    {
        var parts = MarkdownSegmenter.Parse("# Plan\n\nDo these:\n- a\n- b\n\n---\n\nDone.");
        Assert.Collection(parts,
            p => Assert.Equal(SegmentKind.Heading1, p.Kind),
            p => { Assert.Equal(SegmentKind.Paragraph, p.Kind); Assert.Equal("Do these:", p.Text); },
            p => Assert.Equal(SegmentKind.Bullet, p.Kind),
            p => Assert.Equal(SegmentKind.Bullet, p.Kind),
            p => Assert.Equal(SegmentKind.Divider, p.Kind),
            p => { Assert.Equal(SegmentKind.Paragraph, p.Kind); Assert.Equal("Done.", p.Text); });
    }

    [Fact]
    public void Empty_ReturnsNoParts()
    {
        Assert.Empty(MarkdownSegmenter.Parse(null));
        Assert.Empty(MarkdownSegmenter.Parse(""));
    }

    [Fact]
    public void Table_HeaderPlusSeparator_BecomesOneTableBlock()
    {
        var md = "| Source | Hydration |\n|--------|-----------|\n| Mary Berry | 65% |\n| Ooni | 60% |";
        var part = Single(md);
        Assert.Equal(SegmentKind.Table, part.Kind);

        var table = MarkdownSegmenter.ParseTable(part.Text);
        Assert.Equal(new[] { "Source", "Hydration" }, table.Header);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new[] { "Mary Berry", "65%" }, table.Rows[0]);
        Assert.Equal(new[] { "Ooni", "60%" }, table.Rows[1]);
    }

    [Fact]
    public void Table_AlignmentColons_AreAccepted_AndShortRowsPad()
    {
        var md = "| A | B | C |\n| :--- | :---: | ---: |\n| 1 | 2 |";
        var part = Single(md);
        Assert.Equal(SegmentKind.Table, part.Kind);

        var table = MarkdownSegmenter.ParseTable(part.Text);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(new[] { "1", "2", "" }, table.Rows[0]); // padded to 3 columns
    }

    [Fact]
    public void PipeLine_WithoutSeparator_IsJustAParagraph()
    {
        var part = Single("| not | a table |\nsome prose");
        Assert.Equal(SegmentKind.Paragraph, part.Kind);
    }

    [Fact]
    public void Table_SurroundedByProse_SplitsCleanly()
    {
        var md = "Before.\n\n| H1 | H2 |\n|----|----|\n| a | b |\n\nAfter.";
        var parts = MarkdownSegmenter.Parse(md);
        Assert.Collection(parts,
            p => { Assert.Equal(SegmentKind.Paragraph, p.Kind); Assert.Equal("Before.", p.Text); },
            p => Assert.Equal(SegmentKind.Table, p.Kind),
            p => { Assert.Equal(SegmentKind.Paragraph, p.Kind); Assert.Equal("After.", p.Text); });
    }

    [Fact]
    public void IsTableSeparator_DistinguishesRules()
    {
        Assert.True(MarkdownSegmenter.IsTableSeparator("|---|---|"));
        Assert.True(MarkdownSegmenter.IsTableSeparator("| :-- | --: |"));
        Assert.False(MarkdownSegmenter.IsTableSeparator("---"));          // no pipes → horizontal rule
        Assert.False(MarkdownSegmenter.IsTableSeparator("| a | b |"));    // text cells, not dashes
    }
}
