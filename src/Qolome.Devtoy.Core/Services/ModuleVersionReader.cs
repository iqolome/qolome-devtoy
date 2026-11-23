namespace Qolome.Devtoy.Core.Services;

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Qolome.Devtoy.Core.Models;

public static partial class ModuleVersionReader
{
    [GeneratedRegex("\"version\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex PackageVersion();

    [GeneratedRegex("(?m)^\\s*version\\s*=\\s*['\"]([^'\"]+)['\"]")]
    private static partial Regex GradleVersionEquals();

    [GeneratedRegex("(?m)^\\s*version\\s+['\"]([^'\"]+)['\"]")]
    private static partial Regex GradleVersionSpace();

    [GeneratedRegex("(?m)^\\s*version\\s*=\\s*([^\\s#]+)")]
    private static partial Regex GradlePropertiesVersion();

    [GeneratedRegex("(?m)^\\s*version:\\s*([^\\s#+]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PubspecVersionLine();

    /// <summary>按工程类型读取版本：后端优先 Maven/Gradle，前端读 package.json。</summary>
    public static string Read(string moduleDir, ProjectTypes? types = null)
    {
        if (!Directory.Exists(moduleDir))
        {
            return "";
        }

        types ??= ProjectTypes.Infer(moduleDir);
        if (types.Java)
        {
            var backend = ReadBackendVersion(moduleDir);
            if (!string.IsNullOrWhiteSpace(backend))
            {
                return backend;
            }
        }

        if (types.Flutter)
        {
            var flutter = ReadPubspecVersion(Path.Combine(moduleDir, "pubspec.yaml"));
            if (!string.IsNullOrWhiteSpace(flutter))
            {
                return flutter;
            }
        }

        if (types.Web)
        {
            var frontend = ReadPackageVersion(Path.Combine(moduleDir, "package.json"));
            if (!string.IsNullOrWhiteSpace(frontend))
            {
                return frontend;
            }
        }

        return ReadBackendVersion(moduleDir) is { Length: > 0 } b
            ? b
            : ReadPackageVersion(Path.Combine(moduleDir, "package.json"));
    }

    public static string ReadBackendVersion(string moduleDir)
    {
        var fromPom = ReadPomVersion(Path.Combine(moduleDir, "pom.xml"));
        if (!string.IsNullOrWhiteSpace(fromPom))
        {
            return fromPom;
        }

        var gradle = Path.Combine(moduleDir, "build.gradle");
        var gradleKts = Path.Combine(moduleDir, "build.gradle.kts");
        if (File.Exists(gradle))
        {
            return ReadGradleVersionChain(moduleDir, gradle);
        }

        if (File.Exists(gradleKts))
        {
            return ReadGradleVersionChain(moduleDir, gradleKts);
        }

        return ReadGradleVersionChain(moduleDir, null);
    }

    /// <summary>
    /// Maven：取 project 直接子元素 version；未定义则取 parent/version（并沿 relativePath 向上解析属性）。
    /// </summary>
    private static string ReadPomVersion(string pomPath) =>
        ReadPomVersionRecursive(pomPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

    private static string ReadPomVersionRecursive(string pomPath, HashSet<string> visited, int depth)
    {
        if (depth > 8 || !File.Exists(pomPath))
        {
            return "";
        }

        var full = Path.GetFullPath(pomPath);
        if (!visited.Add(full))
        {
            return "";
        }

        try
        {
            var doc = XDocument.Load(full);
            var root = doc.Root;
            if (root is null)
            {
                return "";
            }

            var ns = root.Name.Namespace;
            var own = NormalizeVersion(root.Element(ns + "version")?.Value);
            if (!string.IsNullOrEmpty(own))
            {
                return ResolveMavenVersion(own, root, ns, full, visited, depth);
            }

            var parent = root.Element(ns + "parent");
            var parentVer = NormalizeVersion(parent?.Element(ns + "version")?.Value);
            if (!string.IsNullOrEmpty(parentVer))
            {
                return ResolveMavenVersion(parentVer, root, ns, full, visited, depth);
            }

            return FollowParentPom(parent, full, visited, depth);
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveMavenVersion(
        string version,
        XElement project,
        XNamespace ns,
        string pomPath,
        HashSet<string> visited,
        int depth)
    {
        if (!IsPropertyRef(version))
        {
            return version;
        }

        var key = version[2..^1].Trim();
        if (key.Length == 0)
        {
            return "";
        }

        var fromProps = NormalizeVersion(project.Element(ns + "properties")?.Element(ns + key)?.Value);
        if (!string.IsNullOrEmpty(fromProps) && !IsPropertyRef(fromProps))
        {
            return fromProps;
        }

        if (!string.IsNullOrEmpty(fromProps) && IsPropertyRef(fromProps))
        {
            return ResolveMavenVersion(fromProps, project, ns, pomPath, visited, depth);
        }

        var parent = project.Element(ns + "parent");
        var fromParentFile = FollowParentPom(parent, pomPath, visited, depth);
        return fromParentFile;
    }

    private static string FollowParentPom(
        XElement? parent,
        string pomPath,
        HashSet<string> visited,
        int depth)
    {
        if (parent is null)
        {
            return "";
        }

        var ns = parent.Name.Namespace;
        var relative = parent.Element(ns + "relativePath")?.Value;
        // 空 relativePath 表示不从本地解析 parent
        if (relative is not null && string.IsNullOrWhiteSpace(relative))
        {
            return "";
        }

        relative = string.IsNullOrWhiteSpace(relative) ? "../pom.xml" : relative.Trim();
        var dir = Path.GetDirectoryName(pomPath);
        if (string.IsNullOrEmpty(dir))
        {
            return "";
        }

        var parentPom = Path.GetFullPath(Path.Combine(dir, relative));
        if (Directory.Exists(parentPom))
        {
            parentPom = Path.Combine(parentPom, "pom.xml");
        }

        return ReadPomVersionRecursive(parentPom, visited, depth + 1);
    }

    /// <summary>Gradle：本模块 version → gradle.properties → 向上父目录同类文件。</summary>
    private static string ReadGradleVersionChain(string moduleDir, string? buildFile)
    {
        if (buildFile is not null)
        {
            var fromBuild = ReadGradleVersionInFile(buildFile);
            if (!string.IsNullOrWhiteSpace(fromBuild))
            {
                return fromBuild;
            }
        }

        var fromProps = ReadGradlePropertiesVersion(Path.Combine(moduleDir, "gradle.properties"));
        if (!string.IsNullOrWhiteSpace(fromProps))
        {
            return fromProps;
        }

        var current = Directory.GetParent(moduleDir);
        for (var i = 0; i < 8 && current is not null; i++, current = current.Parent)
        {
            foreach (var name in new[] { "build.gradle.kts", "build.gradle" })
            {
                var path = Path.Combine(current.FullName, name);
                if (!File.Exists(path))
                {
                    continue;
                }

                var v = ReadGradleVersionInFile(path);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            var props = Path.Combine(current.FullName, "gradle.properties");
            var pv = ReadGradlePropertiesVersion(props);
            if (!string.IsNullOrWhiteSpace(pv))
            {
                return pv;
            }
        }

        return "";
    }

    private static string ReadGradleVersionInFile(string gradleFile)
    {
        try
        {
            var text = File.ReadAllText(gradleFile);
            var match = GradleVersionEquals().Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            match = GradleVersionSpace().Match(text);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadGradlePropertiesVersion(string propertiesPath)
    {
        if (!File.Exists(propertiesPath))
        {
            return "";
        }

        try
        {
            var match = GradlePropertiesVersion().Match(File.ReadAllText(propertiesPath));
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadPubspecVersion(string pubspecPath)
    {
        if (!File.Exists(pubspecPath))
        {
            return "";
        }

        try
        {
            var match = PubspecVersionLine().Match(File.ReadAllText(pubspecPath));
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadPackageVersion(string packageJsonPath)
    {
        if (!File.Exists(packageJsonPath))
        {
            return "";
        }

        try
        {
            var match = PackageVersion().Match(File.ReadAllText(packageJsonPath));
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        return raw.Trim();
    }

    private static bool IsPropertyRef(string version) =>
        version.StartsWith("${", StringComparison.Ordinal) && version.EndsWith('}');
}
