namespace AI_Interface.Models;

/// <summary>
/// A project workspace: a name plus the directory the project agent is allowed to read, modify,
/// and run terminal commands in. Held in memory for the active session (single active project).
/// </summary>
public sealed record Project(string Name, string Directory);
