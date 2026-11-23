namespace Qolome.Devtoy.Core.Models;

/// <summary>qolome_dict_val 行。</summary>
public sealed class DictValueRow
{
    public long ValId { get; set; }

    public long ItemId { get; set; }

    public string ValValue { get; set; } = "";

    public string ValCode { get; set; } = "";

    public string ValName { get; set; } = "";

    public string? ValEnum { get; set; }

    public int? ValScope { get; set; }

    public string? ValDesc { get; set; }

    public int Seq { get; set; }

    public string? Remark { get; set; }

    public bool Enabled { get; set; } = true;

    public string EnabledDisplay => Enabled ? "是" : "否";
}