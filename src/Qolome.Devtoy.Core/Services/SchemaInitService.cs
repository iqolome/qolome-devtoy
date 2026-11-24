namespace Qolome.Devtoy.Core.Services;

using System.Data.Common;
using System.Text;
using Qolome.Devtoy.Core.Models;

/// <summary>
/// Devtoy 依赖表初始化：按数据源类型生成建表 SQL，并执行初始化。
/// 初始化前若目标表已存在则抛出异常（不做 IF NOT EXISTS）。
/// </summary>
public static class SchemaInitService
{
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        "qolome_dict_item",
        "qolome_dict_val",
    ];

    /// <summary>
    /// 按当前数据源生成可执行的初始化 DDL（表结构定义，非从线上库提取）。
    /// </summary>
    public static string GenerateInitSql(DataSourceSettings dataSource)
    {
        EnsureSupported(dataSource);
        var database = ResolveDatabaseName(dataSource);
        var body = LoadBundledInitSql(dataSource.DatabaseKind);
        var sb = new StringBuilder();
        sb.AppendLine("-- Qolome Devtoy 初始化 DDL");
        sb.AppendLine($"-- databaseKind={dataSource.DatabaseKind}");
        if (!string.IsNullOrWhiteSpace(database))
        {
            sb.AppendLine($"-- database={database}");
        }

        sb.AppendLine($"-- tables={string.Join(", ", RequiredTables)}");
        sb.AppendLine($"-- generated={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.Append(body.TrimEnd());
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// 初始化：检测表已存在则抛异常；否则按数据源生成建表 SQL 并执行。
    /// </summary>
    public static void Initialize(DataSourceSettings dataSource)
    {
        EnsureSupported(dataSource);

        using var conn = DbConnectionFactory.Open(dataSource);
        var existing = FindExistingTables(conn, RequiredTables);
        if (existing.Count > 0)
        {
            var names = string.Join(", ", existing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"目标库已存在表：{names}。初始化已中止，请改用空库或先清理同名表后再试。");
        }

        var sql = GenerateInitSql(dataSource);
        ExecuteScript(conn, sql);
    }

    public static IReadOnlyList<string> ListExistingRequiredTables(DataSourceSettings dataSource)
    {
        EnsureSupported(dataSource);
        using var conn = DbConnectionFactory.Open(dataSource);
        return FindExistingTables(conn, RequiredTables)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void EnsureSupported(DataSourceSettings dataSource)
    {
        if (dataSource.DatabaseKind != DatabaseKind.MySql)
        {
            throw new NotSupportedException(
                $"初始化目前仅支持 MySQL，当前为 {dataSource.DatabaseKind}。");
        }
    }

    private static string ResolveDatabaseName(DataSourceSettings dataSource)
    {
        if (dataSource.InputMode == JdbcInputMode.Fields
            && !string.IsNullOrWhiteSpace(dataSource.Database))
        {
            return dataSource.Database.Trim();
        }

        var url = JdbcUrlBuilder.Resolve(dataSource);
        // jdbc:mysql://host:port/dbname?...
        var schemeSep = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeSep < 0)
        {
            return dataSource.Database?.Trim() ?? "";
        }

        var afterHost = url[(schemeSep + 3)..];
        var slash = afterHost.IndexOf('/');
        if (slash < 0)
        {
            return dataSource.Database?.Trim() ?? "";
        }

        var dbAndQuery = afterHost[(slash + 1)..];
        var q = dbAndQuery.IndexOfAny(['?', ';']);
        return (q < 0 ? dbAndQuery : dbAndQuery[..q]).Trim();
    }

    private static string LoadBundledInitSql(DatabaseKind kind)
    {
        if (kind != DatabaseKind.MySql)
        {
            throw new NotSupportedException("暂无 " + kind + " 的初始化 SQL 模板。");
        }

        var path = ResolveBundledSqlPath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到初始化 SQL 模板: " + path);
        }

        return File.ReadAllText(path);
    }

    private static HashSet<string> FindExistingTables(DbConnection conn, IReadOnlyList<string> tables)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string sql = """
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @name
            LIMIT 1
            """;
        foreach (var table in tables)
        {
            using var cmd = DbConnectionFactory.CreateCommand(conn, sql);
            DbConnectionFactory.AddParam(cmd, "@name", table);
            var result = cmd.ExecuteScalar();
            if (result is string name && !string.IsNullOrWhiteSpace(name))
            {
                found.Add(name);
            }
        }

        return found;
    }

    private static void ExecuteScript(DbConnection conn, string script)
    {
        foreach (var batch in SplitSqlBatches(script))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            using var cmd = DbConnectionFactory.CreateCommand(conn, batch);
            cmd.CommandTimeout = 120;
            cmd.ExecuteNonQuery();
        }
    }

    private static IEnumerable<string> SplitSqlBatches(string script)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(script);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal)
                || trimmed.StartsWith('#'))
            {
                continue;
            }

            sb.AppendLine(line);
            if (trimmed.EndsWith(';'))
            {
                var batch = sb.ToString().Trim();
                sb.Clear();
                if (batch.Length > 0)
                {
                    yield return batch.TrimEnd(';').Trim();
                }
            }
        }

        var tail = sb.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail.TrimEnd(';').Trim();
        }
    }

    private static string ResolveBundledSqlPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("QOLOME_DEVTOY_SQL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var envFile = Path.Combine(fromEnv, "qolome_devtoy_mysql_init.sql");
            if (File.Exists(envFile))
            {
                return envFile;
            }
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "sql", "qolome_devtoy_mysql_init.sql"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "sql", "qolome_devtoy_mysql_init.sql")),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
