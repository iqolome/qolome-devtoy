using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public sealed class AppConfig
{
    public DatabaseConfig Database { get; set; } = new();
    public AppearanceConfig Appearance { get; set; } = new();
    public string TimeZone { get; set; } = AppTimeZones.System;

    public void Normalize()
    {
        Database ??= new DatabaseConfig();
        Database.Normalize();
        Appearance ??= new AppearanceConfig();
        Appearance.Normalize();
        TimeZone = AppTimeZones.Normalize(TimeZone);
    }
}

public sealed class AppearanceConfig
{
    public string Theme { get; set; } = AppThemes.System;

    public void Normalize() => Theme = AppThemes.Normalize(Theme);
}

public static class AppThemes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System
    };

    public static int ToIndex(string? value) => Normalize(value) switch
    {
        Light => 1,
        Dark => 2,
        _ => 0
    };

    public static string FromIndex(int index) => index switch
    {
        1 => Light,
        2 => Dark,
        _ => System
    };
}

public sealed class DatabaseConfig
{
    public string Provider { get; set; } = DatabaseProviders.Sqlite;
    public SqliteConfig Sqlite { get; set; } = new();
    public MySqlConfig Mysql { get; set; } = new();

    [JsonIgnore]
    public bool IsMySql => DatabaseProviders.IsMySql(Provider);

    [JsonIgnore]
    public bool IsSqlite => !IsMySql;

    public void Normalize()
    {
        Provider = DatabaseProviders.Normalize(Provider);
        Sqlite ??= new SqliteConfig();
        Mysql ??= new MySqlConfig();
        Mysql.Port = Mysql.Port <= 0 ? 3306 : Math.Clamp(Mysql.Port, 1, 65535);
        Mysql.Host = string.IsNullOrWhiteSpace(Mysql.Host) ? "127.0.0.1" : Mysql.Host.Trim();
        Mysql.Database = string.IsNullOrWhiteSpace(Mysql.Database) ? "qolome_nets" : Mysql.Database.Trim();
        Mysql.User = string.IsNullOrWhiteSpace(Mysql.User) ? "root" : Mysql.User.Trim();
        Mysql.Password ??= "";
        Sqlite.Path = string.IsNullOrWhiteSpace(Sqlite.Path) ? "" : Sqlite.Path.Trim();
    }
}

public sealed class SqliteConfig
{
    public string Path { get; set; } = "";
}

public sealed class MySqlConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "qolome_nets";
    public string User { get; set; } = "root";

    [JsonIgnore]
    public string Password { get; set; } = "";

    [JsonIgnore]
    public bool PasswordUnprotectFailed { get; set; }

    [JsonIgnore]
    public bool NeedsReprotect { get; set; }

    private string? _storedPayload;

    [JsonPropertyName("password")]
    public string PasswordPayload
    {
        get
        {
            if (MachineBoundSecret.IsOmittingPassword)
            {
                return "";
            }

            if (PasswordUnprotectFailed && string.IsNullOrEmpty(Password))
            {
                return _storedPayload ?? "";
            }

            return MachineBoundSecret.ToStorage(Password);
        }
        set
        {
            _storedPayload = value;
            Password = MachineBoundSecret.FromStorage(value);
            PasswordUnprotectFailed = MachineBoundSecret.LastUnprotectFailed;
            NeedsReprotect = MachineBoundSecret.LastWasLegacyPlaintext && !string.IsNullOrEmpty(Password);
        }
    }

    public void SetPasswordFromUser(string? password)
    {
        Password = password ?? "";
        PasswordUnprotectFailed = false;
        NeedsReprotect = false;
        _storedPayload = null;
    }
}

public static class DatabaseProviders
{
    public const string Sqlite = "sqlite";
    public const string MySql = "mysql";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), MySql, StringComparison.OrdinalIgnoreCase) ? MySql : Sqlite;

    public static bool IsMySql(string? value) => Normalize(value) == MySql;
}

public static class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppConfig Current { get; private set; } = new();

    public static string DataDirectory => DevtoyPaths.AppDataRoot;

    public static string UserFilePath => Path.Combine(DataDirectory, "appsettings.json");

    public static string BundledFilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string ExampleFilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");

    public static string ActiveFilePath { get; private set; } = UserFilePath;

    public static AppConfig Load()
    {
        Directory.CreateDirectory(DataDirectory);
        var path = ResolveLoadPath();
        ActiveFilePath = File.Exists(UserFilePath) ? UserFilePath : path;
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                Current = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                throw new ScanException($"无法读取配置文件「{path}」：{ex.Message}");
            }
        }
        else
        {
            Current = new AppConfig();
        }

        Current.Normalize();
        if (Current.Database.Mysql.PasswordUnprotectFailed && Current.Database.IsMySql)
        {
            AppDatabase.StartupWarning = string.IsNullOrWhiteSpace(AppDatabase.StartupWarning)
                ? "无法用当前主板解密 MySQL 密码（可能已更换设备），请在设置中重新输入。"
                : AppDatabase.StartupWarning;
        }

        if (!File.Exists(UserFilePath) || Current.Database.Mysql.NeedsReprotect)
        {
            Save(Current);
        }

        return Current;
    }

    public static void Save(AppConfig config)
    {
        config.Normalize();
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(UserFilePath, json);
        Current = config;
        ActiveFilePath = UserFilePath;
    }

    private static string ResolveLoadPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("QOLOME_NETS_CONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv.Trim()))
        {
            return Path.GetFullPath(fromEnv.Trim());
        }

        if (File.Exists(UserFilePath))
        {
            return UserFilePath;
        }

        if (File.Exists(BundledFilePath))
        {
            return BundledFilePath;
        }

        return UserFilePath;
    }
}
