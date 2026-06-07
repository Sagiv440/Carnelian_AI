using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// <see cref="IChatClient"/> over the Anthropic Messages API (api.anthropic.com/v1). The API key
/// (sent as the <c>x-api-key</c> header alongside <c>anthropic-version</c>) is read from settings on
/// every call; a blank key reports "not configured".
/// </summary>
public sealed class AnthropicClient : IAnthropicClient
{
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public AnthropicClient(HttpClient http, ISettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    public AiProvider Provider => AiProvider.Anthropic;

    private string ApiKey => _settings.Current.AnthropicApiKey.Trim();

    private HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, "v1/" + relativeUrl.TrimStart('/'));
        req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        return req;
    }

    public async Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey))
            return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var req = NewRequest(HttpMethod.Get, "models");
            using var resp = await _http.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey))
            return Array.Empty<string>();

        using var req = NewRequest(HttpMethod.Get, "models");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return Array.Empty<string>();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync<AnthropicModelList>(stream, JsonOptions, ct)
            .ConfigureAwait(false);

        return list?.Data?
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, bool think,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // `think` is ignored here to stay low-risk: extended thinking needs per-model support and a
        // budget, and a wrong combination errors. Standard streaming is unaffected.
        var (wireMessages, system) = ToWireMessages(messages);
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = system,
            ["messages"] = wireMessages,
            ["stream"] = true
        };

        using var req = NewRequest(HttpMethod.Post, "messages");
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, err));
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // SSE: alternating "event: <type>" / "data: {json}" lines. We only need the data lines, whose
        // JSON has a "type" we switch on (content_block_delta → text_delta).
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = line["data:".Length..].Trim();
            var delta = TryExtractTextDelta(payload);
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    private static string? TryExtractTextDelta(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "content_block_delta")
                return null;
            if (!root.TryGetProperty("delta", out var delta))
                return null;
            if (delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta" &&
                delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string> CompleteAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var delta in ChatStreamAsync(model, messages, think: false, ct).ConfigureAwait(false))
            sb.Append(delta);
        return sb.ToString();
    }

    public async Task<AgentTurn> ChatWithToolsAsync(
        string model, IEnumerable<ChatMessage> messages,
        IReadOnlyList<AgentTool> tools, CancellationToken ct = default)
    {
        var (wireMessages, system) = ToWireMessages(messages);
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = system,
            ["messages"] = wireMessages,
            ["tools"] = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = t.Parameters
            }).ToList()
        };

        using var req = NewRequest(HttpMethod.Post, "messages");
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, raw));

        var text = new StringBuilder();
        var calls = new List<AgentToolCall>();

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var blockType = block.TryGetProperty("type", out var bt) ? bt.GetString() : null;
                if (blockType == "text" && block.TryGetProperty("text", out var t) &&
                    t.ValueKind == JsonValueKind.String)
                {
                    text.Append(t.GetString());
                }
                else if (blockType == "tool_use")
                {
                    var name = block.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    // Anthropic sends tool input as a JSON object already.
                    var input = block.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object
                        ? inp.Clone()
                        : JsonSerializer.SerializeToElement(new { });
                    calls.Add(new AgentToolCall(name, input));
                }
            }
        }

        return new AgentTurn(text.ToString(), calls);
    }

    /// <summary>
    /// Translates the conversation into Anthropic's <c>messages</c> array (plus the extracted top-level
    /// <c>system</c> text). System messages are NOT included in the array. Assistant tool calls become
    /// <c>tool_use</c> content blocks with synthesised ids ("toolu_{n}"); each subsequent tool-result
    /// message becomes a <c>user</c> message with a <c>tool_result</c> block carrying the matching
    /// <c>tool_use_id</c> (paired by tool name, since the agent runs calls in order).
    /// </summary>
    private static (List<object> Messages, string? System) ToWireMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<object>();
        var system = new StringBuilder();
        var pending = new Queue<(string Name, string Id)>();
        var counter = 0;

        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.System:
                    if (system.Length > 0) system.Append("\n\n");
                    system.Append(m.Content);
                    break;

                case ChatRole.User:
                    result.Add(new { role = "user", content = m.Content });
                    break;

                case ChatRole.Assistant when m.ToolCalls is { Count: > 0 }:
                    var blocks = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                        blocks.Add(new { type = "text", text = m.Content });
                    foreach (var call in m.ToolCalls)
                    {
                        var id = "toolu_" + (++counter);
                        pending.Enqueue((call.Name, id));
                        blocks.Add(new
                        {
                            type = "tool_use",
                            id,
                            name = call.Name,
                            input = (object)(call.Arguments.ValueKind == JsonValueKind.Undefined
                                ? new { }
                                : (object)call.Arguments)
                        });
                    }
                    result.Add(new { role = "assistant", content = blocks });
                    break;

                case ChatRole.Assistant:
                    result.Add(new { role = "assistant", content = m.Content });
                    break;

                case ChatRole.Tool:
                    var matchedId = DequeueMatchingId(pending, m.ToolName);
                    result.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "tool_result", tool_use_id = matchedId, content = m.Content }
                        }
                    });
                    break;
            }
        }

        return (result, system.Length == 0 ? null : system.ToString());
    }

    private static string DequeueMatchingId(Queue<(string Name, string Id)> pending, string? toolName)
    {
        if (pending.Count == 0)
            return "toolu_unknown";

        if (toolName is null || pending.Peek().Name == toolName)
            return pending.Dequeue().Id;

        var buffer = new List<(string Name, string Id)>();
        string? found = null;
        while (pending.Count > 0)
        {
            var item = pending.Dequeue();
            if (found is null && item.Name == toolName)
                found = item.Id;
            else
                buffer.Add(item);
        }
        foreach (var item in buffer)
            pending.Enqueue(item);
        return found ?? "toolu_unknown";
    }

    private static string BuildErrorMessage(int status, string body)
    {
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                detail = msg.GetString();
        }
        catch (JsonException)
        {
            // not the expected shape
        }

        if (string.IsNullOrWhiteSpace(detail))
            detail = string.IsNullOrWhiteSpace(body) ? "(no details)" : body.Trim();

        return $"Anthropic returned HTTP {status}: {detail}";
    }
}

// --- wire DTOs ---------------------------------------------------------------------------------

internal sealed class AnthropicModelList
{
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public List<AnthropicModelEntry>? Data { get; set; }
}

internal sealed class AnthropicModelEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
}
