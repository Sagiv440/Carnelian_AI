using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Loads and persists <see cref="AppSettings"/> across runs.</summary>
public interface ISettingsService
{
    /// <summary>The live settings instance. Mutate its properties, then call <see cref="Save"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Writes the current settings to disk. Failures are swallowed (settings are best-effort).</summary>
    void Save();
}
