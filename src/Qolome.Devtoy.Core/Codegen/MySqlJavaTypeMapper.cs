namespace Qolome.Devtoy.Core.Codegen;

/// <summary>MySQL 列类型 → Java 属性类型（对齐 MySqlTypeConvert 常用映射）。</summary>
public static class MySqlJavaTypeMapper
{
    public static (string PropertyType, string JdbcType, IReadOnlyList<string> Imports) Map(ColumnMetadata column)
    {
        var dataType = column.DataType.ToLowerInvariant();
        var columnType = column.ColumnType.ToLowerInvariant();
        return dataType switch
        {
            "bigint" => ("Long", "BIGINT", Array.Empty<string>()),
            "int" or "integer" or "mediumint" => ("Integer", "INTEGER", Array.Empty<string>()),
            "smallint" => ("Integer", "SMALLINT", Array.Empty<string>()),
            "tinyint" when columnType is "tinyint(1)" => ("Boolean", "BIT", Array.Empty<string>()),
            "tinyint" => ("Integer", "TINYINT", Array.Empty<string>()),
            "bit" => ("Boolean", "BIT", Array.Empty<string>()),
            "decimal" or "numeric" or "dec" => ("BigDecimal", "DECIMAL", new[] { "java.math.BigDecimal" }),
            "double" or "double precision" => ("Double", "DOUBLE", Array.Empty<string>()),
            "float" or "real" => ("Float", "REAL", Array.Empty<string>()),
            "date" => ("LocalDate", "DATE", new[] { "java.time.LocalDate" }),
            "datetime" or "timestamp" => ("LocalDateTime", "TIMESTAMP", new[] { "java.time.LocalDateTime" }),
            "time" => ("LocalTime", "TIME", new[] { "java.time.LocalTime" }),
            "blob" or "binary" or "varbinary" or "longblob" or "mediumblob" or "tinyblob" =>
                ("byte[]", "BLOB", Array.Empty<string>()),
            "json" => ("String", "VARCHAR", Array.Empty<string>()),
            _ => ("String", "VARCHAR", Array.Empty<string>()),
        };
    }
}
