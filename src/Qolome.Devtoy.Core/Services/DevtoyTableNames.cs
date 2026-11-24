namespace Qolome.Devtoy.Core.Services;

/// <summary>
/// Devtoy 表名。
/// 远程业务字典为 <c>qolome_dict_*</c>；本机配置为 <c>devtoy_doc</c> / <c>devtoy_workspace</c> / <c>devtoy_module</c>。
/// </summary>
public static class DevtoyTableNames
{
    public const string DictItem = "qolome_dict_item";
    public const string DictVal = "qolome_dict_val";
    public const string Doc = "devtoy_doc";
    public const string Workspace = "devtoy_workspace";
    public const string Module = "devtoy_module";

    public static readonly IReadOnlyList<string> RequiredDictTables = [DictItem, DictVal];

    public static readonly IReadOnlyList<string> LocalConfigTables = [Doc, Workspace, Module];
}
