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

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public OllamaClient(HttpClient http, ISettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseUrl => _settings.Current.OllamaBaseUrl.TrimEnd('/');

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl}/api/tags", ct).ConfigureAwait(false);
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

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new OllamaChatRequest
        {
            Model = model,
            Stream = true,
            Messages = messages
                .Select(m => new OllamaChatMessage { Role = m.Role.ToWire(), Content = m.Content })
                .ToList()
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var resp = await _http
            .SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

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

            var delta = chunk.Message?.Content;
            if (!string.IsNullOrEmpty(delta))
                yield return delta;

            if (chunk.Done)
                yield break;
        }
    }

    public async Task<string> CompleteAsync(
        string model, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var delta in ChatStreamAsync(model, messages, ct).ConfigureAwait(false))
            sb.Append(delta);
        return sb.ToString();
    }
}
