using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IMcpService"/>. Reads the configured servers from settings, keeps one cached
/// <see cref="McpConnection"/> per enabled server (reconciled to the current config on each list), aggregates
/// their tools for the agent loop, and routes calls back to the owning server. All I/O is off the UI thread;
/// the VM marshals any UI updates.
/// </summary>
public sealed class McpService : IMcpService, IAsyncDisposable
{
    /// <summary>Fail-fast cap for connecting/handshaking a single server (a hung launcher can't block the loop).</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>server id → live connection.</summary>
    private readonly Dictionary<string, McpConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>namespaced tool name → owning server id (rebuilt each list; used for routing).</summary>
    private readonly Dictionary<string, string> _toolOwner = new(StringComparer.Ordinal);

    /// <summary>namespaced tool names whose server is trusted (auto-approve). Rebuilt each list.</summary>
    private readonly HashSet<string> _autoApproved = new(StringComparer.Ordinal);

    public McpService(ISettingsService settings) => _settings = settings;

    public async Task<IReadOnlyList<AgentTool>> ListToolsAsync(string? projectDir, CancellationToken ct)
    {
        var configs = EnabledServers(projectDir);
        if (configs.Count == 0)
        {
            await DisconnectAllAsync().ConfigureAwait(false); // settings emptied → tear down any stale connections
            return Array.Empty<AgentTool>();
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SyncConnections(configs);

            var all = new List<AgentTool>();
            _toolOwner.Clear();
            _autoApproved.Clear();

            foreach (var cfg in configs)
            {
                if (!_connections.TryGetValue(cfg.Id, out var conn))
                    continue;

                IReadOnlyList<AgentTool> tools;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(ConnectTimeout);
                    tools = await conn.ListToolsAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // user-cancelled the whole turn
                }
                catch
                {
                    // Best-effort: drop a server that failed to connect/list; it contributes no tools this turn.
                    await DropAsync(cfg.Id).ConfigureAwait(false);
                    continue;
                }

                foreach (var tool in tools)
                {
                    _toolOwner[tool.Name] = cfg.Id;
                    if (cfg.AutoApprove)
                        _autoApproved.Add(tool.Name);
                    all.Add(tool);
                }
            }

            return all;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> CallToolAsync(string toolName, JsonElement args, CancellationToken ct)
    {
        McpConnection? conn = null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_toolOwner.TryGetValue(toolName, out var id))
                _connections.TryGetValue(id, out conn);
        }
        finally
        {
            _gate.Release();
        }

        if (conn is null)
            return $"Error: no connected MCP server provides the tool '{toolName}'. " +
                   "It may have failed to start — check Settings → MCP Servers (Test).";

