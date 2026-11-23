namespace Qolome.Devtoy.Core.Models;

/// <summary>代码生成页表列表项（表名 + 注释）。</summary>
public sealed class TableListItem
{
    public required string Name { get; init; }

    public string Comment { get; init; } = "";

    public string CommentDisplay => string.IsNullOrWhiteSpace(Comment) ? "—" : Comment.Trim();

    public string DisplayText => string.IsNullOrWhiteSpace(Comment)
        ? Name
        : $"{Name}  ·  {Comment.Trim()}";
}
