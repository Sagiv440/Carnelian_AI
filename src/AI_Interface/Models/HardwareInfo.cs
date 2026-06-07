namespace AI_Interface.Models;

/// <summary>A best-effort snapshot of the machine's compute resources, used to rank models by fit.</summary>
public sealed class HardwareInfo
{
    public string CpuName { get; init; } = "Unknown CPU";
    public int CpuCores { get; init; }
    public double TotalRamGb { get; init; }

    /// <summary>Detected GPU name, or null when none was found.</summary>
    public string? GpuName { get; init; }

    /// <summary>Detected dedicated VRAM in GB (0 when unknown).</summary>
    public double VramGb { get; init; }

    public bool HasGpu => !string.IsNullOrEmpty(GpuName);

    /// <summary>
    /// Memory budget for model weights: GPU VRAM when known (GPU inference), otherwise system RAM
    /// (CPU inference). A model is considered to "fit" when its estimated footprint is within this.
    /// </summary>
    public double BudgetGb => VramGb > 0 ? VramGb : TotalRamGb;

    public string Summary
    {
        get
        {
            var gpu = string.IsNullOrEmpty(GpuName)
                ? "GPU: none detected — using CPU + system RAM"
                : VramGb > 0
                    ? $"GPU: {GpuName} · {VramGb:0.#} GB VRAM"
                    : $"GPU: {GpuName} · VRAM unknown";
            return $"CPU: {CpuName}  ({CpuCores} logical cores)\n" +
                   $"RAM: {TotalRamGb:0.#} GB\n" +
                   $"{gpu}\n" +
                   $"Model memory budget: ~{BudgetGb:0.#} GB";
        }
    }
}
