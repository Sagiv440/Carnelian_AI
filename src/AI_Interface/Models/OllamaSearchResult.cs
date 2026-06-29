namespace AI_Interface.Models;

/// <summary>One result returned by the Ollama library search API.</summary>
public sealed record OllamaSearchResult(string Name, string Description);
