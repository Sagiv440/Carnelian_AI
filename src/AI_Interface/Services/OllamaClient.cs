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
/// Default <see cref="IOllamaClient"/>. The base URL is read from settings on every call so the
/// user can repoint the app at a different Ollama instance without a restart.
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Stand-in for tool-call arguments when the model omitted them (a default JsonElement can't be serialized).</summary>
    private static readonly JsonElement EmptyJsonObject = JsonSerializer.SerializeToElement(new { });

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public OllamaClient(HttpClient http, ISettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseUrl => _settings.Current.OllamaBaseUrl.TrimEnd('/');

    public AiProvider Provider => AiProvider.Ollama;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => PingAsync(BaseUrl, ct);

    /// <summary>Reachability for the router (Ollama needs no key — delegates to <see cref="IsAvailableAsync"/>).</summary>
    public Task<bool> IsConfiguredAndReachableAsync(CancellationToken ct = default) => IsAvailableAsync(ct);

    public async Task<bool> PingAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        // The shared HttpClient has a 10-min timeout (for generation); a reachability probe must fail
        // fast, so bound it to a few seconds via a linked token.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var resp = await _http.GetAsync($"{baseUrl.TrimEnd('/')}/api/tags", timeoutCts.Token)
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
        using var resp = await _http.GetAsync($"{BaseUrl}/api/tags", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var tags = await JsonSerializer.DeserializeAsync<OllamaTagsResponse>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        return tags?.Models.Select(m => m.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
               ?? new List<string>();
    }

    public async Task PullModelAsync(string name, IProgress<string>? progress, CancellationToken ct = default)
    {
        var request = new { name, stream = true };
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/pull")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        // ResponseHeadersRead so the 10-min client timeout only bounds time-to-first-byte, not the
        // whole (potentially multi-GB) download.
        using var resp = await _http
            .SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, body));
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaPullChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaPullChunk>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null)
                continue;
            if (!string.IsNullOrEmpty(chunk.Error))
                throw new InvalidOperationException($"Ollama error: {chunk.Error}");

            progress?.Report(FormatPullProgress(chunk));
        }
    }

    private static string FormatPullProgress(OllamaPullChunk chunk)
    {
        var status = string.IsNullOrEmpty(chunk.Status) ? "downloading" : chunk.Status;
        if (chunk.Total is > 0 && chunk.Completed is >= 0)
        {
            var pct = (int)(100.0 * chunk.Completed.Value / chunk.Total.Value);
            return $"{status} {pct}%";
        }
        return status;
    }

    public async Task DeleteModelAsync(string name, CancellationToken ct = default)
    {
        using var httpReq = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/delete")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { name }, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, body));
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        bool think,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var wire = messages
            .Select(m => new OllamaChatMessage
            {
                Role = m.Role.ToWire(),
                Content = m.Content,
                Images = m.Images is { Count: > 0 } imgs ? new List<string>(imgs) : null
            })
            .ToList();

        var resp = await SendChatStreamAsync(model, wire, think, ct).ConfigureAwait(false);

        // Some models reject think:true with HTTP 400 ("… does not support thinking"). Retry without it
        // so enabling Thinking never breaks a non-thinking model.
        if (think && !resp.IsSuccessStatusCode && (int)resp.StatusCode == 400)
        {
            var probe = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            resp.Dispose();
            if (probe.Contains("think", StringComparison.OrdinalIgnoreCase))
                resp = await SendChatStreamAsync(model, wire, false, ct).ConfigureAwait(false);
            else
                throw new InvalidOperationException(BuildErrorMessage(400, probe));
        }

        try
        {
            if (!resp.IsSuccessStatusCode)
            {
                // Ollama returns a JSON body like {"error":"..."} — surface it instead of a generic status.
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, body));
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var thinkOpen = false;
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                OllamaChatChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue; // skip malformed line rather than aborting the stream
                }

                if (chunk is null)
                    continue;
                if (!string.IsNullOrEmpty(chunk.Error))
                    throw new InvalidOperationException($"Ollama error: {chunk.Error}");

                // Reasoning comes in a separate "thinking" field; wrap it in <think>…</think> so the UI's
                // reasoning splitter routes it to the collapsible block instead of the answer.
                var thinking = chunk.Message?.Thinking;
                if (!string.IsNullOrEmpty(thinking))
                {
                    if (!thinkOpen) { thinkOpen = true; yield return "<think>"; }
                    yield return thinking;
                }

                var delta = chunk.Message?.Content;
                if (!string.IsNullOrEmpty(delta))
                {
                    if (thinkOpen) { thinkOpen = false; yield return "</think>"; }
                    yield return delta;
                }

                if (chunk.Done)
                {
                    if (thinkOpen) yield return "</think>";
                    yield break;
                }
            }

            if (thinkOpen) yield return "</think>";
        }
        finally
        {
            resp.Dispose();
        }
    }

    /// <summary>Posts a streaming chat request (optionally enabling native thinking) and returns the open response.</summary>
    private Task<HttpResponseMessage> SendChatStreamAsync(
        string model, List<OllamaChatMessage> messages, bool think, CancellationToken ct)
    {
        var request = new OllamaChatRequest
        {
            Model = model,
            Stream = true,
            Messages = messages,
            Think = think ? true : null
        };

        var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        // ResponseHeadersRead so the 10-min client timeout only bounds time-to-first-byte.
        return _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static string BuildErrorMessage(int status, string body)
    {
        string? detail = null;
        try
        {
            detail = JsonSerializer.Deserialize<OllamaChatChunk>(body, JsonOptions)?.Error;
        }
        catch (JsonException)
        {
            // Body wasn't the expected JSON; fall back to the raw text below.
        }

        if (string.IsNullOrWhiteSpace(detail))
            detail = string.IsNullOrWhiteSpace(body) ? "(no details)" : body.Trim();

        return $"Ollama returned HTTP {status}: {detail}";
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
        var request = new OllamaChatRequest
        {
            Model = model,
            Stream = false, // tool-use rounds are request/response, not streamed
            Messages = messages.Select(ToWireMessage).ToList(),
            Tools = tools.Select(t => new OllamaTool
            {
                Function = new OllamaFunctionDef
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters
                }
            }).ToList()
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildErrorMessage((int)resp.StatusCode, body));

        var chunk = JsonSerializer.Deserialize<OllamaChatChunk>(body, JsonOptions);
        if (chunk is null)
            throw new InvalidOperationException("Ollama returned an empty response.");
        if (!string.IsNullOrEmpty(chunk.Error))
            throw new InvalidOperationException($"Ollama error: {chunk.Error}");

        var content = chunk.Message?.Content ?? "";
        var calls = chunk.Message?.ToolCalls?
            .Where(c => c.Function is not null)
            .Select(c => new AgentToolCall(c.Function!.Name, c.Function.Arguments))
            .ToList() ?? new List<AgentToolCall>();

        return new AgentTurn(content, calls);
    }

    /// <summary>Maps a domain <see cref="ChatMessage"/> (including tool calls/results) to the wire shape.</summary>
    private static OllamaChatMessage ToWireMessage(ChatMessage m) => new()
    {
        Role = m.Role.ToWire(),
        Content = m.Content,
        Images = m.Images is { Count: > 0 } imgs ? new List<string>(imgs) : null,
        ToolName = m.ToolName,
        ToolCalls = m.ToolCalls is { Count: > 0 } tcs
            ? tcs.Select(tc => new OllamaToolCall
            {
                Function = new OllamaFunctionCall
                {
                    Name = tc.Name,
                    Arguments = tc.Arguments.ValueKind == JsonValueKind.Undefined ? EmptyJsonObject : tc.Arguments
                }
            }).ToList()
            : null
    };
}
