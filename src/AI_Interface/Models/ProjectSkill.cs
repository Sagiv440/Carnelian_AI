namespace AI_Interface.Models;

/// <summary>A skill file discovered in a project: its name and full markdown content.</summary>
public sealed record ProjectSkill(string Name, string Content);
