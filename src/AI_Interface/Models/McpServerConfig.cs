using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AI_Interface.Models;

/// <summary>How the app talks to an MCP server.</summary>
public enum McpTransport
{
    /// <summary>Launch the server as a child process and speak JSON-RPC over stdin/stdout (local servers).</summary>
    Stdio,

    /// <summary>Connect to an already-running server over Streamable HTTP / SSE (remote servers). Phase 2.</summary>
    Http
}

/// <summary>
/// One configured MCP (Model Context Protocol) server. For <see cref="McpTransport.Stdio"/> the app launches
/// <see cref="Command"/> + <see cref="Args"/> (with <see cref="Env"/>) as a child process and speaks JSON-RPC
/// over its stdio; for <see cref="McpTransport.Http"/> it connects to <see cref="Url"/>. The server's discovered
/// tools are advertised to the project agent, namespaced as <c>mcp__&lt;id&gt;__&lt;tool&gt;</c> (see
/// <c>McpToolName</c>) so they never collide with the built-in tools or across servers.
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>Stable slug used in the tool namespace (<c>mcp__&lt;id&gt;__tool</c>). Derived from the name; unique per list.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name shown in Settings.</summary>
    public string Name { get; set; } = "";

    /// <summary>When false the server is configured but not connected and contributes no tools.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which transport the app uses to reach this server.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    // --- stdio transport ---

    /// <summary>Executable / launcher to run, e.g. "npx", "uvx", "docker" (stdio transport).</summary>
    public string Command { get; set; } = "";

    /// <summary>Arguments passed to <see cref="Command"/>, in order.</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Extra environment variables for the child process (may carry secrets/tokens).</summary>
    public Dictionary<string, string> Env { get; set; } = new();

    // --- http transport (Phase 2) ---

    /// <summary>Base URL of a remote MCP server (HTTP / SSE transport).</summary>
    public string Url { get; set; } = "";

    /// <summary>HTTP headers sent to the remote server (e.g. an Authorization bearer token).</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    // --- trust ---

    /// <summary>
    /// When true, this server's tool calls skip the per-call approval prompt (the user has marked it trusted).
    /// Default false — MCP tools reach external services, so they are approval-gated like destructive tools.
    /// </summary>
    public bool AutoApprove { get; set; }
}
