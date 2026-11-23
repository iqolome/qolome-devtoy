namespace Qolome.Devtoy.Core.Codegen;

public sealed class TableMetadata
{
    public required string Name { get; init; }

    public string Comment { get; init; } = "";

    public List<ColumnMetadata> Columns { get; init; } = new();
}

public sealed class ColumnMetadata
{
    public required string Name { get; init; }

    public string Comment { get; init; } = "";

    public required string DataType { get; init; }

    public string ColumnType { get; init; } = "";

    public bool Nullable { get; init; }

    public bool PrimaryKey { get; init; }

    public bool AutoIncrement { get; init; }

    public string? Extra { get; init; }
}
