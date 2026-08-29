using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public interface IHardwareMonitorService
{
    HardwareSnapshot Current { get; }
}
