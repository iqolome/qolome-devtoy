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

    public const string IconBoxKey = "DevtoyGlyphIconBox";

    public const string TextStyleKey = "DevtoyGlyphTextStyle";

    public const string IconStyleKey = "DevtoyGlyphIconStyle";

    /// <summary>界面 Symbol 与配套文字统一字号。</summary>
    public const double DefaultFontSize = 14;

    /// <summary>SymbolIcon 未缩放时的近似设计尺寸（用于 ScaleTransform 回退）。</summary>
    private const double SymbolIntrinsic = 16;

    public static double FontSize { get; private set; } = DefaultFontSize;

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

        if (Application.Current?.Resources is not ResourceDictionary resources)
        {
            return;
        }

        resources[FontSizeKey] = font;
        resources[IconBoxKey] = font;
        resources[TextStyleKey] = CreateTextStyle(font);
        resources[IconStyleKey] = CreateIconStyle();

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

    private static Style CreateTextStyle(double fontSize)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, fontSize));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style CreateIconStyle()
    {
        // 不设 Width/Height：由 Viewbox / FontSize / ScaleTransform 负责缩放
        var style = new Style(typeof(SymbolIcon));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        return style;
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
        else if (node is FontIcon fontIcon)
        {
            fontIcon.FontSize = FontSize;
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
                fi.FontSize = FontSize;
            }
            else if (child is TextBlock tb)
            {
                tb.FontSize = FontSize;
            }
        }
    }
}
