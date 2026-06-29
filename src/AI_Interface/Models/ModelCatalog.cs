using System;
using System.Collections.Generic;
using System.Linq;

namespace AI_Interface.Models;

/// <summary>
/// Capabilities / modalities a model can interact with — what each model "talks to". Rendered as
/// icons in the Model Config list so you can see at a glance what a model supports.
/// </summary>
[Flags]
public enum ModelCapabilities
{
    None = 0,
    Text = 1 << 0,       // 💬 text chat — every chat model
    Tools = 1 << 1,      // 🛠 tool / function calling (needed for Project / agent mode)
    Vision = 1 << 2,     // 👁 image input
    Reasoning = 1 << 3,  // 🧠 native step-by-step thinking
}

/// <summary>One known Ollama model variant and the metadata needed to size it for the recommender.</summary>
/// <param name="Name">Ollama pull name, e.g. "qwen2.5-coder:7b".</param>
/// <param name="ParamsB">Approximate parameter count, in billions.</param>
/// <param name="Category">What the model is best at.</param>
/// <param name="BaseSizeGb">Approximate on-disk / load size at the Q4_K_M quant.</param>
/// <param name="MaxContextK">Largest context window the model supports, in thousands of tokens.</param>
/// <param name="Capabilities">Which APIs / modalities the model supports.</param>
public sealed record ModelCatalogEntry(
    string Name, double ParamsB, ModelUseCase Category, double BaseSizeGb, int MaxContextK,
    ModelCapabilities Capabilities);

/// <summary>A catalog entry scored against the current hardware + preferences.</summary>
public sealed record ModelRecommendation(
    string Symbol, string Name, string PullName, string Quant, double SizeGb,
    ModelUseCase Category, string FitNote, double Score, bool IsInstalled,
    int MaxContextK, ModelCapabilities Capabilities)
{
    public string SizeDisplay => $"{SizeGb:0.0} GB";

    /// <summary>Max context window as a compact token label, e.g. "128K".</summary>
    public string TokensDisplay => MaxContextK > 0 ? $"{MaxContextK}K" : "—";

    // Per-capability flags drive the API icons in the Model Config row.
    public bool HasTools => Capabilities.HasFlag(ModelCapabilities.Tools);
    public bool HasVision => Capabilities.HasFlag(ModelCapabilities.Vision);
    public bool HasReasoning => Capabilities.HasFlag(ModelCapabilities.Reasoning);
}

/// <summary>A selectable context-window size (label for the UI, value in thousands of tokens).</summary>
public sealed record ContextOption(string Label, int ValueK);

/// <summary>
/// An API/capability filter option for the picker. <see cref="ModelCapabilities.None"/> means
/// "Any" (no filtering); otherwise the list is restricted to models that support the flag.
/// </summary>
public sealed record ApiOption(string Label, ModelCapabilities Required);

/// <summary>
/// A use-case / filter option for the picker. A null <paramref name="UseCase"/> with
/// <paramref name="DownloadedOnly"/> = true means "show only models already installed".
/// </summary>
public sealed record CategoryOption(string Label, ModelUseCase? UseCase, bool DownloadedOnly);

/// <summary>
/// A curated catalog of popular Ollama models plus the heuristics that rank them for a machine.
/// Sizes are approximate (there is no official registry API); the goal is a useful ordering, not an
/// exact byte count.
/// </summary>
public static class ModelCatalog
{
    /// <summary>Quantizations offered in the picker, smallest → largest.</summary>
    public static IReadOnlyList<string> Quants { get; } = new[]
    {
        "Q3_K_M", "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0"
    };

    /// <summary>Context sizes offered in the picker.</summary>
    public static IReadOnlyList<ContextOption> Contexts { get; } = new[]
    {
        new ContextOption("4K", 4),
        new ContextOption("8K", 8),
        new ContextOption("16K", 16),
        new ContextOption("32K", 32),
        new ContextOption("128K", 128)
    };

