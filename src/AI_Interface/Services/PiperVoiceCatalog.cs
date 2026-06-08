using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Reads the Piper voice index (<c>voices.json</c>) from the rhasspy/piper-voices repo for the
/// browseable catalog, downloads chosen voices into the installer's voices folder, and resolves a
/// downloaded voice for a given language so replies can be read in the language they're written in.
/// </summary>
public sealed class PiperVoiceCatalog : IPiperVoiceCatalog
{
    private const string RepoBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
    private const string IndexUrl = RepoBase + "voices.json";

    // Quality preference when several voices exist for the same language.
    private static readonly string[] QualityRank = { "high", "medium", "low", "x_low" };

    private readonly HttpClient _http;
    private readonly IPiperInstaller _installer;

    public PiperVoiceCatalog(HttpClient http, IPiperInstaller installer)
    {
        _http = http;
        _installer = installer;
    }

    private string VoicesDir => _installer.VoicesDirectory;

    public async Task<IReadOnlyList<PiperVoiceInfo>> ListAvailableAsync(CancellationToken ct)
    {
        var json = await _http.GetStringAsync(IndexUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var voices = new List<PiperVoiceInfo>();
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            var v = Parse(entry.Name, entry.Value);
            if (v is not null)
            {
                v.IsDownloaded = IsDownloaded(v);
                voices.Add(v);
            }
        }

        return voices
            .OrderBy(v => v.LanguageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Quality, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PiperVoiceInfo? Parse(string key, JsonElement value)
    {
        try
        {
            var name = value.TryGetProperty("name", out var n) ? n.GetString() ?? key : key;
            var quality = value.TryGetProperty("quality", out var q) ? q.GetString() ?? "" : "";

            string code = key, family = key, langName = key;
            if (value.TryGetProperty("language", out var lang))
            {
                code = lang.TryGetProperty("code", out var c) ? c.GetString() ?? key : key;
                family = lang.TryGetProperty("family", out var f) ? f.GetString() ?? code : code;
                var english = lang.TryGetProperty("name_english", out var e) ? e.GetString() : null;
                var country = lang.TryGetProperty("country_english", out var co) ? co.GetString() : null;
                langName = english is null ? code
                    : string.IsNullOrWhiteSpace(country) ? english : $"{english} ({country})";
            }

            // Locate the .onnx model and its .onnx.json config among the listed files.
            string? onnx = null, onnxJson = null;
            long size = 0;
            if (value.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateObject())
                {
                    var path = file.Name;
                    if (path.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase))
                        onnxJson = path;
                    else if (path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                    {
                        onnx = path;
                        if (file.Value.TryGetProperty("size_bytes", out var sz) && sz.TryGetInt64(out var b))
                            size = b;
                    }
                }
            }

            if (onnx is null || onnxJson is null)
                return null;

            return new PiperVoiceInfo
            {
                Key = key,
                LanguageCode = code,
                LanguageFamily = family,
                LanguageName = langName,
                Name = name,
                Quality = quality,
                OnnxRepoPath = onnx,
                OnnxJsonRepoPath = onnxJson,
                SizeBytes = size,
            };
        }
        catch
        {
            return null; // skip a malformed entry rather than failing the whole catalog
        }
    }

    public async Task DownloadAsync(PiperVoiceInfo voice, IProgress<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(VoicesDir);
        var onnxDest = ModelPath(voice.Key);
        var jsonDest = ConfigPath(voice.Key);

        await HttpDownloads.ToFileAsync(
            _http, RepoBase + voice.OnnxRepoPath + "?download=true", onnxDest,
            $"Downloading {voice.Name}", progress, ct).ConfigureAwait(false);

        await HttpDownloads.ToFileAsync(
            _http, RepoBase + voice.OnnxJsonRepoPath + "?download=true", jsonDest,
            $"Downloading {voice.Name} config", progress, ct).ConfigureAwait(false);

        voice.IsDownloaded = true;
    }

    public void Delete(PiperVoiceInfo voice)
    {
        TryDelete(ModelPath(voice.Key));
        TryDelete(ConfigPath(voice.Key));
        voice.IsDownloaded = false;
    }

    public bool IsDownloaded(PiperVoiceInfo voice) =>
        File.Exists(ModelPath(voice.Key)) && File.Exists(ConfigPath(voice.Key));

    public string? ResolveModelPathForLanguage(string languageFamily)
    {
        if (string.IsNullOrWhiteSpace(languageFamily))
            return AnyInstalledModelPath();

        var matches = InstalledModels()
            .Where(m => FamilyOf(m).Equals(languageFamily, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => QualityIndex(m))
            .ToList();

        return matches.FirstOrDefault();
    }

    public string? AnyInstalledModelPath() => InstalledModels().FirstOrDefault();

    // --- local helpers ---

    private string ModelPath(string key) => Path.Combine(VoicesDir, key + ".onnx");
    private string ConfigPath(string key) => Path.Combine(VoicesDir, key + ".onnx.json");

    /// <summary>Paths of locally downloaded voices (a .onnx that also has its .onnx.json beside it).</summary>
    private IEnumerable<string> InstalledModels()
    {
        if (!Directory.Exists(VoicesDir))
            return Enumerable.Empty<string>();

        return Directory.EnumerateFiles(VoicesDir)
            .Where(p => p.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                        && !p.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(p + ".json"));
    }

    /// <summary>Language family from a model filename, e.g. <c>en_US-amy-medium.onnx</c> → <c>en</c>.</summary>
    private static string FamilyOf(string modelPath)
    {
        var stem = Path.GetFileNameWithoutExtension(modelPath); // en_US-amy-medium
        var locale = stem.Split('-', 2)[0];                     // en_US
        return locale.Split('_', 2)[0];                         // en
    }

    private static int QualityIndex(string modelPath)
    {
        var stem = Path.GetFileNameWithoutExtension(modelPath);
        var parts = stem.Split('-');
        var quality = parts.Length > 0 ? parts[^1] : "";
        var idx = Array.IndexOf(QualityRank, quality);
        return idx < 0 ? QualityRank.Length : idx;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
