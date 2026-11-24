using System.Text.RegularExpressions;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

internal static class NetsSqlIdentifiers
{
    private static readonly HashSet<string> KnownSchemaTables = new(StringComparer.OrdinalIgnoreCase)
    {
        NetsTableNames.NetworkEnvironment,
        NetsTableNames.NetworkTopology,
        NetsTableNames.DeviceDomain,
        NetsTableNames.DeviceGroup,
        NetsTableNames.DeviceType,
        NetsTableNames.DeviceNic,
        NetsTableNames.PortPurpose,
        NetsTableNames.OuiEntry,
        NetsTableNames.OuiMeta,
        NetsTableNames.Device,
    };

    private static readonly Regex ColumnNamePattern = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    internal static string RequireKnownTableName(string table)
    {
        if (!KnownSchemaTables.Contains(table))
        {
            throw new InvalidOperationException($"未知表名：{table}");
        }

        return table;
    }

    internal static string RequireColumnName(string column)
    {
        if (string.IsNullOrWhiteSpace(column) || !ColumnNamePattern.IsMatch(column))
        {
            throw new InvalidOperationException($"无效列名：{column}");
        }

        return column;
    }

    internal static string RequireMySqlDatabaseName(string database)
    {
        if (string.IsNullOrWhiteSpace(database) || database.Length > 64)
        {
            throw new ScanException("MySQL 数据库名称无效。");
        }

        if (database.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '_' and not '$'))
        {
            throw new ScanException("MySQL 数据库名称无效。");
        }

        return database;
    }

    internal static string BuildMySqlCreateDatabaseSql(string database)
    {
        var validated = RequireMySqlDatabaseName(database);
        var escaped = validated.Replace("`", "``", StringComparison.Ordinal);
        return string.Concat(
            "CREATE DATABASE IF NOT EXISTS `",
            escaped,
            "` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
    }

    internal static string ResolvePragmaTableInfoSql(string table) =>
        RequireKnownTableName(table) switch
        {
            NetsTableNames.NetworkEnvironment => "PRAGMA table_info(devtoy_network_environment)",
            NetsTableNames.NetworkTopology => "PRAGMA table_info(devtoy_network_topology)",
            NetsTableNames.DeviceDomain => "PRAGMA table_info(devtoy_device_domain)",
            NetsTableNames.DeviceGroup => "PRAGMA table_info(devtoy_device_group)",
            NetsTableNames.DeviceType => "PRAGMA table_info(devtoy_device_type)",
            NetsTableNames.DeviceNic => "PRAGMA table_info(devtoy_device_nic)",
            NetsTableNames.PortPurpose => "PRAGMA table_info(devtoy_port_purpose)",
            NetsTableNames.OuiEntry => "PRAGMA table_info(devtoy_oui_entry)",
            NetsTableNames.OuiMeta => "PRAGMA table_info(devtoy_oui_meta)",
            NetsTableNames.Device => "PRAGMA table_info(devtoy_device)",
            _ => throw new InvalidOperationException($"未知表名：{table}"),
        };
}