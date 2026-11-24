namespace Qolome.Devtoy.Core.Services;

public static class DevtoyPaths
{
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qolome", "Devtoy");

    /// <summary>Devtoy 与 Nets 共用的本地 SQLite 库。</summary>
    public static string LocalDbPath => Path.Combine(AppDataRoot, "qolome-devtoy.db");

    public static string GlobalRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qolome");

    /// <summary>用户可编辑的 Velocity 模板目录（首次从随包默认模板复制）。</summary>
    public static string TemplatesRoot => Path.Combine(AppDataRoot, "templates");

    /// <summary>随应用发布的默认模板（只读种子）。</summary>
    public static string BundledTemplatesRoot
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("QOLOME_DEVTOY_TEMPLATES");
            if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            {
                return Path.GetFullPath(fromEnv);
            }

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "templates"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "templates")),
            };
            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }
    }
}