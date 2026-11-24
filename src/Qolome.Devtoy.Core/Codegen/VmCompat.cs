using System.Collections;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>NVelocity 上下文中的 Java 风格字符串（toUpperCase / equals）。</summary>
public sealed class VmString
{
    private readonly string _value;

    public VmString(string? value) => _value = value ?? "";

    public string toUpperCase() => _value.ToUpperInvariant();

    public string toLowerCase() => _value.ToLowerInvariant();

    public bool equals(object? other) =>
        string.Equals(_value, other?.ToString(), StringComparison.Ordinal);

    public override string ToString() => _value;

    public static implicit operator string(VmString value) => value._value;
}

/// <summary>提供 size()，供模板 $list.size() 与 foreach 适配使用。</summary>
public sealed class VmList : ArrayList
{
    public VmList()
    {
    }

    public VmList(IEnumerable items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    public int size() => Count;
}

/// <summary>对齐 Velocity $null.isNull(...)；须为实例方法供 NVelocity 反射调用。</summary>
public sealed class VmNullTool
{
#pragma warning disable CA1822, S2325 // NVelocity $null.isNull 需要实例方法
    public bool isNull(object? value) => value is null;
#pragma warning restore CA1822, S2325
}

/// <summary>对齐 mapperAnnotationClass.name / simpleName。</summary>
public sealed class VmAnnotationClass
{
    public VmAnnotationClass(string fullName)
    {
        name = fullName;
        var dot = fullName.LastIndexOf('.');
        simpleName = dot < 0 ? fullName : fullName[(dot + 1)..];
    }

    public string name { get; }

    public string simpleName { get; }
}
