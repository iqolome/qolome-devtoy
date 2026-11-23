namespace Qolome.Devtoy.Core.Services;

using System.IO.Compression;
using System.Text.Json;
using Qolome.Devtoy.Core.Models;

public static class ConfigProjectArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string Entry = "devtoy-settings.json";

    public static void Export(string zipPath, DevtoySettings settings)
    {
        var dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(Entry);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(json);
    }

    public static DevtoySettings Import(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(Entry)
            ?? throw new InvalidDataException("ZIP 中缺少 devtoy-settings.json");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();
    }
}