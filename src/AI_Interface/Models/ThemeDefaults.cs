namespace AI_Interface.Models;

/// <summary>
/// Default theme colors and the preset swatch palette. The look is a flat "IDE" theme
/// (Visual Studio Code / Photoshop): neutral dark-gray surfaces, system type, red-orange accent.
/// </summary>
public static class ThemeDefaults
{
    /// <summary>Primary accent — drives the Send button and highlights (red-orange).</summary>
    public const string Accent = "#F2542D";

    /// <summary>Background of the user's own message bubbles (translucent accent).</summary>
    public const string UserBubble = "#2EF2542D";

    /// <summary>Background of assistant message bubbles (neutral, theme-agnostic).</summary>
    public const string AssistantBubble = "#22888888";

    /// <summary>Default font family — the system UI font (see <c>App.axaml</c> AppFont).</summary>
    public const string FontFamily = "Segoe UI";

    /// <summary>Default base font size (compact, IDE-style).</summary>
    public const double FontSize = 13;

    /// <summary>Default line-spacing multiplier (relative to font size). 1.0 = tight, 1.5 = spacious.</summary>
    public const double LineSpacing = 1.3;

    /// <summary>Selectable font families for the Theme tab. "Poppins" maps to the embedded font.</summary>
    public static readonly string[] Fonts =
    {
        "Segoe UI", "Cascadia Code", "Consolas", "Inter", "Poppins", "Arial",
        "Calibri", "Verdana", "Georgia", "Times New Roman"
    };

    /// <summary>Preset color swatches offered in the Theme settings tab.</summary>
    public static readonly string[] Palette =
    {
        "#F2542D", // red-orange (accent)
        "#FF8A3D", // orange
        "#E0B341", // amber
        "#3FB950", // green
        "#3794D2", // blue (VS Code)
        "#4EC9B0", // teal
        "#C586C0", // mauve
        "#9CA3AF"  // gray
    };
}
