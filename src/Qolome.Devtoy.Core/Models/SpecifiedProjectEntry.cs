namespace Qolome.Devtoy.Core.Models;

/// <summary>用户添加的指定工程（目录即为项目根）。</summary>
public sealed class SpecifiedProjectEntry
{
    public string Root { get; set; } = "";

    public bool Java { get; set; }

    public bool Web { get; set; }

    /// <summary>已废弃，反序列化时合并入 <see cref="Web"/>。</summary>
    public bool JavaScript { get; set; }

    public bool Flutter { get; set; }

    public string? Version { get; set; }

    /// <summary>无法自动识别时由用户手工勾选类型/版本。</summary>
    public bool Manual { get; set; }
}