    // Size relative to the Q4_K_M baseline (BaseSizeGb).
    private static double QuantFactor(string quant) => quant switch
    {
        "Q3_K_M" => 0.82,
        "Q4_K_M" => 1.00,
        "Q5_K_M" => 1.16,
        "Q6_K" => 1.34,
        "Q8_0" => 1.73,
        _ => 1.00
    };

    public static string Glyph(ModelUseCase c) => c switch
    {
        ModelUseCase.Coding => "💻",
        ModelUseCase.Chat => "💬",
        ModelUseCase.Vision => "👁",
        ModelUseCase.Reasoning => "🧠",
        _ => "⚡"
    };

    // Capability shorthands.
    private const ModelCapabilities TextOnly = ModelCapabilities.Text;
    private const ModelCapabilities TextTools = ModelCapabilities.Text | ModelCapabilities.Tools;
    private const ModelCapabilities TextVision = ModelCapabilities.Text | ModelCapabilities.Vision;
    private const ModelCapabilities TextReasoning = ModelCapabilities.Text | ModelCapabilities.Reasoning;
    private const ModelCapabilities TextToolsReasoning = ModelCapabilities.Text | ModelCapabilities.Tools | ModelCapabilities.Reasoning;

    private static readonly ModelCatalogEntry[] Entries =
    {
        // ── General / standard ──────────────────────────────────────────────
        new("llama3.2:1b",        1.2,  ModelUseCase.Chat,     0.8,  128, TextTools),
        new("llama3.2:3b",        3.2,  ModelUseCase.Chat,     2.0,  128, TextTools),
        new("llama3.1:8b",        8,    ModelUseCase.Standard,  4.7, 128, TextTools),
        new("llama3.1:70b",       70,   ModelUseCase.Standard, 40.0, 128, TextTools),
        new("llama3.3:70b",       70,   ModelUseCase.Standard, 40.0, 128, TextTools),
        new("qwen2.5:7b",         7,    ModelUseCase.Standard,  4.7, 128, TextTools),
        new("qwen2.5:14b",        14,   ModelUseCase.Standard,  9.0, 128, TextTools),
        new("qwen2.5:32b",        32,   ModelUseCase.Standard, 20.0, 128, TextTools),
        new("qwen2.5:72b",        72,   ModelUseCase.Standard, 47.0, 128, TextTools),
        new("mistral:7b",         7,    ModelUseCase.Chat,      4.4,  32, TextTools),
        new("mistral-nemo:12b",   12,   ModelUseCase.Standard,  7.1, 128, TextTools),
        new("mistral-small:22b",  22,   ModelUseCase.Standard, 12.4,  32, TextTools),
        new("gemma2:2b",          2.6,  ModelUseCase.Chat,      1.6,   8, TextOnly),
        new("gemma2:9b",          9,    ModelUseCase.Standard,  5.4,   8, TextOnly),
        new("gemma2:27b",         27,   ModelUseCase.Standard, 16.0,   8, TextOnly),
        new("gemma3:1b",          1,    ModelUseCase.Chat,      0.8, 128, TextTools),
        new("gemma3:4b",          4,    ModelUseCase.Chat,      2.5, 128, TextTools),
        new("gemma3:12b",         12,   ModelUseCase.Standard,  7.7, 128, TextTools),
        new("gemma3:27b",         27,   ModelUseCase.Standard, 17.0, 128, TextTools),
        new("phi3:mini",          3.8,  ModelUseCase.Chat,      2.2, 128, TextOnly),
        new("phi3.5:3.8b",        3.8,  ModelUseCase.Chat,      2.2, 128, TextOnly),
        new("phi4:14b",           14,   ModelUseCase.Standard,  8.9,  16, TextTools),
        new("command-r:35b",      35,   ModelUseCase.Standard, 20.0, 128, TextTools),
        new("smollm2:1.7b",       1.7,  ModelUseCase.Chat,      1.0,   8, TextOnly),

        // ── Reasoning ───────────────────────────────────────────────────────
        new("deepseek-r1:1.5b",   1.5,  ModelUseCase.Reasoning,  1.1, 128, TextReasoning),
        new("deepseek-r1:7b",     7,    ModelUseCase.Reasoning,  4.7, 128, TextToolsReasoning),
        new("deepseek-r1:8b",     8,    ModelUseCase.Reasoning,  4.9, 128, TextToolsReasoning),
        new("deepseek-r1:14b",    14,   ModelUseCase.Reasoning,  9.0, 128, TextToolsReasoning),
        new("deepseek-r1:32b",    32,   ModelUseCase.Reasoning, 19.8, 128, TextToolsReasoning),
        new("deepseek-r1:70b",    70,   ModelUseCase.Reasoning, 42.5, 128, TextToolsReasoning),
        new("qwq:32b",            32,   ModelUseCase.Reasoning, 19.8, 128, TextToolsReasoning),
        new("phi4-reasoning:14b", 14,   ModelUseCase.Reasoning,  9.2,  16, TextToolsReasoning),

        // ── Coding ──────────────────────────────────────────────────────────
        new("qwen2.5-coder:1.5b", 1.5,  ModelUseCase.Coding,  1.0,  32, TextTools),
        new("qwen2.5-coder:7b",   7,    ModelUseCase.Coding,  4.7,  32, TextTools),
        new("qwen2.5-coder:14b",  14,   ModelUseCase.Coding,  9.0,  32, TextTools),
        new("qwen2.5-coder:32b",  32,   ModelUseCase.Coding, 20.0,  32, TextTools),
        new("devstral:24b",       24,   ModelUseCase.Coding, 15.0, 128, TextTools),
        new("codellama:7b",       7,    ModelUseCase.Coding,  3.8,  16, TextOnly),
        new("codellama:13b",      13,   ModelUseCase.Coding,  7.4,  16, TextOnly),
        new("codegemma:7b",       7,    ModelUseCase.Coding,  5.0,   8, TextTools),
        new("deepseek-coder-v2:16b", 16, ModelUseCase.Coding, 8.9, 128, TextOnly),
        new("starcoder2:7b",      7,    ModelUseCase.Coding,  4.3,  16, TextOnly),
        new("starcoder2:15b",     15,   ModelUseCase.Coding,  9.1,  16, TextOnly),
        new("granite-code:8b",    8,    ModelUseCase.Coding,  4.6, 128, TextOnly),
        new("granite-code:20b",   20,   ModelUseCase.Coding, 12.1,   8, TextOnly),

        // ── Vision ──────────────────────────────────────────────────────────
        new("llava:7b",               7,   ModelUseCase.Vision, 4.7,   4, TextVision),
        new("llava:13b",              13,  ModelUseCase.Vision, 8.0,   4, TextVision),
        new("llava-phi3:3.8b",        3.8, ModelUseCase.Vision, 2.9,   4, TextVision),
        new("llama3.2-vision:11b",    11,  ModelUseCase.Vision, 7.9, 128, TextVision),
        new("qwen2.5vl:7b",           7,   ModelUseCase.Vision, 5.0, 128, TextVision),
        new("minicpm-v:8b",           8,   ModelUseCase.Vision, 5.5,  32, TextVision),
        new("moondream:1.8b",         1.8, ModelUseCase.Vision, 1.5,   2, TextVision),
        new("granite3.2-vision:2b",   2,   ModelUseCase.Vision, 1.5,  16, TextVision),
    };

