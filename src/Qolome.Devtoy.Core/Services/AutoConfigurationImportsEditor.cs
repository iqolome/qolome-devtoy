namespace Qolome.Devtoy.Core.Services;

using System.Text;

/// <summary>
/// 维护 META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports：
/// 按 # convert / # service / # controller 分区追加全限定类名（去重）。
/// </summary>
public sealed class AutoConfigurationImportsEditor
{
    private readonly string _path;

    public AutoConfigurationImportsEditor(string modulePath) =>
        _path = BootStarterDetector.ResolveImportsPath(modulePath);

    public void EnsureClasses(IEnumerable<(string Section, string Fqcn)> entries)
    {
        var list = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Fqcn) && !string.IsNullOrWhiteSpace(e.Section))
            .Select(e => (Section: NormalizeSection(e.Section), Fqcn: e.Fqcn.Trim()))
            .ToList();
        if (list.Count == 0)
        {
            return;
        }

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var lines = File.Exists(_path)
            ? File.ReadAllLines(_path, Encoding.UTF8).ToList()
            : CreateHeader();

        var existing = new HashSet<string>(
            lines.Where(l => IsClassLine(l)).Select(l => l.Trim()),
            StringComparer.Ordinal);

        foreach (var group in list.GroupBy(e => e.Section, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (_, fqcn) in group)
            {
                if (!existing.Add(fqcn))
                {
                    continue;
                }

                InsertUnderSection(lines, group.Key, fqcn);
            }
        }

        File.WriteAllLines(_path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static List<string> CreateHeader() =>
    [
        "#",
        "# Spring Boot AutoConfiguration.imports (managed by Qolome DevToy)",
        "#",
        "",
        "# convert",
        "",
        "# service",
        "",
        "# controller",
        "",
    ];

    private static string NormalizeSection(string section)
    {
        var s = section.Trim().TrimStart('#').Trim().ToLowerInvariant();
        return s switch
        {
            "convert" => "convert",
            "service" => "service",
            "controller" => "controller",
            _ => s,
        };
    }

    private static bool IsClassLine(string line)
    {
        var t = line.Trim();
        return t.Length > 0 && !t.StartsWith('#') && t.Contains('.', StringComparison.Ordinal);
    }

    private static void InsertUnderSection(List<string> lines, string section, string fqcn)
    {
        var header = "# " + section;
        var headerIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Trim(), header, StringComparison.OrdinalIgnoreCase))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add("");
            }

            lines.Add(header);
            lines.Add(fqcn);
            return;
        }

        var insertAt = headerIndex + 1;
        while (insertAt < lines.Count)
        {
            var t = lines[insertAt].Trim();
            if (t.StartsWith('#') && !t.StartsWith("# " + section, StringComparison.OrdinalIgnoreCase))
            {
                // 下一分区开始
                break;
            }

            insertAt++;
        }

        // 插在分区末尾空行之前
        while (insertAt > headerIndex + 1 && string.IsNullOrWhiteSpace(lines[insertAt - 1]))
        {
            insertAt--;
        }

        lines.Insert(insertAt, fqcn);
    }
}
