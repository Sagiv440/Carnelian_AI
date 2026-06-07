using AI_Interface.Models;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AI_Interface.Services;

/// <summary>
/// Default <see cref="IThemeService"/>. Sets <see cref="Application.RequestedThemeVariant"/> for
/// light/dark and overrides the app-level brush resources that the UI references via DynamicResource,
/// so changes apply live across all open windows.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public const string AccentBrushKey = "AppAccentBrush";
    public const string UserBubbleBrushKey = "UserBubbleBrush";
    public const string AssistantBubbleBrushKey = "AssistantBubbleBrush";

    public void Apply(AppSettings settings)
    {
        var app = Application.Current;
        if (app is null)
            return;

        app.RequestedThemeVariant = settings.ThemeMode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default // follow the OS
        };

        SetBrush(app, AccentBrushKey, settings.AccentColor);
        SetBrush(app, UserBubbleBrushKey, settings.UserBubbleColor);
        SetBrush(app, AssistantBubbleBrushKey, settings.AssistantBubbleColor);
    }

    private static void SetBrush(Application app, string key, string? hex)
    {
        // Ignore invalid/partial hex (e.g. mid-typing in the settings UI) and keep the previous brush.
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color))
            app.Resources[key] = new SolidColorBrush(color);
    }
}
