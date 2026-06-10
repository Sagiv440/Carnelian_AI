using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Manages the configured MCP (Model Context Protocol) servers: connects to each enabled server, aggregates
/// their tools (namespaced <c>mcp__&lt;id&gt;__&lt;tool&gt;</c>) for the project agent, and routes the model's
/// tool calls to the owning server. Connections are cached across turns and reconciled to the current settings.
/// </summary>
public interface IMcpService
{
    /// <summary>
    /// All tools across enabled servers, ready to advertise to the model. Best-effort: a server that fails to
    /// connect contributes nothing (its error surfaces via <see cref="TestAsync"/> in Settings). Returns empty
    /// when no servers are configured.
    /// </summary>
    Task<IReadOnlyList<AgentTool>> ListToolsAsync(string? projectDir, CancellationToken ct);

    /// <summary>Routes a namespaced tool call to its server and returns the text result (errors prefixed "Error:").</summary>
    Task<string> CallToolAsync(string toolName, JsonElement args, CancellationToken ct);

    /// <summary>True when the named tool's server is marked trusted (its calls skip the approval prompt).</summary>
    bool IsAutoApproved(string toolName);

    /// <summary>All resources across enabled servers (best-effort; a server without resource support contributes none).</summary>
    Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(string? projectDir, CancellationToken ct);

    /// <summary>Reads one resource's contents as text (routed to its owning server).</summary>
    Task<string> ReadResourceAsync(string serverId, string uri, CancellationToken ct);

    /// <summary>All prompt templates across enabled servers (best-effort; surfaced as composer slash-commands).</summary>
    Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(string? projectDir, CancellationToken ct);

    /// <summary>Expands a prompt template (no arguments) to its text, routed to its owning server.</summary>
    Task<string> GetPromptTextAsync(string serverId, string promptName, CancellationToken ct);

    /// <summary>Connects to a (possibly unsaved) server config and reports reachability + tool count for the Settings "Test" button.</summary>
    Task<McpProbe> TestAsync(McpServerConfig server, CancellationToken ct);

    /// <summary>Disposes every cached connection (kills stdio child processes). Call on app exit / config change.</summary>
    Task DisconnectAllAsync();
}

/// <summary>
/// Outcome of a connection test: whether it connected, a short message, and the tools the server exposes
/// (name + description) so the Settings panel can list them. <see cref="ToolCount"/> = <c>Tools.Count</c>.
/// </summary>
public sealed record McpProbe(bool Ok, int ToolCount, string Message, IReadOnlyList<McpToolSummary> Tools);
