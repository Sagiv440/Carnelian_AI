using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AI_Interface.Models;

// Wire-format DTOs for the Ollama HTTP API (https://github.com/ollama/ollama/blob/main/docs/api.md).
// Kept separate from the app's domain models so the API shape can change without rippling outward.

/// <summary>Response of <c>GET /api/tags</c>.</summary>
public sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo> Models { get; set; } = new();
}

public sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>Request body for <c>POST /api/chat</c>.</summary>
public sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;
}

public sealed class OllamaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

/// <summary>A single NDJSON chunk streamed back from <c>POST /api/chat</c>.</summary>
public sealed class OllamaChatChunk
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
