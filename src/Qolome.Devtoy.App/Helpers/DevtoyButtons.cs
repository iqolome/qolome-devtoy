using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>统一为按钮设置图标 + 文字内容（图标与文字视觉对齐）。</summary>
public static class DevtoyButtons
{
    /// <summary>配套文字字号（pt）；默认见 <see cref="UiGlyphResources.DefaultFontSize"/>。</summary>
    public static double GlyphFontSize => UiGlyphResources.FontSize;

    /// <summary>图标目标边长（与字号一致，经 Viewbox 缩放）。</summary>
    public static double GlyphIconBox => UiGlyphResources.IconBox;

    [Obsolete("使用 GlyphFontSize")]
    public static double DefaultIconSize => GlyphFontSize;

    [Obsolete("使用 GlyphIconBox")]
    public static double CompactIconSize => GlyphIconBox;

    public static void SetGlyph(Button button, string label, Symbol symbol, double? fontSize = null)
    {
        button.Content = BuildContent(label, symbol, fontSize);
    }

    public static Button Create(
        string label,
        Symbol symbol,
        Thickness? padding = null,
        double? fontSize = null)
    {
        var button = new Button
        {
            Padding = padding ?? new Thickness(12, 6, 12, 6),
        };
        SetGlyph(button, label, symbol, fontSize);
        return button;
    }

    public static StackPanel BuildContent(string label, Symbol symbol, double? fontSize = null) =>
        BuildContent(label, UiGlyphResources.CreateScaledSymbol(symbol, ResolveIconBox(fontSize)), fontSize);

    /// <summary>图标为 Segoe Fluent Icons 字形（如排序升降箭头）。</summary>
    public static StackPanel BuildContentWithGlyph(string label, string glyph, double? fontSize = null) =>
        BuildContent(label, UiGlyphResources.CreateScaledFontIcon(glyph, ResolveIconBox(fontSize)), fontSize);

    private static StackPanel BuildContent(string label, UIElement icon, double? fontSize)
    {
        var textSize = fontSize ?? UiGlyphResources.FontSize;
        var text = new TextBlock
        {
            Text = label,
            FontSize = textSize,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                icon,
                text,
            },
        };
    }

    private static double ResolveIconBox(double? fontSize) =>
        fontSize is null ? UiGlyphResources.IconBox : UiGlyphResources.IconBoxFor(fontSize.Value);

    /// <summary>可缩放 Symbol 图标（Viewbox）。</summary>
    public static UIElement CreateSymbolIcon(Symbol symbol, double? box = null) =>
        UiGlyphResources.CreateScaledSymbol(symbol, box);
}
