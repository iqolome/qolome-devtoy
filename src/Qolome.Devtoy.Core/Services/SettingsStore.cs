namespace Qolome.Devtoy.Core.Services;

using System.Text.Json;
using Qolome.Devtoy.Core.Models;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string SettingsPath => Path.Combine(DevtoyPaths.AppDataRoot, "settings.json");

    public string GlobalPath => Path.Combine(DevtoyPaths.GlobalRoot, "codegen-global.json");

    public DevtoySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();
                var migrated = LegacySpecifiedMigration.TryApplyFromJson(json, settings);
                var rootsBefore = settings.WorkspaceRoots.Count;
                settings.NormalizeWorkspaceRoots();
                if (migrated || (rootsBefore == 0 && settings.WorkspaceRoots.Count > 0))
                {
                    Save(settings);
                }

                return settings;
            }
        }
        catch
        {
            // ignored
        }

        return new DevtoySettings();
    }

    public void Save(DevtoySettings settings)
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public void ExportGlobal(DevtoySettings settings)
    {
        Directory.CreateDirectory(DevtoyPaths.GlobalRoot);
        File.WriteAllText(GlobalPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public DevtoySettings? ImportGlobal()
    {
        if (!File.Exists(GlobalPath))
        {
            return null;
        }

        var json = File.ReadAllText(GlobalPath);
        var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions);
        if (settings is not null)
        {
            LegacySpecifiedMigration.TryApplyFromJson(json, settings);
            settings.NormalizeWorkspaceRoots();
        }

        return settings;
    }
}