        return await conn.CallAsync(toolName, args, ct).ConfigureAwait(false);
    }

    public bool IsAutoApproved(string toolName) => _autoApproved.Contains(toolName);

    public async Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(string? projectDir, CancellationToken ct)
    {
        var all = new List<McpResourceInfo>();
        foreach (var conn in await ConnectAllAsync(projectDir, ct).ConfigureAwait(false))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(ConnectTimeout);
                all.AddRange(await conn.ListResourcesAsync(cts.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* server failed to connect or doesn't support resources */ }
        }
        return all;
    }

    public async Task<string> ReadResourceAsync(string serverId, string uri, CancellationToken ct)
    {
        var conn = await GetConnectionAsync(serverId).ConfigureAwait(false);
        return conn is null
            ? $"Error: MCP server '{serverId}' is not connected."
            : await conn.ReadResourceAsync(uri, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(string? projectDir, CancellationToken ct)
    {
        var all = new List<McpPromptInfo>();
        foreach (var conn in await ConnectAllAsync(projectDir, ct).ConfigureAwait(false))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(ConnectTimeout);
                all.AddRange(await conn.ListPromptsAsync(cts.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* server failed to connect or doesn't support prompts */ }
        }
        return all;
    }

    public async Task<string> GetPromptTextAsync(string serverId, string promptName, CancellationToken ct)
    {
        var conn = await GetConnectionAsync(serverId).ConfigureAwait(false);
        return conn is null
            ? $"Error: MCP server '{serverId}' is not connected."
            : await conn.GetPromptTextAsync(promptName, ct).ConfigureAwait(false);
    }

    /// <summary>Ensures connections exist for the enabled set (reconciled to config) and returns them.</summary>
    private async Task<List<McpConnection>> ConnectAllAsync(string? projectDir, CancellationToken ct)
    {
        var configs = EnabledServers(projectDir);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SyncConnections(configs);
            return configs.Select(c => _connections.GetValueOrDefault(c.Id))
                .Where(c => c is not null).Select(c => c!).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpConnection?> GetConnectionAsync(string serverId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return _connections.GetValueOrDefault(serverId); }
        finally { _gate.Release(); }
    }

    public async Task<McpProbe> TestAsync(McpServerConfig server, CancellationToken ct)
    {
        var none = (IReadOnlyList<McpToolSummary>)Array.Empty<McpToolSummary>();
        if (server.Transport == McpTransport.Stdio && string.IsNullOrWhiteSpace(server.Command))
            return new McpProbe(false, 0, "No command specified.", none);

        var conn = new McpConnection(server);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ConnectTimeout);
            var tools = await conn.ListToolSummariesAsync(cts.Token).ConfigureAwait(false);
            var msg = tools.Count == 1 ? "Connected — 1 tool." : $"Connected — {tools.Count} tools.";
            return new McpProbe(true, tools.Count, msg, tools);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new McpProbe(false, 0, "Cancelled.", none);
        }
        catch (OperationCanceledException)
        {
            return new McpProbe(false, 0, $"Timed out after {ConnectTimeout.TotalSeconds:0}s.", none);
        }
        catch (Exception ex)
        {
            return new McpProbe(false, 0, ShortError(ex), none);
        }
        finally
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task DisconnectAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var conn in _connections.Values)
                await conn.DisposeAsync().ConfigureAwait(false);
            _connections.Clear();
            _toolOwner.Clear();
            _autoApproved.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- internals -------------------------------------------------------------------------

    /// <summary>
    /// The effective enabled+runnable server set for this turn: the global servers (Settings) merged with the
    /// active project's <c>.AI/mcp.json</c>, where a project server <b>overrides</b> a global one with the same id.
    /// </summary>
    private List<McpServerConfig> EnabledServers(string? projectDir)
    {
        var byId = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _settings.Current.McpServers ?? new List<McpServerConfig>())
            if (!string.IsNullOrWhiteSpace(s.Id))
                byId[s.Id] = s;
        foreach (var s in McpConfigStore.Load(projectDir)) // project overrides global by id
            if (!string.IsNullOrWhiteSpace(s.Id))
                byId[s.Id] = s;

        return byId.Values.Where(s => s.Enabled && IsRunnable(s)).ToList();
    }

    private static bool IsRunnable(McpServerConfig s) => s.Transport == McpTransport.Stdio
        ? !string.IsNullOrWhiteSpace(s.Command)
        : !string.IsNullOrWhiteSpace(s.Url);

    /// <summary>
    /// Reconciles the cached connections to the current config set: drops connections that were removed or whose
    /// launch config changed (so an edit reconnects), and creates (but doesn't yet connect) any new ones.
    /// Caller holds <see cref="_gate"/>.
    /// </summary>
    private void SyncConnections(List<McpServerConfig> configs)
    {
        var wanted = configs.ToDictionary(c => c.Id, McpConnection.ComputeSignature, StringComparer.OrdinalIgnoreCase);

        foreach (var id in _connections.Keys.ToList())
            if (!wanted.TryGetValue(id, out var sig) || _connections[id].Signature != sig)
                DropSync(id);

        foreach (var cfg in configs)
            if (!_connections.ContainsKey(cfg.Id))
                _connections[cfg.Id] = new McpConnection(cfg);
    }

    private void DropSync(string id)
    {
        if (_connections.Remove(id, out var conn))
            _ = conn.DisposeAsync(); // fire-and-forget: disposing kills the child process
    }

    private async Task DropAsync(string id)
    {
        if (_connections.Remove(id, out var conn))
            await conn.DisposeAsync().ConfigureAwait(false);
    }

    private static string ShortError(Exception ex)
    {
        var msg = ex is System.ComponentModel.Win32Exception
            ? $"Couldn't launch the command — is it installed and on your PATH? ({ex.Message})"
            : ex.Message;
        return msg.Length > 300 ? msg[..300] + "…" : msg;
    }

    public async ValueTask DisposeAsync() => await DisconnectAllAsync().ConfigureAwait(false);
}
