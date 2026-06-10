using System.Linq;
using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Unit tests for <see cref="McpConfigStore.Parse"/> — the pure parser for a project's Claude-Code-style
/// <c>.AI/mcp.json</c> (<c>{ "mcpServers": { "&lt;name&gt;": {...} } }</c>). Covers stdio vs HTTP inference,
/// the explicit <c>type</c>, id sanitization, the enabled flag, and tolerance of malformed input.
/// </summary>
public sealed class McpConfigStoreTests
{
    [Fact]
    public void Parse_StdioEntry_MapsCommandArgsEnv()
    {
        const string json = """
        { "mcpServers": { "filesystem": {
            "command": "npx",
            "args": ["-y", "@modelcontextprotocol/server-filesystem", "/data"],
            "env": { "TOKEN": "abc" } } } }
        """;
        var s = Assert.Single(McpConfigStore.Parse(json));
        Assert.Equal("filesystem", s.Name);
        Assert.Equal("filesystem", s.Id);
        Assert.Equal(McpTransport.Stdio, s.Transport);
        Assert.Equal("npx", s.Command);
        Assert.Equal(new[] { "-y", "@modelcontextprotocol/server-filesystem", "/data" }, s.Args);
        Assert.Equal("abc", s.Env["TOKEN"]);
    }

    [Fact]
    public void Parse_UrlEntry_InfersHttpTransport()
    {
        const string json = """
        { "mcpServers": { "remote": { "url": "https://example.com/mcp",
            "headers": { "Authorization": "Bearer x" } } } }
        """;
        var s = Assert.Single(McpConfigStore.Parse(json));
        Assert.Equal(McpTransport.Http, s.Transport);
        Assert.Equal("https://example.com/mcp", s.Url);
        Assert.Equal("Bearer x", s.Headers["Authorization"]);
    }

    [Fact]
    public void Parse_ExplicitType_OverridesInference()
    {
        // type:http with no url is still HTTP (but won't be runnable until a url is added).
        var s = Assert.Single(McpConfigStore.Parse(
            """{ "mcpServers": { "r": { "type": "sse", "url": "https://h" } } }"""));
        Assert.Equal(McpTransport.Http, s.Transport);
    }

    [Fact]
    public void Parse_IdIsSanitizedFromName()
    {
        var s = Assert.Single(McpConfigStore.Parse(
            """{ "mcpServers": { "My Server!": { "command": "x" } } }"""));
        Assert.Equal("My Server!", s.Name);
        Assert.Equal("my-server", s.Id);
    }

    [Fact]
    public void Parse_EnabledFalse_IsHonored()
    {
        var s = Assert.Single(McpConfigStore.Parse(
            """{ "mcpServers": { "x": { "command": "c", "enabled": false } } }"""));
        Assert.False(s.Enabled);
    }

    [Fact]
    public void Parse_EntryWithNeitherCommandNorUrl_IsSkipped()
    {
        Assert.Empty(McpConfigStore.Parse("""{ "mcpServers": { "bad": { "env": {} } } }"""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "mcpServers": [] }""")]
    [InlineData("""{ "other": { "x": {} } }""")]
    public void Parse_MalformedOrEmpty_ReturnsEmpty(string json)
    {
        Assert.Empty(McpConfigStore.Parse(json));
    }

    [Fact]
    public void Serialize_RoundTripsThroughParse()
    {
        var original = new[]
        {
            new McpServerConfig
            {
                Id = "filesystem", Name = "filesystem", Transport = McpTransport.Stdio,
                Command = "npx", Args = { "-y", "@modelcontextprotocol/server-filesystem", "/data" },
                Env = { ["TOKEN"] = "abc" }, Enabled = true, AutoApprove = true
            },
            new McpServerConfig
            {
                Id = "remote", Name = "remote", Transport = McpTransport.Http,
                Url = "https://example.com/mcp", Headers = { ["Authorization"] = "Bearer x" }, Enabled = false
            }
        };

        var parsed = McpConfigStore.Parse(McpConfigStore.Serialize(original));

        Assert.Equal(2, parsed.Count);

        var fs = parsed.Single(s => s.Id == "filesystem");
        Assert.Equal(McpTransport.Stdio, fs.Transport);
        Assert.Equal("npx", fs.Command);
        Assert.Equal(new[] { "-y", "@modelcontextprotocol/server-filesystem", "/data" }, fs.Args);
        Assert.Equal("abc", fs.Env["TOKEN"]);
        Assert.True(fs.Enabled);
        Assert.True(fs.AutoApprove);

        var remote = parsed.Single(s => s.Id == "remote");
        Assert.Equal(McpTransport.Http, remote.Transport);
        Assert.Equal("https://example.com/mcp", remote.Url);
        Assert.Equal("Bearer x", remote.Headers["Authorization"]);
        Assert.False(remote.Enabled);
        Assert.False(remote.AutoApprove);
    }

    [Fact]
    public void Parse_MultipleServers_AllReturned()
    {
        const string json = """
        { "mcpServers": {
            "a": { "command": "x" },
            "b": { "url": "https://b" } } }
        """;
        var servers = McpConfigStore.Parse(json);
        Assert.Equal(2, servers.Count);
        Assert.Contains(servers, s => s.Id == "a" && s.Transport == McpTransport.Stdio);
        Assert.Contains(servers, s => s.Id == "b" && s.Transport == McpTransport.Http);
    }
}
