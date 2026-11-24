namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

public static class JdbcUrlBuilder
{
    public static string Resolve(DataSourceSettings ds)
    {
        if (ds.InputMode == JdbcInputMode.Url && !string.IsNullOrWhiteSpace(ds.JdbcUrl))
        {
            return ds.JdbcUrl.Trim();
        }

        var url = Build(ds.DatabaseKind, ds.Host, ds.Port, ds.Database);
        return AppendParams(url, ds.ConnectionParams);
    }

    public static string Build(DatabaseKind kind, string host, string port, string database)
    {
        host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        port = string.IsNullOrWhiteSpace(port) ? DefaultPort(kind) : port.Trim();
        database = database?.Trim() ?? string.Empty;
        return kind switch
        {
            DatabaseKind.PostgreSql => $"jdbc:postgresql://{host}:{port}/{database}",
            DatabaseKind.Oracle => $"jdbc:oracle:thin:@//{host}:{port}/{database}",
            DatabaseKind.Sqlite => string.IsNullOrWhiteSpace(database)
                ? "jdbc:sqlite::memory:"
                : $"jdbc:sqlite:{database}",
            DatabaseKind.Odbc => string.IsNullOrWhiteSpace(database)
                ? "odbc:DSN="
                : "odbc:" + database.Trim(),
            _ => $"jdbc:mysql://{host}:{port}/{database}",
        };
    }

    public static string AppendParams(string url, string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return url;
        }

        var trimmed = parameters.Trim().TrimStart('?', '&');
        if (url.Contains('?', StringComparison.Ordinal))
        {
            return url.EndsWith('&') ? url + trimmed : url + "&" + trimmed;
        }

        return url + "?" + trimmed;
    }

    public static string ToMySqlConnectionString(string jdbcUrl, string user, string password)
    {
        // jdbc:mysql://host:port/db?...
        var raw = jdbcUrl.Replace("jdbc:mysql://", "", StringComparison.OrdinalIgnoreCase);
        var slash = raw.IndexOf('/');
        if (slash < 0)
        {
            throw new ArgumentException("无效的 MySQL JDBC URL");
        }

        var hostPort = raw[..slash];
        var rest = raw[(slash + 1)..];
        var query = rest.IndexOf('?');
        var db = query >= 0 ? rest[..query] : rest;
        var host = hostPort;
        var port = "3306";
        var colon = hostPort.LastIndexOf(':');
        if (colon > 0)
        {
            host = hostPort[..colon];
            port = hostPort[(colon + 1)..];
        }

        var builder = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = host,
            Port = uint.TryParse(port, out var p) ? p : 3306,
            Database = db,
            UserID = user,
            Password = password,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 15,
            MaximumPoolSize = 5,
        };
        return builder.ConnectionString;
    }

    private static string DefaultPort(DatabaseKind kind) =>
        kind switch
        {
            DatabaseKind.PostgreSql => "5432",
            DatabaseKind.Oracle => "1521",
            DatabaseKind.Sqlite => "",
            DatabaseKind.Odbc => "",
            _ => "3306",
        };
}