namespace Qolome_Devtoy_App.Helpers;

/// <summary>表格列宽统一约定（复选框 / 序号 / 操作 / 日期时间）。</summary>
public static class TableColumnMetrics
{
    public const int MaxHeaderChars = 8;

    /// <summary>与界面正文默认字号对齐（见 <c>DevtoyGlyphFontSize</c>）。</summary>
    public const double ReferenceFontSize = 14;

    /// <summary>约 8 个汉字宽；仅用于可选的列宽上限。</summary>
    public static double MaxColumnWidth { get; } = MaxHeaderChars * ReferenceFontSize;

    public static double MinColumnWidth { get; } = ReferenceFontSize * 2.5;

    /// <summary>列内容左右各预留。</summary>
    public const double SidePad = 5;

    /// <summary>MinWidth=0 时 CheckBox 可视约宽。</summary>
    public const double CheckboxContentWidth = 20;

    /// <summary>复选框列：复选框宽 + 左右各 5px。</summary>
    public static double CheckColumnWidth { get; } = CheckboxContentWidth + SidePad * 2;

    /// <summary>序号按 4 位数字估算（等宽近似）。</summary>
    public const int IndexDigitCount = 4;

    /// <summary>序号列：4 位数字宽 + 左右各 5px。</summary>
    public static double IndexColumnWidth { get; } =
        IndexDigitCount * ReferenceFontSize * 0.62 + SidePad * 2;

    /// <summary>操作列一行最多完整显示的按钮数。</summary>
    public const int ActionButtonsPerRow = 5;

    /// <summary>单个紧凑操作按钮约占宽（图标+短文案+内边距）。</summary>
    public const double ActionButtonSlotWidth = 68;

    public const double ActionButtonSpacing = 4;

    /// <summary>操作列最大宽度：5 个按钮 + 间距；超出换行。</summary>
    public static double ActionColumnMaxWidth { get; } = ActionColumnWidthFor(ActionButtonsPerRow);

    /// <summary>按按钮个数估算操作列宽（一行排满、含间距）。</summary>
    public static double ActionColumnWidthFor(int buttonCount)
    {
        var n = Math.Clamp(buttonCount, 1, ActionButtonsPerRow);
        return n * ActionButtonSlotWidth + (n - 1) * ActionButtonSpacing;
    }

    /// <summary>日期时间完整宽（Consolas 12 的 <c>yyyy-MM-dd HH:mm</c>）。</summary>
    public static double DateTimeColumnWidth { get; } = 16 * 7.4 + SidePad * 2;

    /// <summary>仅日期 <c>yyyy-MM-dd</c> 最小宽，挤窄时可换行成「日期 / 时间」两行。</summary>
    public static double DateTimeColumnMinWidth { get; } = 10 * 7.4 + SidePad * 2;

    public static ColumnSpec CheckColumnSpec() =>
        new(0, CheckColumnWidth, CheckColumnWidth, CapToHeaderMax: false);

    public static ColumnSpec IndexColumnSpec() =>
        new(0, IndexColumnWidth, IndexColumnWidth, CapToHeaderMax: false);

    public static ColumnSpec DateTimeColumnSpec() =>
        new(0, DateTimeColumnMinWidth, DateTimeColumnWidth, CapToHeaderMax: false);

    /// <param name="buttonCount">该表操作列实际按钮数；默认按上限 5 个预留。</param>
    public static ColumnSpec ActionColumnSpec(int buttonCount = ActionButtonsPerRow)
    {
        var width = ActionColumnWidthFor(buttonCount);
        return new(0, width, width, CapToHeaderMax: false);
    }

    /// <summary>按表头字数估算列宽，夹在 Min~Max 之间。</summary>
    public static double WidthForHeader(string? headerText)
    {
        if (string.IsNullOrWhiteSpace(headerText))
        {
            return MinColumnWidth;
        }

        var chars = 0.0;
        foreach (var ch in headerText.Trim())
        {
            chars += ch <= 0x7F ? 0.55 : 1.0;
        }

        return Math.Clamp(chars * ReferenceFontSize, MinColumnWidth, MaxColumnWidth);
    }
}
