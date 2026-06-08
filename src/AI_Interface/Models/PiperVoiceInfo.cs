namespace AI_Interface.Models;

/// <summary>
/// One Piper voice from the published catalog (huggingface.co/rhasspy/piper-voices), plus whether
/// it's already downloaded locally. Plain DTO; the voice-browser view model wraps these for display.
/// </summary>
public sealed class PiperVoiceInfo
{
    /// <summary>Catalog key, e.g. <c>en_US-amy-medium</c>. Also the local file stem.</summary>
    public required string Key { get; init; }

    /// <summary>Full locale, e.g. <c>en_US</c>.</summary>
    public required string LanguageCode { get; init; }

    /// <summary>Language family used for grouping/auto-selection, e.g. <c>en</c>.</summary>
    public required string LanguageFamily { get; init; }

    /// <summary>Human-readable language, e.g. <c>English (United States)</c>.</summary>
    public required string LanguageName { get; init; }

    /// <summary>Voice name, e.g. <c>amy</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Quality tier: <c>x_low</c> / <c>low</c> / <c>medium</c> / <c>high</c>.</summary>
    public required string Quality { get; init; }

    /// <summary>Repo-relative path of the <c>.onnx</c> model file (for building the download URL).</summary>
    public required string OnnxRepoPath { get; init; }

    /// <summary>Repo-relative path of the <c>.onnx.json</c> config that must sit beside the model.</summary>
    public required string OnnxJsonRepoPath { get; init; }

    /// <summary>Download size of the model in bytes (0 if unknown).</summary>
    public long SizeBytes { get; init; }

    /// <summary>True when both files are present in the local voices folder. Set when the list is built.</summary>
    public bool IsDownloaded { get; set; }

    /// <summary>"amy · medium" — shown as the voice's title in the list.</summary>
    public string DisplayName => $"{Name} · {Quality}";

    /// <summary>Approx size for display, e.g. "60 MB".</summary>
    public string SizeDisplay => SizeBytes > 0 ? $"{SizeBytes / 1_048_576} MB" : "";
}
