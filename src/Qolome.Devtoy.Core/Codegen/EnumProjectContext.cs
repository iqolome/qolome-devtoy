namespace Qolome.Devtoy.Core.Codegen;

/// <summary>枚举生成时的工程上下文（降低 Generate 参数个数）。</summary>
public sealed record EnumProjectContext(
    string ProjectPath,
    string ProjectName,
    string Auth,
    string Version);
