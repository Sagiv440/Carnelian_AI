using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AI_Interface.Services;

/// <summary>
/// A live connection to one MCP server, backed by the official <c>ModelContextProtocol</c> SDK. Lazily
/// connects (for stdio, the SDK launches the configured command as a child process), lists the server's tools
/// as namespaced <see cref="AgentTool"/>s, routes a call to the underlying client, and flattens the result's
/// content blocks to the plain-text string the agent loop expects. Disposing disposes the SDK client, which
/// tears down the transport and kills the child process.
/// </summary>
internal sealed class McpConnection : IAsyncDisposable
{
    private readonly McpServerConfig _config;
    private McpClient? _client;

    /// <summary>Namespaced tool name (<c>mcp__id__tool</c>) → the server's real tool name. Truncation-proof routing.</summary>
    private readonly Dictionary<string, string> _toolMap = new(StringComparer.Ordinal);

    public McpConnection(McpServerConfig config) => _config = config;

    public string Id => _config.Id;
    public bool AutoApprove => _config.AutoApprove;

    /// <summary>A value summarizing the launch config, so the manager can detect when a server's settings changed.</summary>
    public string Signature => ComputeSignature(_config);

    /// <summary>Connects (if needed) and returns the server's tools as namespaced <see cref="AgentTool"/>s.</summary>
    public async Task<IReadOnlyList<AgentTool>> ListToolsAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        var tools = await _client!.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);

        _toolMap.Clear();
        var result = new List<AgentTool>(tools.Count);
        foreach (var t in tools)
        {
            var namespaced = McpToolName.Make(_config.Id, t.Name);
            _toolMap[namespaced] = t.Name;
            var desc = string.IsNullOrWhiteSpace(t.Description) ? t.Name : t.Description!;
            // Prefix the display name with the server so the model has context for which service a tool reaches.
            result.Add(new AgentTool(namespaced, $"[{_config.Name}] {desc}", t.JsonSchema));
        }
        return result;
    }

    /// <summary>Calls a namespaced tool and returns its text result (content blocks flattened; errors prefixed "Error:").</summary>
    public async Task<string> CallAsync(string namespacedTool, JsonElement args, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        if (!_toolMap.TryGetValue(namespacedTool, out var realTool))
            return $"Error: tool '{namespacedTool}' is not available on MCP server '{_config.Name}'.";

        var arguments = ToArguments(args);
        CallToolResult res;
        try
        {
            res = await _client!.CallToolAsync(realTool, arguments, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error: MCP tool '{realTool}' failed — {ex.Message}";
        }

        var text = FlattenContent(res);
        return res.IsError == true ? $"Error: {text}" : text;
    }

    /// <summary>Lists the server's tools as (real name, description) summaries — for the Settings "Test" tool list.</summary>
    public async Task<IReadOnlyList<McpToolSummary>> ListToolSummariesAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        var tools = await _client!.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        return tools.Select(t => new McpToolSummary(t.Name, (t.Description ?? "").Trim())).ToList();
    }

    // ---- resources (Phase 3) ---------------------------------------------------------------

    /// <summary>Lists the server's resources (empty if the server doesn't support resources).</summary>
    public async Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        IList<McpClientResource> resources;
        try { resources = await _client!.ListResourcesAsync(cancellationToken: ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return Array.Empty<McpResourceInfo>(); }

        return resources.Select(r => new McpResourceInfo(
            _config.Id, DisplayName, r.Uri ?? "", r.Name ?? r.Uri ?? "", r.Description ?? "", r.MimeType ?? ""))
            .ToList();
    }

    /// <summary>Reads a resource's contents and flattens them to text (binary contents noted).</summary>
    public async Task<string> ReadResourceAsync(string uri, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        ReadResourceResult res;
        try { res = await _client!.ReadResourceAsync(uri, cancellationToken: ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return $"Error: couldn't read resource '{uri}' — {ex.Message}"; }

        if (res.Contents is null || res.Contents.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var c in res.Contents)
            sb.AppendLine(c is TextResourceContents t ? t.Text : $"[binary resource: {c.Uri}]");
        return sb.ToString().Trim();
    }

    // ---- prompts (Phase 3) -----------------------------------------------------------------

    /// <summary>Lists the server's prompt templates (empty if the server doesn't support prompts).</summary>
    public async Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        IList<McpClientPrompt> prompts;
        try { prompts = await _client!.ListPromptsAsync(cancellationToken: ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return Array.Empty<McpPromptInfo>(); }

        return prompts.Select(p => new McpPromptInfo(_config.Id, DisplayName, p.Name ?? "", p.Description ?? ""))
            .ToList();
    }

    /// <summary>Expands a prompt template (no arguments) to its text — the prompt messages flattened.</summary>
    public async Task<string> GetPromptTextAsync(string promptName, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        GetPromptResult res;
        try { res = await _client!.GetPromptAsync(promptName, EmptyArgs, cancellationToken: ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return $"Error: couldn't expand prompt '{promptName}' — {ex.Message}"; }

        var sb = new StringBuilder();
        if (res.Messages is not null)
            foreach (var m in res.Messages)
                sb.AppendLine(FlattenBlocks(new[] { m.Content }));
        return sb.ToString().Trim();
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyArgs = new Dictionary<string, object?>();

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is not null)
            return;

        IClientTransport transport = _config.Transport == McpTransport.Http
            ? BuildHttpTransport()
            : BuildStdioTransport();
        _client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>stdio: the app launches the configured command as a child process.</summary>
    private IClientTransport BuildStdioTransport()
    {
        var options = new StdioClientTransportOptions
        {
            Name = DisplayName,
            Command = _config.Command,
            Arguments = _config.Args?.ToList() ?? new List<string>(),
            EnvironmentVariables = _config.Env is { Count: > 0 }
                ? _config.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)
                : null
        };
        return new StdioClientTransport(options);
    }

    /// <summary>HTTP: connect to a remote server (Streamable HTTP / SSE auto-detected) with optional headers.</summary>
    private IClientTransport BuildHttpTransport()
    {
        if (string.IsNullOrWhiteSpace(_config.Url) || !Uri.TryCreate(_config.Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"MCP server '{DisplayName}' has an invalid or missing URL.");

        var options = new HttpClientTransportOptions
        {
            Name = DisplayName,
            Endpoint = uri,
            AdditionalHeaders = _config.Headers is { Count: > 0 } ? _config.Headers : null
            // TransportMode left at its default (AutoDetect): tries Streamable HTTP, falls back to SSE.
        };
        return new HttpClientTransport(options);
    }

    private string DisplayName => string.IsNullOrWhiteSpace(_config.Name) ? _config.Id : _config.Name;

    /// <summary>Top-level JSON-object args → a CLR dictionary the SDK serializes back to JSON for the call.</summary>
    private static Dictionary<string, object?> ToArguments(JsonElement args)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (args.ValueKind == JsonValueKind.Object)
            foreach (var p in args.EnumerateObject())
                dict[p.Name] = p.Value.Clone(); // boxed JsonElement (a struct → never null); STJ writes its JSON
        return dict;
    }

    private static string FlattenContent(CallToolResult res)
    {
        if (res.Content is null || res.Content.Count == 0)
            return res.IsError == true ? "the MCP tool reported an error with no message." : "(no content)";
        return FlattenBlocks(res.Content);
    }

    /// <summary>
    /// Flattens MCP content blocks (from a tool result or a prompt message) to plain text: text blocks and
    /// embedded text resources are kept, resource links are noted, image/audio are placeholdered.
    /// </summary>
    private static string FlattenBlocks(IEnumerable<ContentBlock> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextContentBlock text:
                    sb.AppendLine(text.Text);
                    break;
                case EmbeddedResourceBlock { Resource: TextResourceContents trc }:
                    sb.AppendLine(trc.Text); // an embedded text resource — include its text
                    break;
                case EmbeddedResourceBlock emb:
                    sb.AppendLine($"[embedded resource: {emb.Resource?.Uri}]"); // binary/blob resource
                    break;
                case ResourceLinkBlock link:
                    sb.AppendLine($"[resource: {link.Name} — {link.Uri}]");
                    break;
                case ImageContentBlock:
                    sb.AppendLine("[image]"); // not surfaced as text (vision passthrough is future work)
                    break;
                case AudioContentBlock:
                    sb.AppendLine("[audio]");
                    break;
                default:
                    sb.AppendLine($"[{block.Type} content]");
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>A stable signature of the launch config; the manager reconnects when it changes.</summary>
    internal static string ComputeSignature(McpServerConfig c)
    {
        var sb = new StringBuilder();
        sb.Append((int)c.Transport).Append('|').Append(c.Command).Append('|')
          .Append(string.Join(" ", c.Args ?? new List<string>())).Append('|').Append(c.Url).Append('|');
        if (c.Env is not null)
            foreach (var kv in c.Env.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch { /* best effort — disposing also kills the child process */ }
        }
    }
}
