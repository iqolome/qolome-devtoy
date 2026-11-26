namespace Qolome.Nets.Core.Scanning;

/// <summary>本机硬件摘要（设备 / CPU / 内存 / 系统 / 硬盘 / 显卡）。</summary>
public sealed class LocalHardwareInfo
{
    public string DeviceName { get; init; } = "—";
    public string DeviceModel { get; init; } = "—";
    public string ProcessorDisplay { get; init; } = "—";
    public string MemoryDisplay { get; init; } = "—";
    public string OsDisplay { get; init; } = "—";
    public IReadOnlyList<string> DiskLines { get; init; } = [];
    public IReadOnlyList<string> GpuLines { get; init; } = [];
    public string? Error { get; init; }

    public static LocalHardwareInfo Failed(string message) => new() { Error = message };
}

public sealed class LocalDiskInfo
{
    public required string Name { get; init; }
    public required string TypeLabel { get; init; }
    public required string CapacityDisplay { get; init; }

    public string ToLine(int index) =>
        $"Disk{index}: {Name} {TypeLabel} {CapacityDisplay}";
}

public sealed class LocalGpuInfo
{
    public required string Name { get; init; }
    public required string VramDisplay { get; init; }
    public required string ClockDisplay { get; init; }

    public string ToLine(int index) =>
        $"GPU{index}: {Name} {VramDisplay} {ClockDisplay}";
}
