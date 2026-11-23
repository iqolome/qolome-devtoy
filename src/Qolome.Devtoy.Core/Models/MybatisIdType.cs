namespace Qolome.Devtoy.Core.Models;

/// <summary>对齐 <c>com.baomidou.mybatisplus.annotation.IdType</c>。</summary>
public static class MybatisIdType
{
    public const string Auto = "AUTO";

    public const string None = "NONE";

    public const string Input = "INPUT";

    public const string AssignId = "ASSIGN_ID";

    public const string AssignUuid = "ASSIGN_UUID";

    public static IReadOnlyList<(string Id, string DisplayName)> All { get; } =
    [
        (Auto, "AUTO — 数据库自增"),
        (None, "NONE — 未设置（约等于 INPUT）"),
        (Input, "INPUT — 用户输入"),
        (AssignId, "ASSIGN_ID — 雪花算法"),
        (AssignUuid, "ASSIGN_UUID — UUID"),
    ];

    public static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AssignId;
        }

        var trimmed = value.Trim();
        foreach (var (id, _) in All)
        {
            if (string.Equals(trimmed, id, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return AssignId;
    }
}
