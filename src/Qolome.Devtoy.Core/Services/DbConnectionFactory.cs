using System.Data.Common;
using System.Data.Odbc;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>按 DatabaseKind 打开 ADO.NET 连接（含 ODBC）。</summary>
public static class DbConnectionFactory
{
    public static DbConnection Open(DataSourceSettings ds)
    {
        var conn = Create(ds);
        conn.Open();
        return conn;
    }

    public static DbConnection Create(DataSourceSettings ds)
    {
        var cs = AdoConnectionStrings.Build(ds);
        return ds.DatabaseKind switch
        {
            DatabaseKind.PostgreSql => new NpgsqlConnection(cs),
            DatabaseKind.Oracle => new OracleConnection(cs),
            DatabaseKind.Sqlite => new SqliteConnection(cs),
            DatabaseKind.Odbc => new OdbcConnection(cs),
            _ => new MySqlConnection(cs),
        };
    }

    public static DbCommand CreateCommand(DbConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;
        return cmd;
    }

    public static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = NormalizeParamName(cmd, name);
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static string NormalizeParamName(DbCommand cmd, string name)
    {
        var trimmed = name.Trim();
        if (cmd is OdbcCommand)
        {
            // ODBC 使用位置参数 "?"，名称仍写入便于调试；调用方 SQL 需用 ?
            return trimmed.TrimStart('@', ':');
        }

        if (cmd is OracleCommand)
        {
            return trimmed.StartsWith(':') ? trimmed : ":" + trimmed.TrimStart('@', ':');
        }

        return trimmed.StartsWith('@') ? trimmed : "@" + trimmed.TrimStart(':');
    }
}
