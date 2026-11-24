namespace Qolome.Devtoy.Core.Services;

/// <summary>检测 Java 模块是否为 Spring Boot starter（存在 AutoConfiguration.imports）。</summary>
public static class BootStarterDetector
{
    public const string TagName = "boot-starter";

    /// <summary>相对模块根：src/main/resources/META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports</summary>
    public static readonly string RelativeImportsPath = Path.Combine(
        "src",
        "main",
        "resources",
        "META-INF",
        "spring",
        "org.springframework.boot.autoconfigure.AutoConfiguration.imports");

    public static string ResolveImportsPath(string modulePath) =>
        Path.Combine(Path.GetFullPath(modulePath), RelativeImportsPath);

    public static bool HasImportsFile(string modulePath)
    {
        try
        {
            return File.Exists(ResolveImportsPath(modulePath));
        }
        catch
        {
            return false;
        }
    }
}
