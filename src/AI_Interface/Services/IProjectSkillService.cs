using System.Collections.Generic;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Finds and loads skill files inside a project directory for the project agent to follow.</summary>
public interface IProjectSkillService
{
    /// <summary>
    /// Scans the project for skill files (SKILL.md, *.skill.md, or markdown under a "skills" folder)
    /// and returns their contents. Best-effort and bounded; never throws.
    /// </summary>
    IReadOnlyList<ProjectSkill> Load(string projectDirectory);
}
