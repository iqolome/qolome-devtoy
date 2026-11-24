using System.Text.Json;
using Qolome.Nets.Core.Data;

namespace Qolome.Nets.Core.Scanning;

public sealed class ScanOptions
{
    public int PortScanConcurrency { get; set; }
    public int IpScanBatchSize { get; set; } = 50;
    public int IpScanConcurrency { get; set; } = 3;
    public int PingCount { get; set; } = 4;

    public static int DefaultPortScanConcurrency =>
        Math.Clamp(Math.Max(1, Environment.ProcessorCount) * 3, 3, 256);

    public static ScanOptions Defaults() => new()
    {
        PortScanConcurrency = DefaultPortScanConcurrency,
        IpScanBatchSize = 50,
        IpScanConcurrency = 3,
        PingCount = 4
    };

    public void Clamp()
    {
        if (PortScanConcurrency <= 0)
        {
            PortScanConcurrency = DefaultPortScanConcurrency;
        }

        PortScanConcurrency = Math.Clamp(PortScanConcurrency, 1, 256);
        IpScanBatchSize = Math.Clamp(IpScanBatchSize <= 0 ? 50 : IpScanBatchSize, 1, 4096);
        IpScanConcurrency = Math.Clamp(IpScanConcurrency <= 0 ? 3 : IpScanConcurrency, 1, 64);
        PingCount = Math.Clamp(PingCount <= 0 ? 4 : PingCount, 1, 100);
    }
}

public static class ScanSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath
    {
        get
        {
            var dir = AppDatabase.DataDirectory;
            return Path.Combine(string.IsNullOrWhiteSpace(dir) ? "." : dir, "scan-settings.json");
        }
    }

    public static ScanOptions Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<ScanOptions>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Clamp();
                    return loaded;
                }
            }
        }
        catch
        {
            // 损坏或无法读取时回退默认值
        }

        var defaults = ScanOptions.Defaults();
        defaults.Clamp();
        return defaults;
    }

    public static void Save(ScanOptions options)
    {
        options.Clamp();
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(options, JsonOptions));
    }
}
