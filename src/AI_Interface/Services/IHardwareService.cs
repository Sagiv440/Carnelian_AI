using System.Threading;
using System.Threading.Tasks;
using AI_Interface.Models;

namespace AI_Interface.Services;

/// <summary>Best-effort detection of the machine's CPU, RAM and GPU for the Model Config tool.</summary>
public interface IHardwareService
{
    Task<HardwareInfo> ScanAsync(CancellationToken ct = default);
}
