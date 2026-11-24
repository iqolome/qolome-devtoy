using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>将数据源设置 / JDBC URL 转为各驱动 ADO.NET 连接串。</summary>
public static class AdoConnectionStrings
{
    private const string SqliteMemoryDataSource = ":memory:";

    public static string Build(DataSourceSettings ds) =>
        ds.DatabaseKind switch
        {
            DatabaseKind.PostgreSql => ToPostgreSql(ds),
            DatabaseKind.Oracle => ToOracle(ds),
            DatabaseKind.Sqlite => ToSqlite(ds),
            DatabaseKind.Odbc => ToOdbc(ds),
            _ => ToMySql(ds),
        };

    public static string ToMySql(DataSourceSettings ds)
    {
        var url = JdbcUrlBuilder.Resolve(ds);
        return JdbcUrlBuilder.ToMySqlConnectionString(url, ds.Username, ds.Password);
    }

    public static string ToPostgreSql(DataSourceSettings ds)
    {
        if (ds.InputMode == JdbcInputMode.Url && !string.IsNullOrWhiteSpace(ds.JdbcUrl))
        {
            return FromJdbcPostgreSql(ds.JdbcUrl.Trim(), ds.Username, ds.Password);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(ds.Host) ? "127.0.0.1" : ds.Host.Trim(),
            Port = int.TryParse(ds.Port, out var p) ? p : 5432,
            Database = string.IsNullOrWhiteSpace(ds.Database) ? "postgres" : ds.Database.Trim(),
            Username = ds.Username,
            Password = ds.Password,
            Timeout = 5,
            CommandTimeout = 30,
        };
        return builder.ConnectionString;
    }

    public static string ToOracle(DataSourceSettings ds)
    {
        if (ds.InputMode == JdbcInputMode.Url && !string.IsNullOrWhiteSpace(ds.JdbcUrl))
        {
            return FromJdbcOracle(ds.JdbcUrl.Trim(), ds.Username, ds.Password);
        }

        var host = string.IsNullOrWhiteSpace(ds.Host) ? "127.0.0.1" : ds.Host.Trim();
        var port = string.IsNullOrWhiteSpace(ds.Port) ? "1521" : ds.Port.Trim();
        var service = string.IsNullOrWhiteSpace(ds.Database) ? "ORCL" : ds.Database.Trim();
        var builder = new OracleConnectionStringBuilder
        {
            DataSource = $"{host}:{port}/{service}",
            UserID = ds.Username,
            Password = ds.Password,
        };
        return builder.ConnectionString;
    }

