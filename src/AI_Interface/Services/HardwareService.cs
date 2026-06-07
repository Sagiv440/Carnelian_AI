using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>
/// Cross-platform, best-effort hardware probe. Everything is wrapped in try/catch and falls back to
/// "unknown" rather than throwing — a failed probe must never crash the Model Config window.
/// GPU VRAM is read from nvidia-smi when present (most reliable); other GPUs fall back to a name only.
/// </summary>
public sealed class HardwareService : IHardwareService
{
    public Task<HardwareInfo> ScanAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var (gpu, vram) = DetectGpu(ct);
            return new HardwareInfo
            {
                CpuName = DetectCpuName(),
                CpuCores = Environment.ProcessorCount,
                TotalRamGb = DetectTotalRamGb(),
                GpuName = gpu,
                VramGb = vram
            };
        }, ct);

    // ---- RAM --------------------------------------------------------------------------------

    private static double DetectTotalRamGb()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref status))
                    return Math.Round(status.ullTotalPhys / 1024d / 1024d / 1024d, 1);
            }
            else
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                        continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                        return Math.Round(kb / 1024d / 1024d, 1);
                }
            }
        }
        catch { /* fall through */ }
        return 0;
    }

    // ---- CPU --------------------------------------------------------------------------------

    private static string DetectCpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                return string.IsNullOrWhiteSpace(id) ? "Unknown CPU" : id.Trim();
            }

            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (!line.StartsWith("model name", StringComparison.Ordinal))
                    continue;
                var idx = line.IndexOf(':');
                if (idx >= 0)
                    return line[(idx + 1)..].Trim();
            }
        }
        catch { /* fall through */ }
        return "Unknown CPU";
    }

    // ---- GPU --------------------------------------------------------------------------------

    private static (string? name, double vramGb) DetectGpu(CancellationToken ct)
    {
        // 1) NVIDIA via nvidia-smi — reliable name + VRAM, works on Windows and Linux.
        var smi = RunProcess("nvidia-smi",
            "--query-gpu=name,memory.total --format=csv,noheader,nounits", ct);
        var line = smi.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is not null)
        {
            var parts = line.Split(',');
            var name = parts[0].Trim();
            if (parts.Length >= 2 &&
                double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var mb))
                return (name, Math.Round(mb / 1024d, 1));
            return (name, 0);
        }

        // 2) Windows fallback: GPU name via CIM (VRAM is not reliably available this way).
        if (OperatingSystem.IsWindows())
        {
            var name = RunProcess("powershell",
                "-NoProfile -Command \"(Get-CimInstance Win32_VideoController | " +
                "Select-Object -First 1 -ExpandProperty Name)\"", ct)
                .Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (!string.IsNullOrWhiteSpace(name))
                return (name, 0);
        }

        return (null, 0);
    }

    /// <summary>Runs a console tool and returns stdout, or "" if it's missing/fails/times out.</summary>
    private static string RunProcess(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!p.Start())
                return "";

            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return "";
            }
            return p.ExitCode == 0 ? output : "";
        }
        catch
        {
            return ""; // tool not installed / not on PATH / blocked
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
