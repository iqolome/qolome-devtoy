namespace Qolome.Devtoy.Core.Services;

/// <summary>Devtoy 字典与本地文档表名。</summary>
public static class DevtoyTableNames
{
    public const string DictItem = "devtoy_qolome_dict_item";
    public const string DictVal = "devtoy_qolome_dict_val";
    public const string Doc = "devtoy_doc";

    public static readonly IReadOnlyList<string> RequiredDictTables = [DictItem, DictVal];
}