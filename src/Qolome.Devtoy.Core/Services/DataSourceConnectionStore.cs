using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>
/// 加密二进制连接配置（datasource.dat）。仅存连接字段，不含业务数据。
/// </summary>
public static class DataSourceConnectionStore
{
    private static readonly byte[] Magic = "QDD1"u8.ToArray();
    private const ushort FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DatPath => DevtoyPaths.DataSourceDatPath;

    public static DataSourceSettings? TryLoad()
    {
        try
        {
            if (!File.Exists(DatPath))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(DatPath);
            if (bytes.Length < Magic.Length + 2 + 4)
            {
                return null;
            }

            if (!bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                return null;
            }

            var version = BitConverter.ToUInt16(bytes, Magic.Length);
            if (version != FormatVersion)
            {
                return null;
            }

            var payloadLen = BitConverter.ToInt32(bytes, Magic.Length + 2);
            if (payloadLen <= 0 || Magic.Length + 2 + 4 + payloadLen != bytes.Length)
            {
                return null;
            }

            var packed = bytes.AsSpan(Magic.Length + 2 + 4, payloadLen).ToArray();
            var plain = MachineBoundCrypto.Unprotect(packed);
            if (plain is null)
            {
                return null;
            }

            var json = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            var dto = JsonSerializer.Deserialize<DataSourceConnectionDto>(json, JsonOptions);
            return dto?.ToSettings();
        }
        catch
        {
            return null;
        }
    }

    public static void Save(DataSourceSettings settings)
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
        DataSourceRuntime.Normalize(settings);
        var dto = DataSourceConnectionDto.FromSettings(settings);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var plain = Encoding.UTF8.GetBytes(json);
        var packed = MachineBoundCrypto.Protect(plain);
        CryptographicOperations.ZeroMemory(plain);

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(BitConverter.GetBytes(FormatVersion));
        ms.Write(BitConverter.GetBytes(packed.Length));
        ms.Write(packed);
        var path = DatPath;
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, ms.ToArray());
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }

    public static void GenerateSqliteCredentialsIfNeeded(DataSourceSettings ds)
    {
        if (ds.DatabaseKind != DatabaseKind.Sqlite)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ds.Username))
        {
            ds.Username = "devtoy";
        }

        if (string.IsNullOrWhiteSpace(ds.Password))
        {
            ds.Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        }
    }

    private sealed class DataSourceConnectionDto
    {
        public string InputMode { get; set; } = nameof(JdbcInputMode.Fields);
        public string DatabaseKind { get; set; } = nameof(Models.DatabaseKind.MySql);
        public string Host { get; set; } = "";
        public string Port { get; set; } = "";
        public string Database { get; set; } = "";
        public string SqlitePath { get; set; } = "";
        public string ConnectionParams { get; set; } = "";
        public string JdbcUrl { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        public static DataSourceConnectionDto FromSettings(DataSourceSettings s)
        {
            var sqlite = s.DatabaseKind == Models.DatabaseKind.Sqlite;
            return new DataSourceConnectionDto
            {
                InputMode = s.InputMode.ToString(),
                DatabaseKind = s.DatabaseKind.ToString(),
                Host = sqlite ? "" : (s.Host ?? ""),
                Port = sqlite ? "" : (s.Port ?? ""),
                Database = sqlite ? DataSourceRuntime.ResolveSqlitePath(s) : (s.Database ?? ""),
                SqlitePath = sqlite ? DataSourceRuntime.ResolveSqlitePath(s) : "",
                ConnectionParams = sqlite ? "" : (s.ConnectionParams ?? ""),
                JdbcUrl = sqlite ? "" : (s.JdbcUrl ?? ""),
                Username = s.Username ?? "",
                Password = s.Password ?? "",
            };
        }

        public DataSourceSettings ToSettings()
        {
            _ = Enum.TryParse<JdbcInputMode>(InputMode, out var mode);
            _ = Enum.TryParse<DatabaseKind>(DatabaseKind, out var kind);
            var ds = new DataSourceSettings
            {
                InputMode = mode,
                DatabaseKind = kind,
                Host = Host,
                Port = Port,
                Database = Database,
                SqlitePath = SqlitePath,
                ConnectionParams = ConnectionParams,
                JdbcUrl = JdbcUrl,
                Username = Username,
                Password = Password,
            };
            DataSourceRuntime.Normalize(ds);
            return ds;
        }
    }
}
