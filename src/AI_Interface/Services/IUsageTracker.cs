using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Accumulates an <b>estimated</b> dollar spend per added cloud provider so the Web Models "Active
/// Providers" budget can count down. Called after each completed cloud reply; local Ollama replies and
/// providers the user hasn't added are ignored. The estimate is deliberately rough (see <c>ModelPricing</c>):
/// it's a ~4-chars-per-token heuristic over the prompt + the visible reply, so it does <b>not</b> count
/// conversation history, system prompts, or — in Project/agent mode — tool-result round-trips. Treat the
/// budget as a ballpark, not a bill; multi-step agent turns in particular are under-counted.
/// </summary>
public interface IUsageTracker
{
    /// <summary>
    /// Add the estimated cost of one completed reply (from the input/output text and the model's price)
    /// to that provider's running <c>SpentUsd</c>, then persist. Best-effort and silent — never throws into
    /// the chat path.
    /// </summary>
    void RecordEstimatedUsage(AiProvider provider, string modelId, string? inputText, string? outputText);
}
