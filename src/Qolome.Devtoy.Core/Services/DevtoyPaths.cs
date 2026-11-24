namespace Qolome.Devtoy.Core.Services;

public static class DevtoyPaths
{
    public const string DefaultSqliteFileName = "devtoy.db";
    public const string LegacySqliteFileName = "qolome-devtoy.db";
    public const string DataSourceDatFileName = "datasource.dat";

    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qolome", "Devtoy");

    /// <summary>默认本地 SQLite 业务库路径（可被数据源 .dat 覆盖）。</summary>
    public static string DefaultSqliteDbPath => Path.Combine(AppDataRoot, DefaultSqliteFileName);

    /// <summary>旧版本地库路径（迁移源）。</summary>
    public static string LegacyLocalDbPath => Path.Combine(AppDataRoot, LegacySqliteFileName);

    /// <summary>当前生效的本地 SQLite 路径（来自连接配置或默认/迁移）。</summary>
    public static string LocalDbPath => DataSourceRuntime.ResolvedSqlitePath;

    /// <summary>加密连接配置文件。</summary>
    public static string DataSourceDatPath => Path.Combine(AppDataRoot, DataSourceDatFileName);

    public static string GlobalRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qolome");

    /// <summary>若仅有旧库则复制/沿用为默认 <c>devtoy.db</c>。</summary>
    public static string EnsureMigratedDefaultSqlitePath()
    {
        Directory.CreateDirectory(AppDataRoot);
        var def = DefaultSqliteDbPath;
        var legacy = LegacyLocalDbPath;
        if (File.Exists(def))
        {
            return def;
        }

        if (File.Exists(legacy))
        {
            File.Copy(legacy, def, overwrite: false);
            return def;
        }

        return def;
    }

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