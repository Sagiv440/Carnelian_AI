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

    // --- Project-scoped store: <projectDir>/.AI/chats/<sessionId>.json ---

    private static string ChatsDir(string projectDirectory) =>
        Path.Combine(projectDirectory, ".AI", "chats");

    public IReadOnlyList<ChatSession> LoadFrom(string projectDirectory)
    {
        try
        {
            var dir = ChatsDir(projectDirectory);
            if (!Directory.Exists(dir))
                return Array.Empty<ChatSession>();

            var sessions = new List<ChatSession>();
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var session = JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(file));
                    if (session is not null)
                        sessions.Add(session);
                }
                catch
                {
                    // Skip a single unreadable/corrupt chat file rather than failing the whole load.
                }
            }

            sessions.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt)); // newest first
            return sessions;
        }
        catch
        {
            return Array.Empty<ChatSession>();
        }
    }

    public void SaveTo(string projectDirectory, IReadOnlyList<ChatSession> sessions)
    {
        try
        {
            var dir = ChatsDir(projectDirectory);
            Directory.CreateDirectory(dir);

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions)
            {
                var fileName = session.Id + ".json";
                File.WriteAllText(Path.Combine(dir, fileName), JsonSerializer.Serialize(session, JsonOptions));
                keep.Add(fileName);
            }

            // Drop files for sessions that were removed from the log.
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                if (!keep.Contains(Path.GetFileName(file)))
                    try { File.Delete(file); } catch { /* ignore */ }
        }
        catch
        {
            // Best-effort: a failed save must not crash the app.
        }
    }
}
