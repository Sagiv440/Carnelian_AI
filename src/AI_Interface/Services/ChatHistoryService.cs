using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// JSON-file chat-log store under the per-user app-data folder
/// (<c>%APPDATA%\AI_Interface\chats.json</c> / <c>~/.config/AI_Interface/chats.json</c>).
/// </summary>
public sealed class ChatHistoryService : IChatHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public ChatHistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Interface");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "chats.json");
    }

    public IReadOnlyList<ChatSession> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<ChatSession>();
            return JsonSerializer.Deserialize<List<ChatSession>>(File.ReadAllText(_filePath))
                   ?? new List<ChatSession>();
        }
        catch
        {
            return Array.Empty<ChatSession>();
        }
    }

    public void Save(IReadOnlyList<ChatSession> sessions)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(sessions, JsonOptions));
        }
        catch
        {
            // Best-effort: a failed save must not crash the app.
        }
    }
}
