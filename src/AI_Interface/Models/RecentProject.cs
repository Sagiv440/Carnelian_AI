namespace AI_Interface.Models;

/// <summary>
/// A previously opened project remembered for the startup launcher: its display name and directory. Stored
/// (most-recent-first) in <see cref="AppSettings.RecentProjects"/> — a mutable class so it round-trips via JSON.
/// </summary>
public sealed class RecentProject
{
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
}
