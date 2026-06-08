using System;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Downloads and installs the local Ollama runtime, mirroring <see cref="IPiperInstaller"/>: one
/// confirmed click pulls the official installer/script for this OS, runs it, and best-effort starts
/// the server so the configured URL becomes reachable.
/// </summary>
public interface IOllamaInstaller
{
    /// <summary>Best-effort: true when an <c>ollama</c> binary is already present on this machine.</summary>
    bool IsOllamaInstalled { get; }

    /// <summary>
    /// Download the official Ollama installer/script for this OS, run it (Windows: silent Inno Setup;
    /// Linux: the official shell script), then best-effort start the server. Reports progress lines via
    /// <paramref name="progress"/>; throws an <see cref="InvalidOperationException"/> with a clear message
    /// on failure (mirrors <see cref="PiperInstaller"/>).
    /// </summary>
    Task InstallAsync(IProgress<string>? progress, CancellationToken ct);
}
