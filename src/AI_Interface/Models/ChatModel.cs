namespace AI_Interface.Models;

/// <summary>
/// A model offered in the picker, tagged by the provider that serves it. Replaces the bare model
/// string so the UI (and routing) can distinguish a local Ollama model from a cloud one. Records
/// give value equality, so a restored selection round-trips by (Provider, Id).
/// </summary>
public sealed record ChatModel(AiProvider Provider, string Id)
{
    /// <summary>Label for the picker: the model id followed by a small provider tag.</summary>
    public string Display => $"{Id}  ·  {Provider.Tag()}";
}
