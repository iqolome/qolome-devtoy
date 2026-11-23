namespace Qolome.Devtoy.Core.Models;

using System.Text.RegularExpressions;
using System.Xml.Linq;

public sealed partial class ProjectTypes
{
    public bool Java { get; set; }

    public bool Web { get; set; }

    public bool Flutter { get; set; }

    /// <summary>字典/TS 代码生成是否可用。</summary>
    public bool SupportsTypeScript => Web;

    /// <summary>后端：Maven / Gradle 工程标记。</summary>
    public static bool IsBackendModule(string moduleDir) =>
        File.Exists(Path.Combine(moduleDir, "pom.xml"))
        || File.Exists(Path.Combine(moduleDir, "build.gradle"))
        || File.Exists(Path.Combine(moduleDir, "build.gradle.kts"));

    /// <summary>
    /// 是否应登记到模块列表：忽略 Maven packaging=pom 的聚合/BOM，
    /// Gradle 忽略无源码的聚合工程；子模块仍继续扫描。
    /// </summary>
    public static bool ShouldRegisterBackendModule(string moduleDir)
    {
        if (!IsBackendModule(moduleDir))
        {
            return false;
        }

        var pomPath = Path.Combine(moduleDir, "pom.xml");
        if (File.Exists(pomPath))
        {
            return !IsMavenPomPackaging(pomPath);
        }

        return IsCodeBearingGradleProject(moduleDir);
    }

    /// <summary>含 package.json 的 Web / JavaScript 工程（统一为 Web）。</summary>
    public static bool IsNodeModule(string moduleDir) =>
        File.Exists(Path.Combine(moduleDir, "package.json"));

    /// <summary>Flutter 工程（pubspec.yaml 且声明 Flutter SDK）。</summary>
    public static bool IsFlutterModule(string moduleDir) =>
        File.Exists(Path.Combine(moduleDir, "pubspec.yaml")) && LooksLikeFlutterPubspec(moduleDir);

    public static bool IsFrontendModule(string moduleDir) =>
        IsNodeModule(moduleDir) || IsFlutterModule(moduleDir);

    public static ProjectTypes Infer(string moduleDir)
    {
        if (!Directory.Exists(moduleDir))
        {
            return new ProjectTypes();
        }

        var backend = IsBackendModule(moduleDir);
        return new ProjectTypes
        {
            // 聚合 pom / 无源码 Gradle 不标为 Java 模块，避免出现在代码生成目标中
            Java = backend && ShouldRegisterBackendModule(moduleDir),
            Web = IsNodeModule(moduleDir),
            Flutter = LooksLikeFlutterPubspec(moduleDir),
        };
    }

    /// <summary>Maven packaging 是否为 pom（聚合父工程 / BOM）。</summary>
    public static bool IsMavenPomPackaging(string pomPath)
    {
        try
        {
            var text = File.ReadAllText(pomPath);
            // 去掉 parent 块，避免误读；再取 project 自身 packaging
            text = ParentBlockRegex().Replace(text, string.Empty);
            text = XmlCommentRegex().Replace(text, string.Empty);
            var match = PackagingRegex().Match(text);
            if (!match.Success)
            {
                return false;
            }

            return string.Equals(match.Groups[1].Value.Trim(), "pom", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            try
            {
                var doc = XDocument.Load(pomPath);
                XNamespace? ns = doc.Root?.Name.Namespace;
                var packaging = ns is null || ns == XNamespace.None
                    ? doc.Root?.Element("packaging")?.Value
                    : doc.Root?.Element(ns + "packaging")?.Value;
                return string.Equals(packaging?.Trim(), "pom", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Gradle：具备 JVM/Android 源码或明确应用了代码插件，才视为有代码工程。</summary>
    public static bool IsCodeBearingGradleProject(string moduleDir)
    {
        if (HasJvmOrAndroidSources(moduleDir))
        {
            return true;
        }

        foreach (var name in new[] { "build.gradle", "build.gradle.kts" })
        {
            var path = Path.Combine(moduleDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(path);
                if (LooksLikeCodeBearingGradleBuild(text))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool HasJvmOrAndroidSources(string moduleDir)
    {
        var sourceRoots = new[]
        {
            Path.Combine(moduleDir, "src", "main", "java"),
            Path.Combine(moduleDir, "src", "main", "kotlin"),
            Path.Combine(moduleDir, "src", "main", "groovy"),
            Path.Combine(moduleDir, "src", "main", "scala"),
            Path.Combine(moduleDir, "src", "main", "cpp"),
            Path.Combine(moduleDir, "src", "main", "jni"),
        };
        foreach (var root in sourceRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                if (Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Any())
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return File.Exists(Path.Combine(moduleDir, "src", "main", "AndroidManifest.xml"))
               || File.Exists(Path.Combine(moduleDir, "AndroidManifest.xml"));
    }

    private static bool LooksLikeCodeBearingGradleBuild(string text)
    {
        // 避免匹配 java-platform 等 BOM/聚合插件
        return ContainsPluginId(text, "java")
               || ContainsPluginId(text, "java-library")
               || ContainsPluginId(text, "application")
               || text.Contains("org.jetbrains.kotlin", StringComparison.OrdinalIgnoreCase)
               || text.Contains("kotlin(\"jvm\")", StringComparison.OrdinalIgnoreCase)
               || text.Contains("kotlin('jvm')", StringComparison.OrdinalIgnoreCase)
               || text.Contains("kotlin(\"android\")", StringComparison.OrdinalIgnoreCase)
               || text.Contains("kotlin('android')", StringComparison.OrdinalIgnoreCase)
               || text.Contains("com.android.application", StringComparison.OrdinalIgnoreCase)
               || text.Contains("com.android.library", StringComparison.OrdinalIgnoreCase)
               || text.Contains("org.springframework.boot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPluginId(string text, string pluginId)
    {
        // id("java") / id 'java' / apply plugin: 'java'
        return text.Contains($"id(\"{pluginId}\")", StringComparison.OrdinalIgnoreCase)
               || text.Contains($"id('{pluginId}')", StringComparison.OrdinalIgnoreCase)
               || text.Contains($"id \"{pluginId}\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains($"id '{pluginId}'", StringComparison.OrdinalIgnoreCase)
               || text.Contains($"apply plugin: \"{pluginId}\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains($"apply plugin: '{pluginId}'", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<parent\b[^>]*>[\s\S]*?</parent>", RegexOptions.IgnoreCase)]
    private static partial Regex ParentBlockRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex XmlCommentRegex();

    [GeneratedRegex(@"<packaging>\s*([^<\s]+)\s*</packaging>", RegexOptions.IgnoreCase)]
    private static partial Regex PackagingRegex();

    public override string ToString()
    {
        var parts = new List<string>(3);
        if (Java)
        {
            parts.Add("Java");
        }

        if (Web)
        {
            parts.Add("Web");
        }

        if (Flutter)
        {
            parts.Add("Flutter");
        }

        return parts.Count > 0 ? string.Join(" / ", parts) : "—";
    }

    private static bool LooksLikeFlutterPubspec(string moduleDir)
    {
        var path = Path.Combine(moduleDir, "pubspec.yaml");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(path);
            return text.Contains("flutter:", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("sdk: flutter", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}