using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 全局图标/文字尺寸（固定与文字同字号）。
/// SymbolIcon 是字体字形，设 Width/Height 只会裁切；须用 Viewbox 或 ScaleTransform 缩放。
/// </summary>
public static class UiGlyphResources
{
    public const string FontSizeKey = "DevtoyGlyphFontSize";

    public const string FluentIconFontSizeKey = "DevtoyFluentIconFontSize";

    public const string IconBoxKey = "DevtoyGlyphIconBox";

    public const string TextStyleKey = "DevtoyGlyphTextStyle";

    public const string IconStyleKey = "DevtoyGlyphIconStyle";

    /// <summary>界面 Symbol 与配套文字统一字号。</summary>
    public const double DefaultFontSize = 14;

    /// <summary>Fluent System Icons 侧栏字号（比 Segoe 同字号视觉更小）。</summary>
    public const double DefaultFluentIconFontSize = 18;

    /// <summary>SymbolIcon 未缩放时的近似设计尺寸（用于 ScaleTransform 回退）。</summary>
    private const double SymbolIntrinsic = 16;

    public static double FontSize { get; private set; } = DefaultFontSize;

    public static double FluentIconFontSize { get; private set; } = DefaultFluentIconFontSize;

    /// <summary>图标目标边长，与文字字号一致。</summary>
    public static double IconBox { get; private set; } = DefaultFontSize;

    public static double IconBoxFor(double fontSize) =>
        fontSize > 0 ? fontSize : DefaultFontSize;

    /// <summary>按固定字号应用缩放（启动时调用一次即可）。</summary>
    public static void Apply() => Apply(DefaultFontSize);

    public static void Apply(double glyphFontSize)
    {
        var font = IconBoxFor(glyphFontSize);
        FontSize = font;
        IconBox = font;
        FluentIconFontSize = Math.Max(DefaultFluentIconFontSize, font + 4);

        // 字号资源已在 App.xaml 定义；启动时不再写入 ResourceDictionary，
        // 避免 WinRT IMap.Insert 的 first-chance / 0x80004005 打断启动。
        if (App.TryGetWindow()?.Content is DependencyObject root)
        {
            RefreshDescendants(root);
        }
    }

    /// <summary>可缩放的 Symbol 图标（Viewbox 包裹，避免裁切）。</summary>
    public static Viewbox CreateScaledSymbol(Symbol symbol, double? size = null)
    {
        var side = size ?? IconBox;
        return new Viewbox
        {
            Width = side,
            Height = side,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new SymbolIcon { Symbol = symbol },
        };
    }

    /// <summary>可缩放的 FontIcon（Segoe Fluent Icons 字形）。</summary>
    public static Viewbox CreateScaledFontIcon(string glyph, double? size = null)
    {
        var side = size ?? IconBox;
        return new Viewbox
        {
            Width = side,
            Height = side,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
            },
        };
    }

    private static void RefreshDescendants(DependencyObject node)
    {
        if (node is Viewbox { Child: SymbolIcon } viewbox)
        {
            viewbox.Width = IconBox;
            viewbox.Height = IconBox;
        }
        else if (node is SymbolIcon icon)
        {
            ApplySymbolSize(icon);
        }
        else if (node is BitmapIcon bitmapIcon)
        {
            SvgPathIconFactory.ApplySize(bitmapIcon);
        }
        else if (node is IconSourceElement iconSource)
        {
            SvgPathIconFactory.ApplySize(iconSource);
        }
        else if (node is PathIcon pathIcon)
        {
            SvgPathIconFactory.ApplySize(pathIcon);
        }
        else if (node is FontIcon fontIcon)
        {
            fontIcon.FontSize = IsFluentSystemIcon(fontIcon) ? FluentIconFontSize : FontSize;
        }

        if (node is TextBlock text && IsGlyphCompanionText(text))
        {
            text.FontSize = FontSize;
        }

        if (node is Button button && button.Content is StackPanel stack)
        {
            ApplyToGlyphStack(stack);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            RefreshDescendants(VisualTreeHelper.GetChild(node, i));
        }
    }

    private static void ApplySymbolSize(SymbolIcon icon)
    {
        icon.ClearValue(FrameworkElement.WidthProperty);
        icon.ClearValue(FrameworkElement.HeightProperty);

        if (icon.Parent is Viewbox parentBox)
        {
            parentBox.Width = IconBox;
            parentBox.Height = IconBox;
            icon.RenderTransform = null;
            return;
        }

        if (TryWrapInViewbox(icon))
        {
            return;
        }

        // NavigationViewItem.Icon 等宿主要求 IconElement，不能包 Viewbox，用等比缩放
        var scale = IconBox / SymbolIntrinsic;
        icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        icon.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
    }

    private static bool TryWrapInViewbox(SymbolIcon icon)
    {
        if (icon.Parent is not Panel panel)
        {
            return false;
        }

        var index = panel.Children.IndexOf(icon);
        if (index < 0)
        {
            return false;
        }

        panel.Children.RemoveAt(index);
        var box = new Viewbox
        {
            Width = IconBox,
            Height = IconBox,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon,
        };
        panel.Children.Insert(index, box);
        icon.RenderTransform = null;
        return true;
    }

    private static bool IsFluentSystemIcon(FontIcon icon)
    {
        var name = icon.FontFamily?.Source ?? string.Empty;
        return name.Contains("FluentSystemIcons", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGlyphCompanionText(TextBlock text)
    {
        if (text.Parent is not StackPanel { Orientation: Orientation.Horizontal } stack)
        {
            return false;
        }

        return stack.Children.Any(c =>
            c is SymbolIcon
            || c is FontIcon
            || c is Viewbox { Child: SymbolIcon });
    }

    private static void ApplyToGlyphStack(StackPanel stack)
    {
        for (var i = 0; i < stack.Children.Count; i++)
        {
            var child = stack.Children[i];
            if (child is Viewbox { Child: SymbolIcon } vb)
            {
                vb.Width = IconBox;
                vb.Height = IconBox;
            }
            else if (child is SymbolIcon sym)
            {
                stack.Children.RemoveAt(i);
                stack.Children.Insert(i, CreateScaledSymbol(sym.Symbol, IconBox));
            }
            else if (child is FontIcon fi)
            {
                fi.FontSize = IsFluentSystemIcon(fi) ? FluentIconFontSize : FontSize;
            }
            else if (child is TextBlock tb)
            {
                tb.FontSize = FontSize;
            }
        }
    }
}
