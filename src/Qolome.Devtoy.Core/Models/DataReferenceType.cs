namespace Qolome.Devtoy.Core.Models;

/// <summary>qolome_dict_item.data_reference_type 整型字典，与 Java DataReferenceType 对齐。</summary>
public enum DataReferenceType
{
    Byte = 1,
    Short = 2,
    Integer = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    Character = 7,
    Boolean = 8,
    Date = 9,
    LocalDateTime = 10,
    List = 11,
    String = 12,
}

public static class DataReferenceTypeHelper
{
    public static string ToTypeCode(DataReferenceType type) => type switch
    {
        DataReferenceType.Byte => "Byte",
        DataReferenceType.Short => "Short",
        DataReferenceType.Integer => "Integer",
        DataReferenceType.Long => "Long",
        DataReferenceType.Float => "Float",
        DataReferenceType.Double => "Double",
        DataReferenceType.Character => "Character",
        DataReferenceType.Boolean => "Boolean",
        DataReferenceType.Date => "Date",
        DataReferenceType.LocalDateTime => "LocalDateTime",
        DataReferenceType.List => "List",
        DataReferenceType.String => "String",
        _ => type.ToString(),
    };

    public static string ResolveReferenceCode(object? raw)
    {
        if (raw is null || raw is DBNull)
        {
            return "";
        }

        var value = raw switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            _ => int.TryParse(raw.ToString(), out var parsed) ? parsed : (int?)null,
        };

        if (value is null)
        {
            return raw.ToString() ?? "";
        }

        if (Enum.IsDefined(typeof(DataReferenceType), value.Value))
        {
            return ToTypeCode((DataReferenceType)value.Value);
        }

        return value.Value.ToString();
    }
}