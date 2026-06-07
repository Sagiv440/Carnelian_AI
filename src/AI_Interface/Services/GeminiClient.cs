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
/// <see cref="IChatClient"/> over the Google Gemini API (generativelanguage.googleapis.com/v1beta).
/// The API key (passed as the <c>?key=</c> query parameter) is read from settings on every call;
/// a blank key reports "not configured".
/// </summary>
public sealed class GeminiClient : IGeminiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public GeminiClient(HttpClient http, ISettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    public AiProvider Provider => AiProvider.Gemini;

    private string ApiKey => _settings.Current.GeminiApiKey.Trim();

    public async Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey))
            return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var resp = await _http.GetAsync($"v1beta/models?key={Uri.EscapeDataString(ApiKey)}", timeoutCts.Token)
                .ConfigureAwait(false);
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

        using var resp = await _http.GetAsync($"v1beta/models?key={Uri.EscapeDataString(ApiKey)}", ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return Array.Empty<string>();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync<GeminiModelList>(stream, JsonOptions, ct)
            .ConfigureAwait(false);

        return list?.Models?
            .Where(m => m.SupportedGenerationMethods is null ||
                        m.SupportedGenerationMethods.Contains("generateContent"))
            .Select(m => StripPrefix(m.Name))
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    /// <summary>Model names come back as "models/gemini-…"; the API path wants the bare id.</summary>
    private static string StripPrefix(string name) =>
        name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IEnumerable<ChatMessage> messages, bool think,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // `think` is ignored: Gemini 2.5 reasoning is automatic and not separately surfaced here.
        var (contents, system) = ToContents(messages);
        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["systemInstruction"] = system is null ? null : new { parts = new[] { new { text = system } } }
        };

        var url = $"v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(ApiKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

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

        // SSE: "data: {json}" lines, each a partial GenerateContentResponse.
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = line["data:".Length..].Trim();
            foreach (var text in ExtractTextParts(payload))
                if (!string.IsNullOrEmpty(text))
                    yield return text;
        }
    }

    private static IEnumerable<string> ExtractTextParts(string payload)
    {
        var results = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                        if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                            results.Add(t.GetString() ?? "");
                }
            }
        }
        catch (JsonException)
        {
            // skip malformed chunk
        }
        return results;
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
        var (contents, system) = ToContents(messages);
        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["systemInstruction"] = system is null ? null : new { parts = new[] { new { text = system } } },
            ["tools"] = new[]
            {
                new
                {
                    functionDeclarations = tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    }).ToList()
                }
            }
        };

        var url = $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(ApiKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, raw));

        var text = new StringBuilder();
        var calls = new List<AgentToolCall>();

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        text.Append(t.GetString());
                    else if (part.TryGetProperty("functionCall", out var fc))
                    {
                        var name = fc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        // Gemini sends args as a JSON object already (not a string).
                        var args = fc.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
                            ? a.Clone()
                            : JsonSerializer.SerializeToElement(new { });
                        calls.Add(new AgentToolCall(name, args));
                    }
                }
            }
        }

        return new AgentTurn(text.ToString(), calls);
    }

    /// <summary>
    /// Translates the conversation into Gemini's <c>contents</c> array (plus the extracted system text).
    /// Gemini has no system role — the system prompt becomes <c>systemInstruction</c>. Roles are
    /// <c>user</c> / <c>model</c>. Assistant tool calls become <c>functionCall</c> parts and tool
    /// results become <c>functionResponse</c> parts (matched to a call by tool name).
    /// </summary>
    private static (List<object> Contents, string? System) ToContents(IEnumerable<ChatMessage> messages)
    {
        var contents = new List<object>();
        var system = new StringBuilder();

        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.System:
                    if (system.Length > 0) system.Append("\n\n");
                    system.Append(m.Content);
                    break;

                case ChatRole.User:
                    contents.Add(new { role = "user", parts = new object[] { new { text = m.Content } } });
                    break;

                case ChatRole.Assistant when m.ToolCalls is { Count: > 0 }:
                    var parts = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                        parts.Add(new { text = m.Content });
                    foreach (var call in m.ToolCalls)
                        parts.Add(new
                        {
                            functionCall = new
                            {
                                name = call.Name,
                                args = (object?)(call.Arguments.ValueKind == JsonValueKind.Undefined
                                    ? new { }
                                    : (object)call.Arguments)
                            }
                        });
                    contents.Add(new { role = "model", parts });
                    break;

                case ChatRole.Assistant:
                    contents.Add(new { role = "model", parts = new object[] { new { text = m.Content } } });
                    break;

                case ChatRole.Tool:
                    // A function result is sent back as a user turn carrying a functionResponse part.
                    contents.Add(new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new
                            {
                                functionResponse = new
                                {
                                    name = m.ToolName ?? "tool",
                                    response = new { result = m.Content }
                                }
                            }
                        }
                    });
                    break;
            }
        }

        return (contents, system.Length == 0 ? null : system.ToString());
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

        return $"Gemini returned HTTP {status}: {detail}";
    }
}

// --- wire DTOs ---------------------------------------------------------------------------------

internal sealed class GeminiModelList
{
    [System.Text.Json.Serialization.JsonPropertyName("models")]
    public List<GeminiModelEntry>? Models { get; set; }
}

internal sealed class GeminiModelEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("supportedGenerationMethods")]
    public List<string>? SupportedGenerationMethods { get; set; }
}
