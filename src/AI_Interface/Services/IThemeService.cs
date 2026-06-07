using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Applies appearance (light/dark) and custom colors to the running application.</summary>
public interface IThemeService
{
    /// <summary>
    /// Applies the given settings to the live app: sets the theme variant and overrides the
    /// accent / bubble brush resources. Safe to call repeatedly for live preview.
    /// </summary>
    void Apply(AppSettings settings);
}
