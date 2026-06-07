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
}
