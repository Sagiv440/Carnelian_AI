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
/// Shared <see cref="IChatClient"/> implementation for any provider that speaks the <b>OpenAI Chat
/// Completions</b> wire protocol (<c>POST v1/chat/completions</c>, <c>GET v1/models</c>, SSE streaming,
/// the same tool-call shape). OpenAI itself, plus OpenAI-compatible providers like DeepSeek and Nvidia NIM,
/// differ only in their base URL (the injected <see cref="HttpClient.BaseAddress"/>), their API key
/// (<see cref="ApiKey"/>), the model-list filter (<see cref="KeepModelId"/>), and the error label
/// (<see cref="ProviderLabel"/>). Subclasses supply those; everything else lives here.
/// The API key is read from settings on every call, so a key change takes effect without a restart; a
/// blank key reports "not configured" (empty model list, unreachable) instead of erroring.
/// </summary>
public abstract class OpenAiCompatibleClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    /// <summary>The settings store (subclasses read their provider's API key from it).</summary>
    protected ISettingsService Settings { get; }

    protected OpenAiCompatibleClient(HttpClient http, ISettingsService settings)
    {
        _http = http;
        Settings = settings;
    }

    public abstract AiProvider Provider { get; }

    /// <summary>The provider's API key (trimmed), read from settings on every call.</summary>
    protected abstract string ApiKey { get; }

    /// <summary>Human label for error messages (e.g. "OpenAI", "DeepSeek").</summary>
    protected virtual string ProviderLabel => Provider.DisplayName();

    /// <summary>Filter applied to the <c>v1/models</c> listing — keep everything by default.</summary>
    protected virtual bool KeepModelId(string id) => true;

    private HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, "v1/" + relativeUrl.TrimStart('/'));
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey);
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
            return Array.Empty<string>(); // best-effort listing — a bad key contributes nothing

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync<OpenAiModelList>(stream, JsonOptions, ct)
            .ConfigureAwait(false);

        return list?.Data?
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id) && KeepModelId(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, bool think,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // `think` is ignored: the standard chat models expose no separate chain-of-thought stream.
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = ToWireMessages(messages),
            ["stream"] = true
        };

        using var req = NewRequest(HttpMethod.Post, "chat/completions");
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

        // Server-sent events: "data: {json}" lines, terminated by "data: [DONE]".
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = line["data:".Length..].Trim();
            if (payload == "[DONE]")
                yield break;

            var delta = TryExtractStreamDelta(payload);
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    private static string? TryExtractStreamDelta(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;
            var first = choices[0];
            if (!first.TryGetProperty("delta", out var delta) ||
                !delta.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String)
                return null;
            return content.GetString();
        }
        catch (JsonException)
        {
            return null; // skip malformed chunk rather than aborting the stream
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
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = ToWireMessages(messages),
            ["stream"] = false,
            ["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
            }).ToList()
        };

        using var req = NewRequest(HttpMethod.Post, "chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, raw));

        using var doc = JsonDocument.Parse(raw);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? ""
            : "";

        var calls = new List<AgentToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                if (!tc.TryGetProperty("function", out var fn))
                    continue;
                var name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                // OpenAI sends arguments as a JSON *string*; parse it into a JsonElement object.
                var args = ParseArgumentsString(fn.TryGetProperty("arguments", out var a) ? a.GetString() : null);
                calls.Add(new AgentToolCall(name, args));
            }
        }

        return new AgentTurn(content, calls);
    }

    private static JsonElement ParseArgumentsString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.SerializeToElement(new { });
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }

    /// <summary>
    /// Translates the running conversation into OpenAI's message array, synthesising the
    /// <c>tool_call_id</c>s the API requires. The app's <see cref="ChatMessage"/> only carries tool
    /// *names* (assistant <see cref="ChatMessage.ToolCalls"/>) and a <see cref="ChatMessage.ToolName"/>
    /// on each result, so we generate deterministic ids ("call_{n}") per assistant tool call and match
    /// each subsequent tool-result message to the next unconsumed call of the same name (the agent runs
    /// calls in order, so this preserves pairing).
    /// </summary>
    private static List<object> ToWireMessages(IEnumerable<ChatMessage> messages)
    {
        var wire = new List<object>();
        var pending = new Queue<(string Name, string Id)>();
        var counter = 0;

        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.System:
                    wire.Add(new { role = "system", content = m.Content });
                    break;

                case ChatRole.User:
                    wire.Add(new { role = "user", content = m.Content });
                    break;

                case ChatRole.Assistant when m.ToolCalls is { Count: > 0 }:
                    var toolCalls = new List<object>();
                    foreach (var call in m.ToolCalls)
                    {
                        var id = "call_" + (++counter);
                        pending.Enqueue((call.Name, id));
                        toolCalls.Add(new
                        {
                            id,
                            type = "function",
                            function = new
                            {
                                name = call.Name,
                                arguments = call.Arguments.ValueKind == JsonValueKind.Undefined
                                    ? "{}"
                                    : call.Arguments.GetRawText()
                            }
                        });
                    }
                    // OpenAI requires content present (may be empty) alongside tool_calls.
                    wire.Add(new { role = "assistant", content = m.Content ?? "", tool_calls = toolCalls });
                    break;

                case ChatRole.Assistant:
                    wire.Add(new { role = "assistant", content = m.Content });
                    break;

                case ChatRole.Tool:
                    // Match this result to the next pending call (preferring one with the same name).
                    var id2 = DequeueMatchingId(pending, m.ToolName);
                    wire.Add(new { role = "tool", tool_call_id = id2, content = m.Content });
                    break;
            }
        }

        return wire;
    }

    private static string DequeueMatchingId(Queue<(string Name, string Id)> pending, string? toolName)
    {
        if (pending.Count == 0)
            return "call_unknown";

        // Fast path: the head matches (the common case — calls executed in order).
        if (toolName is null || pending.Peek().Name == toolName)
            return pending.Dequeue().Id;

        // Otherwise scan for the first pending call with the matching name; fall back to the head.
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
        return found ?? "call_unknown";
    }

    private string BuildErrorMessage(int status, string body)
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
            // Not the expected JSON shape; fall back to the raw text below.
        }

        if (string.IsNullOrWhiteSpace(detail))
            detail = string.IsNullOrWhiteSpace(body) ? "(no details)" : body.Trim();

        return $"{ProviderLabel} returned HTTP {status}: {detail}";
    }
}

// --- wire DTOs (shared by every OpenAI-compatible client) --------------------------------------

internal sealed class OpenAiModelList
{
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public List<OpenAiModelEntry>? Data { get; set; }
}

internal sealed class OpenAiModelEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
}
