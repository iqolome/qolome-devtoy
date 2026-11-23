namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

public static class JdbcTableLister
{
    private static readonly HashSet<string> MySqlSystem = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATABASECHANGELOG",
        "DATABASECHANGELOGLOCK",
    };

    public static IReadOnlyList<TableListItem> ListTables(DataSourceSettings dataSource)
    {
        using var conn = MySqlConnectionFactory.Open(dataSource);
        var list = new List<TableListItem>();
        const string sql = """
            SELECT TABLE_NAME, TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME
            """;
        using var cmd = new MySqlConnector.MySqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (IsSystemTable(name))
            {
                continue;
            }

            var comment = reader.IsDBNull(1) ? "" : reader.GetString(1);
            list.Add(new TableListItem
            {
                Name = name,
                Comment = comment ?? "",
            });
        }

        return list;
    }

    private static bool IsSystemTable(string name) =>
        name.StartsWith("QRTZ_", StringComparison.OrdinalIgnoreCase)
        || MySqlSystem.Contains(name);
}
