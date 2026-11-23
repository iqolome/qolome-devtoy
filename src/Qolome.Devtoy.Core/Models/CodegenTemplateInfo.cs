namespace Qolome.Devtoy.Core.Models;

/// <summary>代码生成 Velocity 模板（.vm）。</summary>
public sealed class CodegenTemplateInfo
{
    public required string FileName { get; init; }

    public required string FullPath { get; init; }

    public string DisplayName { get; init; } = "";

    public string Category { get; init; } = "其他";

    public bool Exists => File.Exists(FullPath);

    public long LengthBytes => Exists ? new FileInfo(FullPath).Length : 0;

    public DateTime? LastWriteTimeUtc => Exists
        ? File.GetLastWriteTimeUtc(FullPath)
        : null;
}
