using System;
using System.Collections.Generic;
using System.Linq;
using AI_Interface.ViewModels;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for the composer's pure slash-command palette helpers in <see cref="SlashMenu"/>
/// (<c>ShouldOpen</c> / <c>ExtractQuery</c> / <c>Filter</c>) and <see cref="SlashCommand.Display"/>.
/// All are deterministic and I/O-free: <c>ShouldOpen</c> decides when the menu is showing, <c>ExtractQuery</c>
/// pulls the lowercased query after the leading '/', and <c>Filter</c> ranks command names prefix-first
/// (stable input order) then substring (stable input order). Reachable from the test assembly via
/// <c>[assembly: InternalsVisibleTo("AI_Interface.Tests")]</c>.
/// </summary>
public sealed class SlashMenuTests
{
    // A no-op Run satisfies the required Action; IsAvailable defaults to always-true.
    private static SlashCommand Cmd(string name) =>
        new() { Name = name, Description = "", Run = () => { } };

    // Fixed sample list in this exact order: chat, compact, clear, research, web, new.
    private static IReadOnlyList<SlashCommand> Sample() => new List<SlashCommand>
    {
        Cmd("chat"),
        Cmd("compact"),
        Cmd("clear"),
        Cmd("research"),
        Cmd("web"),
        Cmd("new"),
    };

    private static string[] Names(IEnumerable<SlashCommand> commands) =>
        commands.Select(c => c.Name).ToArray();

    // ---- ShouldOpen ----------------------------------------------------------------------------

    [Theory]
    [InlineData("/")]            // bare slash opens (lists everything)
    [InlineData("/c")]
    [InlineData("/compact")]
    [InlineData("/auto-read")]   // hyphen is not whitespace
    public void ShouldOpen_SlashTokenWithoutWhitespace_ReturnsTrue(string input)
    {
        Assert.True(SlashMenu.ShouldOpen(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]        // no leading slash
    [InlineData("hi /there")]    // slash not at start
    [InlineData("/comp ")]       // trailing space (typing a real message)
    [InlineData("/a b")]         // internal space
    [InlineData(" /comp")]       // leading space -> first char isn't '/'
    [InlineData("/line\nbreak")] // newline is whitespace
    [InlineData("/tab\tx")]      // tab is whitespace
    public void ShouldOpen_NotASlashToken_ReturnsFalse(string? input)
    {
        Assert.False(SlashMenu.ShouldOpen(input));
    }

    // ---- ExtractQuery --------------------------------------------------------------------------

    [Theory]
    [InlineData("/Compact", "compact")] // lowercased
    [InlineData("/WEB", "web")]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("hello", "")]           // no leading slash -> empty
    public void ExtractQuery_VariousInputs_ReturnsLowercasedTail(string? input, string expected)
    {
        Assert.Equal(expected, SlashMenu.ExtractQuery(input));
    }

    // ---- Filter: empty / null query returns all in input order ---------------------------------

    [Fact]
    public void Filter_EmptyQuery_ReturnsAllInInputOrder()
    {
        var result = SlashMenu.Filter(Sample(), "");
        Assert.Equal(new[] { "chat", "compact", "clear", "research", "web", "new" }, Names(result));
    }

    [Fact]
    public void Filter_NullQuery_ReturnsAllInInputOrder()
    {
        var result = SlashMenu.Filter(Sample(), null);
        Assert.Equal(new[] { "chat", "compact", "clear", "research", "web", "new" }, Names(result));
    }

    // ---- Filter: null / empty command list -> empty result -------------------------------------

    [Fact]
    public void Filter_NullCommands_ReturnsEmpty()
    {
        var result = SlashMenu.Filter(null, "c");
        Assert.Empty(result);
    }

    [Fact]
    public void Filter_EmptyCommands_ReturnsEmpty()
    {
        var result = SlashMenu.Filter(new List<SlashCommand>(), "c");
        Assert.Empty(result);
    }

    // ---- Filter: prefix-before-substring, stable -----------------------------------------------

    [Fact]
    public void Filter_QueryC_PrefixMatchesThenSubstring_InStableOrder()
    {
        // Prefix (StartsWith 'c'): chat, compact, clear -> in input order.
        // Substring (contains 'c' but not prefix): research. web/new excluded (no 'c').
        var result = SlashMenu.Filter(Sample(), "c");
        Assert.Equal(new[] { "chat", "compact", "clear", "research" }, Names(result));
    }

    [Fact]
    public void Filter_QueryCl_OnlyClear()
    {
        // Prefix: clear. No other name contains "cl".
        var result = SlashMenu.Filter(Sample(), "cl");
        Assert.Equal(new[] { "clear" }, Names(result));
    }

    [Fact]
    public void Filter_QueryRe_OnlyResearch()
    {
        // Prefix: research. No other name contains "re".
        var result = SlashMenu.Filter(Sample(), "re");
        Assert.Equal(new[] { "research" }, Names(result));
    }

    [Fact]
    public void Filter_QueryEa_PureSubstringMatchesInInputOrder()
    {
        // No name starts with "ea" (prefix bucket empty). Substring (contains "ea") in input order:
        // clear (cl-EA-r) then research (res-EA-rch). web/new/chat/compact have no "ea".
        var result = SlashMenu.Filter(Sample(), "ea");
        Assert.Equal(new[] { "clear", "research" }, Names(result));
    }

    [Fact]
    public void Filter_QueryE_AllSubstringMatchesInInputOrder()
    {
        // No name starts with 'e' (prefix bucket empty). Substring (contains 'e') in input order:
        // chat=no, compact=no, clear=yes, research=yes, web=yes, new=yes.
        var result = SlashMenu.Filter(Sample(), "e");
        Assert.Equal(new[] { "clear", "research", "web", "new" }, Names(result));
    }

    [Fact]
    public void Filter_QueryTrimmedAndCaseInsensitive_BehavesLikeC()
    {
        // "  C  " is trimmed and lowercased to "c" -> same result as the bare "c" case.
        var result = SlashMenu.Filter(Sample(), "  C  ");
        Assert.Equal(new[] { "chat", "compact", "clear", "research" }, Names(result));
    }

    // ---- SlashCommand.Display ------------------------------------------------------------------

    [Theory]
    [InlineData("compact", "/compact")]
    [InlineData("auto-read", "/auto-read")]
    public void Display_PrependsLeadingSlash(string name, string expected)
    {
        Assert.Equal(expected, Cmd(name).Display);
    }
}
