namespace Qolome.Devtoy.Core.Services;

using System.Text.Json;
using Qolome.Devtoy.Core.Models;

/// <summary>将 settings.json 中的旧字段 externalRoots 转为 <see cref="DevtoySettings.SpecifiedProjects"/>。</summary>
internal static class LegacySpecifiedMigration
{
    internal static bool TryApplyFromJson(string json, DevtoySettings settings)
    {
        var roots = ReadLegacyRoots(json);
        return TryApply(settings, roots);
    }

    internal static bool TryApply(DevtoySettings settings, IReadOnlyList<string> legacyRoots)
    {
        if (settings.SpecifiedProjects.Count > 0 || legacyRoots.Count == 0)
        {
            return false;
        }

        foreach (var root in legacyRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var recognition = ProjectRecognizer.Recognize(root);
            settings.SpecifiedProjects.Add(new SpecifiedProjectEntry
            {
                Root = Path.GetFullPath(root),
                Java = recognition.Types.Java,
                Web = recognition.Types.Web,
                Flutter = recognition.Types.Flutter,
                Version = string.IsNullOrWhiteSpace(recognition.Version) ? null : recognition.Version,
                Manual = !recognition.AutoRecognized,
            });
        }

        return settings.SpecifiedProjects.Count > 0;
    }

    private static IReadOnlyList<string> ReadLegacyRoots(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("externalRoots", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var roots = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var path = item.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    roots.Add(path);
                }
            }

            return roots;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}