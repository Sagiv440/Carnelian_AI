namespace AI_Interface.Models;

/// <summary>
/// Default theme colors and the preset swatch palette. The palette is drawn from
/// sagiv440.github.io/sagiv-reuben (purple/violet/teal/cyan/blue on near-black).
/// </summary>
public static class ThemeDefaults
{
    /// <summary>Primary accent — drives the Send button and highlights.</summary>
    public const string Accent = "#804DEE";

    /// <summary>Background of the user's own message bubbles (translucent accent).</summary>
    public const string UserBubble = "#33804DEE";

    /// <summary>Background of assistant message bubbles (neutral, theme-agnostic).</summary>
    public const string AssistantBubble = "#22888888";

    /// <summary>Preset color swatches offered in the Theme settings tab.</summary>
    public static readonly string[] Palette =
    {
        "#804DEE", // purple
        "#BF61FF", // violet
        "#00CEA8", // teal
        "#22D3EE", // cyan
        "#3B82F6", // blue
        "#F5AF19", // amber
        "#F12711", // red
        "#9CA3AF"  // gray
    };
}
