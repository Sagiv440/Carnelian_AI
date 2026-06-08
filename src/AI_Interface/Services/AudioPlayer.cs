using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Interface.Services;

/// <summary>
/// Cross-platform audio playback with zero NuGet dependencies: it shells out to the OS's audio
/// player and waits for it to finish. <see cref="Stop"/> kills the player process.
/// Windows uses <c>System.Media.SoundPlayer</c> via a hidden PowerShell call (WAV); Linux tries
/// <c>paplay</c> → <c>aplay</c> → <c>ffplay</c>; macOS uses <c>afplay</c>.
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    private Process? _current;

    public async Task PlayAsync(string filePath, CancellationToken ct)
    {
        Process? started = null;
        foreach (var (cmd, args) in Candidates(filePath))
        {
            try
            {
                started = Process.Start(new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (started is not null)
                    break;
            }
            catch (Win32Exception)
            {
                // Player not installed on this machine — try the next candidate.
            }
        }

        if (started is null)
            throw new InvalidOperationException("No supported audio player was found on this system.");

        _current = started;
        try
        {
            await started.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(started);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_current, started))
                _current = null;
            started.Dispose();
        }
    }

    public void Stop() => TryKill(_current);

    /// <summary>Player commands to try, in priority order, for the current OS.</summary>
    private static IEnumerable<(string Cmd, string Args)> Candidates(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            // PlaySync blocks until the clip ends; killing PowerShell stops it.
            yield return ("powershell",
                $"-NoProfile -WindowStyle Hidden -Command \"(New-Object System.Media.SoundPlayer '{filePath}').PlaySync()\"");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return ("afplay", Quote(filePath));
        }
        else
        {
            yield return ("paplay", Quote(filePath));
            yield return ("aplay", Quote(filePath));
            yield return ("ffplay", $"-nodisp -autoexit -loglevel quiet {Quote(filePath)}");
        }
    }

    private static string Quote(string path) => $"\"{path}\"";

    private static void TryKill(Process? proc)
    {
        if (proc is null)
            return;
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone / not killable — nothing to do.
        }
    }
}
