using System.Collections.Generic;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Persists the chat log (all saved conversations) across runs.</summary>
public interface IChatHistoryService
{
    /// <summary>Loads saved sessions, newest first. Empty if none/unreadable.</summary>
    IReadOnlyList<ChatSession> Load();

    /// <summary>Persists the full set of sessions (best-effort; never throws).</summary>
    void Save(IReadOnlyList<ChatSession> sessions);

    /// <summary>Loads a project's chats from its <c>.AI/chats</c> folder, newest first.</summary>
    IReadOnlyList<ChatSession> LoadFrom(string projectDirectory);

    /// <summary>Persists a project's chats into its <c>.AI/chats</c> folder (best-effort; never throws).</summary>
    void SaveTo(string projectDirectory, IReadOnlyList<ChatSession> sessions);
}
