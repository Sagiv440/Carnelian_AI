namespace AI_Interface.ViewModels;

/// <summary>
/// Top-level Settings categories, grouped in the left rail under Editor Features / AI Features.
/// The right-hand content host switches on the selected value.
/// </summary>
public enum SettingsCategory
{
    // --- Editor Features ---
    Appearance,
    Typography,
    Layout,

    // --- AI Features ---
    Models,
    Agents,
    AutonomyAndMemory,
    WebSearch,
    Voice,
    ResearchAndThinking
}
