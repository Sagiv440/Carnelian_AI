using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="McpToolName"/> — the pure namespacing helper that maps an MCP server tool to a
/// provider-safe, collision-free name (<c>mcp__&lt;id&gt;__&lt;tool&gt;</c>) and back. <c>internal static</c>,
/// reached via <c>InternalsVisibleTo</c>. Covers the round-trip, sanitization, the first-"__" split (so tool
/// names with underscores survive), and the 64-char length guard.
/// </summary>
public sealed class McpToolNameTests
{
    // ---- IsMcp ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("mcp__github__create_issue", true)]
    [InlineData("mcp__x__y", true)]
    [InlineData("read_file", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsMcp_DetectsTheNamespace(string? name, bool expected) =>
        Assert.Equal(expected, McpToolName.IsMcp(name));

    // ---- Make: basic shape ---------------------------------------------------------------------

    [Fact]
    public void Make_ComposesPrefixIdSeparatorTool()
    {
        Assert.Equal("mcp__github__create_issue", McpToolName.Make("github", "create_issue"));
    }

    [Fact]
    public void Make_SanitizesServerId_ToLowerKebab()
    {
        var name = McpToolName.Make("GitHub Issues", "list");
        Assert.Equal("mcp__github-issues__list", name);
    }

    [Fact]
    public void Make_SanitizesToolName_DisallowedCharsBecomeUnderscore()
    {
        // Spaces / dots are not in [A-Za-z0-9_-] → '_'. Underscores and hyphens are kept.
        var name = McpToolName.Make("srv", "do.a thing-now");
        Assert.Equal("mcp__srv__do_a_thing-now", name);
    }

    [Fact]
    public void Make_ProducesProviderSafeCharacterSet()
    {
        var name = McpToolName.Make("My Server!", "weird/tool name");
        Assert.Matches("^[A-Za-z0-9_-]+$", name);
    }

    // ---- TryParse: round-trip + first-"__" split -----------------------------------------------

    [Fact]
    public void TryParse_RoundTripsAName()
    {
        Assert.True(McpToolName.TryParse("mcp__github__create_issue", out var id, out var tool));
        Assert.Equal("github", id);
        Assert.Equal("create_issue", tool); // tool's single underscores survive (split is on the FIRST "__")
    }

    [Fact]
    public void TryParse_ToolWithDoubleUnderscore_KeptWholeBecauseIdHasNoUnderscores()
    {
        // The id is sanitized to have no underscores, so the first "__" after the prefix is the separator and
        // everything after it — including further "__" — is the tool name.
        var name = McpToolName.Make("srv", "a__b");
        Assert.True(McpToolName.TryParse(name, out var id, out var tool));
        Assert.Equal("srv", id);
        Assert.Equal("a__b", tool);
    }

    [Theory]
    [InlineData("read_file")]
    [InlineData("mcp__onlyid")]   // no separator after the id
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsNonNamespacedNames(string? name)
    {
        Assert.False(McpToolName.TryParse(name, out _, out _));
    }

    // ---- length guard --------------------------------------------------------------------------

    [Fact]
    public void Make_LongToolName_IsTruncatedWithinLimitAndStaysMcp()
    {
        var longTool = new string('a', 200);
        var name = McpToolName.Make("server", longTool);

        Assert.True(name.Length <= McpToolName.MaxLength);
        Assert.True(McpToolName.IsMcp(name));
        Assert.Matches("^[A-Za-z0-9_-]+$", name);
    }

    [Fact]
    public void Make_DistinctLongTools_ProduceDistinctNames()
    {
        var a = McpToolName.Make("server", new string('a', 100) + "_one");
        var b = McpToolName.Make("server", new string('a', 100) + "_two");
        Assert.NotEqual(a, b); // the appended hash keeps them distinct after truncation
    }
}
