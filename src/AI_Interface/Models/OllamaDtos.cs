using System.Collections.Generic;
using System.Text.Json;
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

    /// <summary>Tools the model may call (function calling). Omitted from JSON when null.</summary>
    [JsonPropertyName("tools")]
    public List<OllamaTool>? Tools { get; set; }

    /// <summary>
    /// Enables the model's native chain-of-thought ("thinking"). Omitted from JSON when null so
    /// non-thinking models are unaffected; only thinking models (qwen3, deepseek-r1, …) honor it.
    /// </summary>
    [JsonPropertyName("think")]
    public bool? Think { get; set; }
}

public sealed class OllamaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>
    /// The model's reasoning, returned separately from <see cref="Content"/> when thinking is enabled.
    /// Present on responses from thinking models; null otherwise.
    /// </summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    /// <summary>Base64-encoded images for vision models. Omitted from JSON when null.</summary>
    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    /// <summary>Tool calls the assistant requested. Omitted from JSON when null.</summary>
    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; set; }

    /// <summary>For a tool-result message: the name of the tool whose output this carries.</summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }
}

/// <summary>A tool definition advertised to the model in a chat request.</summary>
public sealed class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OllamaFunctionDef Function { get; set; } = new();
}

public sealed class OllamaFunctionDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>JSON-schema object describing the function's parameters.</summary>
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

/// <summary>A tool call the model emitted in its response message.</summary>
public sealed class OllamaToolCall
{
    [JsonPropertyName("function")]
    public OllamaFunctionCall? Function { get; set; }
}

public sealed class OllamaFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Ollama sends arguments as a JSON object (not a string, unlike OpenAI).</summary>
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

/// <summary>A single NDJSON progress chunk streamed back from <c>POST /api/pull</c>.</summary>
public sealed class OllamaPullChunk
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("completed")]
    public long? Completed { get; set; }

    [JsonPropertyName("total")]
    public long? Total { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
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
