namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

public sealed class ModuleScanner
{
    /// <summary>依赖 / 构建产物 / IDE 缓存 / 源码叶子，永不下钻。</summary>
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "target", "build", "dist", "out", "output", "coverage",
        ".git", ".idea", ".vscode", ".vs", ".cursor", ".svn", ".hg",
        "bin", "obj", ".gradle", ".mvn", ".next", ".nuxt", ".output", ".turbo",
        ".cache", ".parcel-cache", ".vite", ".svelte-kit", ".yarn", ".pnpm-store",
        "Pods", "DerivedData", "__pycache__", ".tox", ".venv", "venv",
        // Maven/Gradle 标准源码根，避免在已识别模块下空扫
        "src",
    };

    /// <summary>相对工作区根的最大扫描深度。</summary>
    private const int MaxDepth = 8;

    public IReadOnlyList<ModuleEntry> ScanWorkspace(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return Array.Empty<ModuleEntry>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ModuleEntry>();
        Collect(workspaceRoot, workspaceRoot, list, seen, depth: 0);
        list.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public IReadOnlyList<ModuleEntry> BuildSpecifiedModules(IReadOnlyList<SpecifiedProjectEntry> specified)
    {
        var list = new List<ModuleEntry>();
        foreach (var spec in specified)
        {
            if (string.IsNullOrWhiteSpace(spec.Root) || !Directory.Exists(spec.Root))
            {
                continue;
            }

            var root = Path.GetFullPath(spec.Root);
            var types = new ProjectTypes
            {
                Java = spec.Java,
                Web = spec.Web || spec.JavaScript,
                Flutter = spec.Flutter,
            };
            if (!spec.Manual)
            {
                var inferred = ProjectTypes.Infer(root);
                types = new ProjectTypes
                {
                    Java = spec.Java || inferred.Java,
                    Web = spec.Web || spec.JavaScript || inferred.Web,
                    Flutter = spec.Flutter || inferred.Flutter,
                };
            }

            var version = !string.IsNullOrWhiteSpace(spec.Version)
                ? spec.Version.Trim()
                : ModuleVersionReader.Read(root, types);
            var name = Path.GetFileName(root) ?? root;
            list.Add(new ModuleEntry
            {
                WorkspaceRoot = root,
                ModulePath = root,
                Label = $"{name} » .",
                ProjectTypes = types,
                ProjectVersion = version,
                IsSpecified = true,
            });
        }

        return list;
    }

    public static IReadOnlyList<ModuleEntry> Merge(IReadOnlyList<ModuleEntry> workspace, IReadOnlyList<ModuleEntry> specified)
    {
        var map = new Dictionary<string, ModuleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in workspace)
        {
            map[entry.ModulePath] = entry;
        }

        foreach (var entry in specified)
        {
            map[entry.ModulePath] = entry;
        }

        var merged = map.Values.ToList();
        merged.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return merged;
    }

    private static void Collect(
        string workspaceRoot,
        string dir,
        List<ModuleEntry> list,
        HashSet<string> seen,
        int depth)
    {
        dir = Path.GetFullPath(dir);
        if (!Directory.Exists(dir) || !seen.Add(dir))
        {
            return;
        }

        var backend = ProjectTypes.IsBackendModule(dir);
        var frontend = ProjectTypes.IsFrontendModule(dir);

        if (frontend)
        {
            list.Add(CreateEntry(workspaceRoot, dir));
            // Web/Flutter 叶子工程不再下钻
            return;
        }

        if (backend)
        {
            // packaging=pom / 无源码 Gradle 聚合工程不登记，但仍下钻识别有代码的子模块
            if (ProjectTypes.ShouldRegisterBackendModule(dir))
            {
                list.Add(CreateEntry(workspaceRoot, dir));
            }
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        Descend(workspaceRoot, dir, list, seen, depth);
    }

    private static void Descend(
        string workspaceRoot,
        string dir,
        List<ModuleEntry> list,
        HashSet<string> seen,
        int depth)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(dir);
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            string name;
            try
            {
                name = Path.GetFileName(child);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(name) || Skip.Contains(name))
            {
                continue;
            }

            Collect(workspaceRoot, child, list, seen, depth + 1);
        }
    }

    private static ModuleEntry CreateEntry(string workspaceRoot, string modulePath)
    {
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        modulePath = Path.GetFullPath(modulePath);
        var rootName = Path.GetFileName(workspaceRoot) ?? workspaceRoot;
        var relative = workspaceRoot.Equals(modulePath, StringComparison.OrdinalIgnoreCase)
            ? "."
            : Path.GetRelativePath(workspaceRoot, modulePath);
        var types = ProjectTypes.Infer(modulePath);
        // 项目名称使用文件夹名（见 ModuleEntry.ProjectNameDisplay）
        return new ModuleEntry
        {
            WorkspaceRoot = workspaceRoot,
            ModulePath = modulePath,
            Label = $"{rootName} » {relative}",
            ProjectTypes = types,
            ProjectVersion = ModuleVersionReader.Read(modulePath, types),
            IsSpecified = false,
        };
    }
}