    public static string ToSqlite(DataSourceSettings ds)
    {
        string dataSource;
        if (ds.InputMode == JdbcInputMode.Url && !string.IsNullOrWhiteSpace(ds.JdbcUrl))
        {
            dataSource = FromJdbcSqlitePath(ds.JdbcUrl.Trim());
        }
        else
        {
            dataSource = string.IsNullOrWhiteSpace(ds.Database) ? SqliteMemoryDataSource : ds.Database.Trim();
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = string.Equals(dataSource, SqliteMemoryDataSource, StringComparison.OrdinalIgnoreCase)
                ? SqliteOpenMode.Memory
                : SqliteOpenMode.ReadWriteCreate,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// ODBC：优先完整连接串（JdbcUrl / ConnectionParams），否则按 Driver+Server 拼简易串。
    /// </summary>
    public static string ToOdbc(DataSourceSettings ds)
    {
        if (ds.InputMode == JdbcInputMode.Url && !string.IsNullOrWhiteSpace(ds.JdbcUrl))
        {
            return AppendUserPasswordIfMissing(ds.JdbcUrl.Trim(), ds.Username, ds.Password);
        }

        if (!string.IsNullOrWhiteSpace(ds.ConnectionParams)
            && (ds.ConnectionParams.Contains('=')
                || ds.ConnectionParams.Contains("DSN=", StringComparison.OrdinalIgnoreCase)
                || ds.ConnectionParams.Contains("Driver=", StringComparison.OrdinalIgnoreCase)))
        {
            return AppendUserPasswordIfMissing(ds.ConnectionParams.Trim(), ds.Username, ds.Password);
        }

        var driver = string.IsNullOrWhiteSpace(ds.ConnectionParams)
            ? "{PostgreSQL Unicode}"
            : ds.ConnectionParams.Trim();
        if (!driver.Contains('=') && !driver.StartsWith('{'))
        {
            driver = "{" + driver.Trim('{', '}') + "}";
        }

        var parts = new List<string>();
        if (driver.Contains('='))
        {
            parts.Add(driver.TrimEnd(';'));
        }
        else
        {
            parts.Add("Driver=" + driver);
        }

        if (!string.IsNullOrWhiteSpace(ds.Host))
        {
            parts.Add("Server=" + ds.Host.Trim());
        }

        if (!string.IsNullOrWhiteSpace(ds.Port))
        {
            parts.Add("Port=" + ds.Port.Trim());
        }

        if (!string.IsNullOrWhiteSpace(ds.Database))
        {
            parts.Add("Database=" + ds.Database.Trim());
        }

        if (!string.IsNullOrWhiteSpace(ds.Username))
        {
            parts.Add("Uid=" + ds.Username.Trim());
        }

        if (!string.IsNullOrEmpty(ds.Password))
        {
            parts.Add("Pwd=" + ds.Password);
        }

        return string.Join(";", parts);
    }

    private static string FromJdbcPostgreSql(string jdbcUrl, string user, string password)
    {
        var raw = jdbcUrl
            .Replace("jdbc:postgresql://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("jdbc:postgres://", "", StringComparison.OrdinalIgnoreCase);
        var slash = raw.IndexOf('/');
        if (slash < 0)
        {
            throw new ArgumentException("无效的 PostgreSQL JDBC URL");
        }

        var hostPort = raw[..slash];
        var rest = raw[(slash + 1)..];
        var q = rest.IndexOf('?');
        var db = q >= 0 ? rest[..q] : rest;
        var host = hostPort;
        var port = 5432;
        var colon = hostPort.LastIndexOf(':');
        if (colon > 0)
        {
            host = hostPort[..colon];
            _ = int.TryParse(hostPort[(colon + 1)..], out port);
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = db,
            Username = user,
            Password = password,
            Timeout = 5,
            CommandTimeout = 30,
        }.ConnectionString;
    }

    private static string FromJdbcOracle(string jdbcUrl, string user, string password)
    {
        // jdbc:oracle:thin:@//host:port/service 或 @host:port:sid
        var marker = jdbcUrl.IndexOf('@');
        if (marker < 0)
        {
            throw new ArgumentException("无效的 Oracle JDBC URL");
        }

        var dataSource = jdbcUrl[(marker + 1)..].Trim();
        if (dataSource.StartsWith("//", StringComparison.Ordinal))
        {
            dataSource = dataSource[2..];
        }

        var q = dataSource.IndexOf('?');
        if (q >= 0)
        {
            dataSource = dataSource[..q];
        }

        return new OracleConnectionStringBuilder
        {
            DataSource = dataSource,
            UserID = user,
            Password = password,
        }.ConnectionString;
    }

    private static string FromJdbcSqlitePath(string jdbcUrl)
    {
        const string prefix = "jdbc:sqlite:";
        if (!jdbcUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("无效的 SQLite JDBC URL");
        }

        var path = jdbcUrl[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(path) || path == SqliteMemoryDataSource ? SqliteMemoryDataSource : path;
    }

    private static string AppendUserPasswordIfMissing(string connectionString, string user, string password)
    {
        var cs = connectionString.Trim().TrimEnd(';');
        var lower = cs.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(user)
            && !lower.Contains("uid=", StringComparison.Ordinal)
            && !lower.Contains("user=", StringComparison.Ordinal)
            && !lower.Contains("username=", StringComparison.Ordinal))
        {
            cs += ";Uid=" + user.Trim();
        }

        if (!string.IsNullOrEmpty(password)
            && !lower.Contains("pwd=", StringComparison.Ordinal)
            && !lower.Contains("password=", StringComparison.Ordinal))
        {
            cs += ";Pwd=" + password;
        }

        return cs;
    }
}
