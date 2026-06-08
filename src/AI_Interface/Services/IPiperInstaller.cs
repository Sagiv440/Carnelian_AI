using System;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Downloads and installs the Piper engine into an app-managed folder, and owns the on-disk layout
/// (engine + voices) the rest of the Voice feature uses.
/// </summary>
public interface IPiperInstaller
{
    /// <summary>Folder the engine is extracted into (under the per-user local app-data folder).</summary>
    string EngineDirectory { get; }

    /// <summary>Folder downloaded voice models live in.</summary>
    string VoicesDirectory { get; }

    /// <summary>Path to the installed <c>piper</c>(.exe), or null if it isn't installed yet.</summary>
    string? ResolvedExecutablePath { get; }

    /// <summary>True when a Piper executable is present in <see cref="EngineDirectory"/>.</summary>
    bool IsEngineInstalled { get; }

    /// <summary>
    /// Download the correct Piper release for this OS/arch, extract it, persist the resolved
    /// executable path to settings, and return that path.
    /// </summary>
    Task<string> InstallEngineAsync(IProgress<string>? progress, CancellationToken ct);
}
