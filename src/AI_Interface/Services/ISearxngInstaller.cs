using System;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Installs and removes a local <b>SearXNG</b> metasearch instance via Docker, mirroring
/// <see cref="IOllamaInstaller"/>. One confirmed click pulls the official image, writes a minimal config
/// that enables the JSON API the app needs, and starts a container on a fixed local port; Remove stops the
/// container and deletes the image + config. Docker is the prerequisite (SearXNG's recommended local setup).
/// </summary>
public interface ISearxngInstaller
{
    /// <summary>The local URL the running container is served on (e.g. <c>http://localhost:8888</c>).</summary>
    string LocalUrl { get; }

    /// <summary>Best-effort: true when the managed SearXNG container is currently running.</summary>
    Task<bool> IsRunningAsync(CancellationToken ct = default);

    /// <summary>
    /// Pull the SearXNG image (first run), write the JSON-enabled config, and (re)start the container.
    /// Reports progress lines; throws <see cref="InvalidOperationException"/> with a clear message when
    /// Docker is missing or the container fails to start.
    /// </summary>
    Task InstallAsync(IProgress<string>? progress, CancellationToken ct);

    /// <summary>Stop and remove the container, drop the image, and delete the generated config.</summary>
    Task RemoveAsync(IProgress<string>? progress, CancellationToken ct);
}
