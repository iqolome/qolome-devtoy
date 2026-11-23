namespace Qolome.Devtoy.Core.Models;

/// <summary>Java 代码生成页使用习惯（跨会话恢复）。</summary>
public sealed class CodegenHabitSettings
{
    public string? LastModulePath { get; set; }

    public string? LastModelName { get; set; }

    public bool Dao { get; set; } = true;

    public bool Service { get; set; }

    public bool Controller { get; set; }
}
