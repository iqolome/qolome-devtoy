namespace Qolome.Nets.Core.Data;

public sealed class OuiEntry
{
    public int Id { get; set; }
    public string Prefix { get; set; } = "";
    public int BitLength { get; set; }
    public string Registry { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string? BatchKey { get; set; }
}

public sealed class OuiMeta
{
    public int Id { get; set; } = 1;
    public string Source { get; set; } = OuiSources.Bundled;
    public DateTime? BundledAtUtc { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public int RecordCount { get; set; }
    public string? Note { get; set; }
    /// <summary>IEEE 无官方批次号；用登记文件 SHA256 作为批次。</summary>
    public string? BatchKey { get; set; }
    public string? PreviousBatchKey { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int RemovedCount { get; set; }
}

public static class OuiSources
{
    public const string Bundled = "bundled";
    public const string Ieee = "ieee";
}
