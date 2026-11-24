namespace Qolome_Devtoy_App.Helpers;

/// <summary>图标库列表项：字形 + 可选 Symbol 枚举。</summary>
public sealed class IconLibraryItem
{
    public IconLibraryItem(SegoeFluentIconCatalog.Entry entry)
    {
        Entry = entry;
        SymbolName = SymbolEnumMap.TryGetSymbolName(entry.Name, entry.CodePoint);
    }

    public SegoeFluentIconCatalog.Entry Entry { get; }

    public string Name => Entry.Name;

    public string Glyph => Entry.Glyph;

    public string Unicode => Entry.Unicode;

    public string CsharpEscape => Entry.CsharpEscape;

    public string? SymbolName { get; }

    public bool HasSymbol => SymbolName is not null;

    public string SymbolBadge => HasSymbol ? "Symbol" : string.Empty;

    /// <summary>有枚举时复制 Symbol.Xxx，否则复制 \uXXXX。</summary>
    public string PreferredCopyText =>
        HasSymbol ? $"Symbol.{SymbolName}" : CsharpEscape;

    public string Tooltip =>
        HasSymbol
            ? $"{Name} · {Unicode} · Symbol.{SymbolName}（点击优先复制枚举）"
            : $"{Name} · {Unicode}（点击复制 {CsharpEscape}）";
}