    /// <summary>Ranks every catalog model for the given hardware/preferences, best match first.</summary>
    /// <param name="installed">Names of models already pulled locally (to flag / filter).</param>
    /// <param name="onlyDownloaded">When true, return only installed models.</param>
    public static IReadOnlyList<ModelRecommendation> Recommend(
        HardwareInfo? hw, ModelUseCase useCase, string quant, int contextK,
        ISet<string> installed, bool onlyDownloaded)
    {
        var budget = hw?.BudgetGb ?? 0;
        var factor = QuantFactor(quant);

        var list = new List<ModelRecommendation>(Entries.Length);
        foreach (var e in Entries)
        {
            var isInstalled = IsInstalledMatch(e.Name, installed);
            if (onlyDownloaded && !isInstalled)
                continue;

            var ctxK = Math.Min(contextK, e.MaxContextK);
            var size = Math.Round(e.BaseSizeGb * factor, 1);
            var kv = e.ParamsB * ctxK * 0.012;          // rough KV-cache cost of the context window
            var required = size + kv + 0.8;             // + runtime overhead
            var ratio = budget > 0 ? required / budget : double.NaN;

            string symbol, note;
            if (double.IsNaN(ratio))
            {
                symbol = "•";
                note = "Scan your hardware to check whether this fits.";
            }
            else if (ratio <= 0.75)
            {
                symbol = "✅";
                note = $"Runs comfortably — needs ~{required:0.0} GB of your ~{budget:0.0} GB.";
            }
            else if (ratio <= 1.0)
            {
                symbol = "🟡";
                note = $"Tight fit — needs ~{required:0.0} GB of your ~{budget:0.0} GB.";
            }
            else
            {
                symbol = "🔴";
                note = $"Likely too large — needs ~{required:0.0} GB but you have ~{budget:0.0} GB.";
            }

            var score = onlyDownloaded ? FitOnlyScore(e, ratio) : Score(e, useCase, ratio);
            var name = $"{Glyph(e.Category)} {e.Name}";
            list.Add(new ModelRecommendation(
                symbol, name, e.Name, quant, size, e.Category, note, score, isInstalled,
                e.MaxContextK, e.Capabilities));
        }

        return list.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>An installed tag matches a catalog entry on an exact or prefix basis
    /// (installed tags often carry an "-instruct-qN" suffix).</summary>
    private static bool IsInstalledMatch(string catalogName, ISet<string> installed)
    {
        foreach (var n in installed)
            if (string.Equals(n, catalogName, StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith(catalogName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // Ranking for the "Downloaded" filter: fit + capability only (category is irrelevant there).
    private static double FitOnlyScore(ModelCatalogEntry e, double ratio)
    {
        var known = !double.IsNaN(ratio);
        if (!known)
            return e.ParamsB;
        var fits = ratio <= 1.0;
        var comfy = ratio <= 0.75;
        return (fits ? 1_000 : -(ratio - 1.0) * 500) + (comfy ? 100 : 0) + e.ParamsB;
    }

    // Tiered score: category match dominates, then whether it fits, then capability as a tiebreaker.
    private static double Score(ModelCatalogEntry e, ModelUseCase pref, double ratio)
    {
        var known = !double.IsNaN(ratio);
        var fits = known && ratio <= 1.0;
        var comfy = known && ratio <= 0.75;

        double s = 0;
        if (Matches(e.Category, pref))
            s += 10_000;

        if (!known)
            s += e.ParamsB;                          // no scan yet → order by capability within category
        else if (fits)
            s += 1_000 + (comfy ? 100 : 0) + e.ParamsB;
        else
            s += -(ratio - 1.0) * 500 - e.ParamsB;   // doesn't fit: worse the more it overflows

        return s;
    }

    private static bool Matches(ModelUseCase category, ModelUseCase preference) => preference switch
    {
        ModelUseCase.Vision    => category == ModelUseCase.Vision,
        ModelUseCase.Coding    => category == ModelUseCase.Coding,
        ModelUseCase.Reasoning => category == ModelUseCase.Reasoning,
        ModelUseCase.Chat      => category is ModelUseCase.Chat or ModelUseCase.Standard,
        _                      => category is ModelUseCase.Standard or ModelUseCase.Chat // Standard
    };
}
