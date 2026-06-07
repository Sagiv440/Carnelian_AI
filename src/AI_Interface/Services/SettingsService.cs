using System;
using System.IO;
using System.Text.Json;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// JSON-file settings store under the per-user application-data folder
/// (<c>%APPDATA%\AI_Interface</c> on Windows, <c>~/.config/AI_Interface</c> on Linux).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettings Current { get; }

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Interface");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");

        Current = Load() ?? new AppSettings();
    }

    private AppSettings? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch
        {
            // Corrupt or unreadable settings should never block startup.
            return null;
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Best-effort: a failed save is not worth crashing the app over.
        }
    }
}
