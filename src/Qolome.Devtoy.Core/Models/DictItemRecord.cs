namespace Qolome.Devtoy.Core.Models;

/// <summary>qolome_dict_item 行，含维护字段。</summary>
public sealed class DictItemRecord
{
    public long ItemId { get; set; }

    public string ItemCode { get; set; } = "";

    public string ItemName { get; set; } = "";

    public int? ItemScope { get; set; }

    public string? ItemDesc { get; set; }

    public bool EnumFlag { get; set; } = true;

    public int? DataReferenceType { get; set; } = 12;

    public string? Remark { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime? CreateTime { get; set; }

    public DateTime? ModifyTime { get; set; }

    public string CreateTimeDisplay => FormatTimestamp(CreateTime);

    public string ModifyTimeDisplay => FormatTimestamp(ModifyTime);

    public string ReferenceCode =>
        DataReferenceType is null
            ? ""
            : DataReferenceTypeHelper.ResolveReferenceCode(DataReferenceType.Value);

    public string EnumDisplay => EnumFlag ? "是" : "否";

    public string EnabledDisplay => Enabled ? "是" : "否";

    private static string FormatTimestamp(DateTime? value) =>
        value.HasValue
            ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "—";
}