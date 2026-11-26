using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Qolome.Nets.Core.Scanning;

/// <summary>通过 WMI / 系统 API 采集本机硬件摘要。</summary>
public static class LocalHardwareInventory
{
    public static LocalHardwareInfo GetInfo(bool parallel = true)
    {
        try
        {
            if (!parallel)
            {
                return Build(
                    ReadComputerSystem(),
                    ReadProcessors(),
                    ReadMemoryModules(),
                    ReadOperatingSystem(),
                    ReadDisks(),
                    ReadGpus());
            }

            var csTask = Task.Run(ReadComputerSystem);
            var cpuTask = Task.Run(ReadProcessors);
            var memTask = Task.Run(ReadMemoryModules);
            var osTask = Task.Run(ReadOperatingSystem);
            var diskTask = Task.Run(ReadDisks);
            var gpuTask = Task.Run(ReadGpus);
            Task.WaitAll(csTask, cpuTask, memTask, osTask, diskTask, gpuTask);
            return Build(
                csTask.Result,
                cpuTask.Result,
                memTask.Result,
                osTask.Result,
                diskTask.Result,
                gpuTask.Result);
        }
        catch (Exception ex)
        {
            return LocalHardwareInfo.Failed(ex.Message);
        }
    }

    public static Task<LocalHardwareInfo> GetInfoAsync(
        bool parallel = true,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetInfo(parallel);
        }, cancellationToken);

    private static LocalHardwareInfo Build(
        ComputerSystemInfo cs,
        IReadOnlyList<ProcessorInfo> cpus,
        IReadOnlyList<MemoryModuleInfo> modules,
        OsInfo os,
        IReadOnlyList<LocalDiskInfo> disks,
        IReadOnlyList<LocalGpuInfo> gpus)
    {
        var deviceName = FirstNonEmpty(cs.Name, Environment.MachineName) ?? "—";
        var deviceModel = FormatDeviceModel(cs.Manufacturer, cs.Model);

        return new LocalHardwareInfo
        {
            DeviceName = deviceName,
            DeviceModel = deviceModel,
            ProcessorDisplay = FormatProcessors(cpus),
            MemoryDisplay = FormatMemory(modules, cs.TotalPhysicalMemoryBytes),
            OsDisplay = FormatOs(os),
            DiskLines = disks.Select((d, i) => d.ToLine(i + 1)).ToList(),
            GpuLines = gpus.Select((g, i) => g.ToLine(i + 1)).ToList(),
        };
    }

    private static string FormatDeviceModel(string? manufacturer, string? model)
    {
        var mfr = Clean(manufacturer);
        var mdl = Clean(model);
        if (string.IsNullOrEmpty(mfr) && string.IsNullOrEmpty(mdl))
        {
            return "—";
        }

        if (string.IsNullOrEmpty(mfr))
        {
            return mdl!;
        }

        if (string.IsNullOrEmpty(mdl) || mdl!.Contains(mfr, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(mdl) ? mfr : mdl;
        }

        return $"{mfr} {mdl}";
    }

    private static string FormatProcessors(IReadOnlyList<ProcessorInfo> cpus)
    {
        if (cpus.Count == 0)
        {
            return "—";
        }

        var (eCores, pCores) = TryCountEfficiencyAndPerformanceCores();
        var parts = new List<string>();
        foreach (var cpu in cpus)
        {
            var name = Clean(cpu.Name) ?? "处理器";
            name = Regex.Replace(name, @"\s+", " ").Trim();

            string corePart;
            if (eCores + pCores > 0 && cpus.Count == 1)
            {
                corePart = $"{eCores}E,{pCores}P";
            }
            else
            {
                // 回退：物理核 / 逻辑处理器
                corePart = $"{cpu.Cores}C,{cpu.LogicalProcessors}T";
            }

            var baseGhz = FormatGhz(cpu.BaseClockMhz);
            var maxGhz = FormatGhz(cpu.MaxClockMhz > cpu.BaseClockMhz ? cpu.MaxClockMhz : cpu.BaseClockMhz);
            var freq = baseGhz == maxGhz ? baseGhz : $"{baseGhz}~{maxGhz}";
            parts.Add($"{name} {corePart} {freq}");
        }

        return string.Join(" / ", parts);
    }

    private static string FormatMemory(IReadOnlyList<MemoryModuleInfo> modules, ulong totalBytesFallback)
    {
        var totalBytes = modules.Count > 0
            ? (ulong)modules.Sum(m => (long)m.CapacityBytes)
            : totalBytesFallback;
        var totalGb = totalBytes > 0 ? Math.Round(totalBytes / (1024d * 1024d * 1024d), 0) : 0;

        if (modules.Count == 0)
        {
            return totalGb > 0 ? $"共 {totalGb:0}GB" : "—";
        }

        var groups = modules
            .GroupBy(m => $"{m.DisplayName}|{m.SpeedMhz}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sample = g.First();
                var name = sample.DisplayName;
                var speed = sample.SpeedMhz > 0 ? $"{sample.SpeedMhz}MHz" : "";
                return string.IsNullOrEmpty(speed)
                    ? $"{name}*{g.Count()}"
                    : $"{name}*{g.Count()} {speed}";
            })
            .ToList();

        var detail = string.Join(" + ", groups);
        return totalGb > 0 ? $"{detail} 共 {totalGb:0}GB" : detail;
    }

    private static string FormatOs(OsInfo os)
    {
        var caption = Clean(os.Caption) ?? "Windows";
        var version = Clean(os.Version);
        if (!string.IsNullOrEmpty(os.BuildNumber) && version is not null && !version.Contains(os.BuildNumber))
        {
            version = $"{version}.{os.BuildNumber}";
        }

        return string.IsNullOrEmpty(version) ? caption : $"{caption} {version}";
    }

    private static string FormatGhz(uint mhz) =>
        mhz == 0 ? "—" : $"{mhz / 1000.0:0.00}GHz";

    private static ComputerSystemInfo ReadComputerSystem()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    return new ComputerSystemInfo(
                        obj["Name"]?.ToString(),
                        obj["Manufacturer"]?.ToString(),
                        obj["Model"]?.ToString(),
                        ToUInt64(obj["TotalPhysicalMemory"]));
                }
            }
        }
        catch
        {
            // ignore
        }

        return new ComputerSystemInfo(Environment.MachineName, null, null, 0);
    }

    private static List<ProcessorInfo> ReadProcessors()
    {
        var list = new List<ProcessorInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    var max = ToUInt32(obj["MaxClockSpeed"]);
                    var current = ToUInt32(obj["CurrentClockSpeed"]);
                    // WMI MaxClockSpeed 多为标称基频；若当前更高则作睿频近似上界
                    var baseMhz = max;
                    var turboMhz = current > max ? current : max;
                    list.Add(new ProcessorInfo(
                        obj["Name"]?.ToString(),
                        ToUInt32(obj["NumberOfCores"]),
                        ToUInt32(obj["NumberOfLogicalProcessors"]),
                        baseMhz,
                        turboMhz));
                }
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    private static List<MemoryModuleInfo> ReadMemoryModules()
    {
        var list = new List<MemoryModuleInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, PartNumber, Speed, ConfiguredClockSpeed, Capacity FROM Win32_PhysicalMemory");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    var mfr = Clean(obj["Manufacturer"]?.ToString()) ?? "";
                    var part = Clean(obj["PartNumber"]?.ToString()) ?? "";
                    var name = string.IsNullOrEmpty(part)
                        ? (string.IsNullOrEmpty(mfr) ? "内存" : mfr)
                        : (string.IsNullOrEmpty(mfr) || part.Contains(mfr, StringComparison.OrdinalIgnoreCase)
                            ? part
                            : $"{mfr} {part}");
                    var speed = ToUInt32(obj["ConfiguredClockSpeed"]);
                    if (speed == 0)
                    {
                        speed = ToUInt32(obj["Speed"]);
                    }

                    list.Add(new MemoryModuleInfo(name, speed, ToUInt64(obj["Capacity"])));
                }
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    private static OsInfo ReadOperatingSystem()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    return new OsInfo(
                        obj["Caption"]?.ToString(),
                        obj["Version"]?.ToString(),
                        obj["BuildNumber"]?.ToString());
                }
            }
        }
        catch
        {
            // ignore
        }

        return new OsInfo(
            RuntimeInformation.OSDescription,
            Environment.OSVersion.Version.ToString(),
            null);
    }

    private static List<LocalDiskInfo> ReadDisks()
    {
        var fromStorage = TryReadMsftPhysicalDisks();
        if (fromStorage.Count > 0)
        {
            return fromStorage;
        }

        return ReadWin32DiskDrives();
    }

    private static List<LocalDiskInfo> TryReadMsftPhysicalDisks()
    {
        var list = new List<LocalDiskInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, MediaType, BusType, Size, SpindleSpeed, DeviceId FROM MSFT_PhysicalDisk");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    var name = Clean(obj["FriendlyName"]?.ToString()) ?? "磁盘";
                    var media = ToUInt16(obj["MediaType"]);
                    var bus = ToUInt16(obj["BusType"]);
                    var spindle = ToUInt32(obj["SpindleSpeed"]);
                    var size = ToUInt64(obj["Size"]);
                    var type = ClassifyDiskType(bus, media, spindle, name);
                    list.Add(new LocalDiskInfo
                    {
                        Name = name,
                        TypeLabel = type,
                        CapacityDisplay = FormatBytesAsCapacity(size),
                    });
                }
            }
        }
        catch
        {
            // Storage namespace may require elevation / unavailable
        }

        return list;
    }

    private static List<LocalDiskInfo> ReadWin32DiskDrives()
    {
        var list = new List<LocalDiskInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, InterfaceType, Size, MediaType, PNPDeviceID FROM Win32_DiskDrive");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    var name = Clean(obj["Model"]?.ToString()) ?? "磁盘";
                    var iface = obj["InterfaceType"]?.ToString() ?? "";
                    var media = obj["MediaType"]?.ToString() ?? "";
                    var pnp = obj["PNPDeviceID"]?.ToString() ?? "";
                    var type = ClassifyDiskTypeFromLegacy(iface, media, name, pnp);
                    list.Add(new LocalDiskInfo
                    {
                        Name = name,
                        TypeLabel = type,
                        CapacityDisplay = FormatBytesAsCapacity(ToUInt64(obj["Size"])),
                    });
                }
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    /// <summary>
    /// BusType: 11=SATA, 17=NVMe, 8=RAID, 7=USB, 9=SCSI …
    /// MediaType: 3=HDD, 4=SSD, 5=SCM
    /// </summary>
    private static string ClassifyDiskType(ushort bus, ushort media, uint spindle, string name)
    {
        var isHdd = media == 3 || (media == 0 && spindle > 0);
        var isSsd = media == 4 || (media == 0 && spindle == 0 && !isHdd);
        var hint = name + " ";

        var pcieGen = DetectPcieGen(hint);

        return bus switch
        {
            17 => // NVMe
                pcieGen is not null ? $"M.2-PCIE_{pcieGen}" : "M.2-PCIE",
            11 => // SATA
                isHdd ? "SATA3-机械" : "SATA3-固态",
            8 => isHdd ? "RAID-机械" : "RAID-固态",
            7 => isHdd ? "USB-机械" : "USB-固态",
            9 or 10 => isHdd ? "SCSI-机械" : "SCSI-固态",
            13 => // SD
                "SD",
            14 => // MMC
                "MMC",
            15 => // Virtual
                "虚拟磁盘",
            16 => // File Backed Virtual
                "虚拟磁盘",
            18 => // SCM
                "SCM",
            19 => // UFS
                "UFS",
            _ when hint.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                => pcieGen is not null ? $"M.2-PCIE_{pcieGen}" : "M.2-PCIE",
            _ when hint.Contains("M.2", StringComparison.OrdinalIgnoreCase)
                  && !hint.Contains("SATA", StringComparison.OrdinalIgnoreCase)
                => pcieGen is not null ? $"M.2-PCIE_{pcieGen}" : "M.2-PCIE",
            _ when ifaceLooksSata(hint) || bus == 11
                => isHdd ? "SATA3-机械" : "SATA3-固态",
            _ => isHdd ? "机械" : isSsd ? "固态" : "未知"
        };

        static bool ifaceLooksSata(string text) =>
            text.Contains("SATA", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyDiskTypeFromLegacy(string iface, string media, string name, string pnp)
    {
        var blob = $"{iface} {media} {name} {pnp}";
        var isHdd = media.Contains("Fixed hard disk", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("HDD", StringComparison.OrdinalIgnoreCase);
        var isSsd = blob.Contains("SSD", StringComparison.OrdinalIgnoreCase)
            || pnp.Contains("NVME", StringComparison.OrdinalIgnoreCase);
        if (pnp.Contains("NVME", StringComparison.OrdinalIgnoreCase)
            || iface.Contains("SCSI", StringComparison.OrdinalIgnoreCase)
               && name.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
        {
            var gen = DetectPcieGen(blob);
            return gen is not null ? $"M.2-PCIE_{gen}" : "M.2-PCIE";
        }

        if (iface.Contains("IDE", StringComparison.OrdinalIgnoreCase)
            || iface.Contains("SATA", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("SATA", StringComparison.OrdinalIgnoreCase))
        {
            return isHdd && !isSsd ? "SATA3-机械" : "SATA3-固态";
        }

        if (iface.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return isHdd && !isSsd ? "USB-机械" : "USB-固态";
        }

        return isHdd && !isSsd ? "机械" : isSsd ? "固态" : (Clean(iface) ?? "未知");
    }

    private static string? DetectPcieGen(string text)
    {
        var m = Regex.Match(text, @"Gen\s*([345])|PCI[eE]\s*([345])(?:\.0)?|PCIE_([345])\.0", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return null;
        }

        for (var i = 1; i < m.Groups.Count; i++)
        {
            if (m.Groups[i].Success)
            {
                return $"{m.Groups[i].Value}.0";
            }
        }

        return null;
    }

    private static List<LocalGpuInfo> ReadGpus()
    {
        var list = new List<LocalGpuInfo>();
        var registryVrams = ReadRegistryDedicatedVrams();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    var name = Clean(obj["Name"]?.ToString());
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    // 跳过远程桌面 / 基本显示适配器等
                    if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("虚拟", StringComparison.OrdinalIgnoreCase)
                           && name.Contains("显示", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pnp = Clean(obj["PNPDeviceID"]?.ToString());
                    // AdapterRAM 为 UInt32，>4GB 会截断；优先注册表 qwMemorySize（QWORD）
                    var bytes = ResolveDedicatedVramBytes(pnp, name, ToUInt64(obj["AdapterRAM"]), registryVrams);
                    var label = IsLikelyIntegratedGpu(name) ? "共享显存" : "显存";
                    var vram = bytes is null or 0
                        ? $"{label} —"
                        : $"{label} {FormatBytesAsCapacity(bytes.Value)}";

                    // WMI 无稳定 GPU 核心/显存频率字段
                    list.Add(new LocalGpuInfo
                    {
                        Name = name,
                        VramDisplay = vram,
                        ClockDisplay = "频率 —",
                    });
                }
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    /// <summary>
    /// 显示适配器类注册表中的专用显存（HardwareInformation.qwMemorySize），避免 WMI AdapterRAM 32 位截断。
    /// </summary>
    private static List<(string MatchingDeviceId, string DriverDesc, ulong Bytes)> ReadRegistryDedicatedVrams()
    {
        var result = new List<(string, string, ulong)>();
        const string classPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(classPath);
            if (root is null)
            {
                return result;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                if (name.Length != 4 || !char.IsDigit(name[0]))
                {
                    continue;
                }

                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is null)
                    {
                        continue;
                    }

                    var bytes = ReadQwMemorySize(key.GetValue("HardwareInformation.qwMemorySize"));
                    if (bytes == 0)
                    {
                        continue;
                    }

                    var matchingId = Clean(key.GetValue("MatchingDeviceId")?.ToString()) ?? "";
                    var driverDesc = Clean(key.GetValue("DriverDesc")?.ToString()) ?? "";
                    result.Add((matchingId, driverDesc, bytes));
                }
                catch
                {
                    // ignore single key
                }
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    private static ulong? ResolveDedicatedVramBytes(
        string? pnpDeviceId,
        string name,
        ulong adapterRam,
        List<(string MatchingDeviceId, string DriverDesc, ulong Bytes)> registryVrams)
    {
        foreach (var (matchingId, driverDesc, bytes) in registryVrams)
        {
            if (!string.IsNullOrEmpty(matchingId)
                && !string.IsNullOrEmpty(pnpDeviceId)
                && pnpDeviceId.StartsWith(matchingId, StringComparison.OrdinalIgnoreCase))
            {
                return bytes;
            }

            if (!string.IsNullOrEmpty(driverDesc)
                && driverDesc.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return bytes;
            }
        }

        // 回退：AdapterRAM 在 0 / UInt32.MaxValue 时不可信
        if (adapterRam == 0 || adapterRam >= uint.MaxValue)
        {
            return null;
        }

        return adapterRam;
    }

    private static ulong ReadQwMemorySize(object? value) =>
        value switch
        {
            long l when l > 0 => (ulong)l,
            ulong ul => ul,
            int i when i > 0 => (uint)i,
            uint u => u,
            byte[] bin when bin.Length >= 8 => BitConverter.ToUInt64(bin, 0),
            byte[] bin when bin.Length >= 4 => BitConverter.ToUInt32(bin, 0),
            _ => 0
        };

    private static bool IsLikelyIntegratedGpu(string name)
    {
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            && (name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                || name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Graphics", StringComparison.OrdinalIgnoreCase)
                   && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // AMD 核显常见命名（无 RX / Radeon Pro 等独显标识时）
        return name.Contains("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>通过 GetLogicalProcessorInformationEx 统计能效核 / 性能核（EfficiencyClass）。</summary>
    private static (int ECores, int PCores) TryCountEfficiencyAndPerformanceCores()
    {
        try
        {
            uint len = 0;
            GetLogicalProcessorInformationEx(RELATION_PROCESSOR_CORE, IntPtr.Zero, ref len);
            if (len == 0)
            {
                return (0, 0);
            }

            var buffer = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformationEx(RELATION_PROCESSOR_CORE, buffer, ref len))
                {
                    return (0, 0);
                }

                var e = 0;
                var p = 0;
                var offset = 0;
                while (offset < len)
                {
                    var rel = Marshal.ReadInt32(buffer, offset); // Relationship
                    var size = Marshal.ReadInt32(buffer, offset + 4);
                    if (size <= 0)
                    {
                        break;
                    }

                    if (rel == RELATION_PROCESSOR_CORE)
                    {
                        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX.Processor.Flags / EfficiencyClass
                        // After header (8 bytes): PROCESSOR_RELATIONSHIP
                        // Flags at +0, EfficiencyClass at +1, then padding, GroupCount, GroupMask...
                        var efficiencyClass = Marshal.ReadByte(buffer, offset + 8 + 1);
                        if (efficiencyClass == 0)
                        {
                            p++;
                        }
                        else
                        {
                            e++;
                        }
                    }

                    offset += size;
                }

                return (e, p);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return (0, 0);
        }
    }

    private const int RELATION_PROCESSOR_CORE = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    private static string FormatBytesAsCapacity(ulong bytes)
    {
        if (bytes == 0)
        {
            return "—";
        }

        var gb = bytes / (1024d * 1024d * 1024d);
        if (gb >= 10)
        {
            return $"{Math.Round(gb):0}GB";
        }

        if (gb >= 1)
        {
            return $"{gb:0.#}GB";
        }

        var mb = bytes / (1024d * 1024d);
        return $"{Math.Round(mb):0}MB";
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Trim('\0');
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static uint ToUInt32(object? value) =>
        value switch
        {
            null => 0,
            uint u => u,
            int i when i >= 0 => (uint)i,
            ushort us => us,
            short s when s >= 0 => (uint)s,
            long l when l >= 0 && l <= uint.MaxValue => (uint)l,
            ulong ul when ul <= uint.MaxValue => (uint)ul,
            string str when uint.TryParse(str, out var p) => p,
            _ => 0
        };

    private static ushort ToUInt16(object? value) =>
        value switch
        {
            null => 0,
            ushort us => us,
            short s when s >= 0 => (ushort)s,
            int i when i >= 0 && i <= ushort.MaxValue => (ushort)i,
            uint u when u <= ushort.MaxValue => (ushort)u,
            string str when ushort.TryParse(str, out var p) => p,
            _ => 0
        };

    private static ulong ToUInt64(object? value) =>
        value switch
        {
            null => 0,
            ulong ul => ul,
            long l when l >= 0 => (ulong)l,
            uint u => u,
            int i when i >= 0 => (uint)i,
            string str when ulong.TryParse(str, out var p) => p,
            _ => 0
        };

    private sealed record ComputerSystemInfo(string? Name, string? Manufacturer, string? Model, ulong TotalPhysicalMemoryBytes);

    private sealed record ProcessorInfo(
        string? Name,
        uint Cores,
        uint LogicalProcessors,
        uint BaseClockMhz,
        uint MaxClockMhz);

    private sealed record MemoryModuleInfo(string DisplayName, uint SpeedMhz, ulong CapacityBytes);

    private sealed record OsInfo(string? Caption, string? Version, string? BuildNumber);
}
