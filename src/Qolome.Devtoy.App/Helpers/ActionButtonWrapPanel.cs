using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 操作列按钮容器：横向排列，超出 <see cref="TableColumnMetrics.ActionColumnMaxWidth"/> 时换行。
/// </summary>
public sealed class ActionButtonWrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(ActionButtonWrapPanel),
        new PropertyMetadata(TableColumnMetrics.ActionButtonSpacing, OnLayoutPropertyChanged));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActionButtonWrapPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var spacing = Spacing;
        var maxWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? TableColumnMetrics.ActionColumnMaxWidth
            : Math.Min(availableSize.Width, TableColumnMetrics.ActionColumnMaxWidth);

        double x = 0;
        double y = 0;
        double rowHeight = 0;
        double usedWidth = 0;

        foreach (UIElement child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > maxWidth)
            {
                usedWidth = Math.Max(usedWidth, x - spacing);
                x = 0;
                y += rowHeight + spacing;
                rowHeight = 0;
            }

            rowHeight = Math.Max(rowHeight, size.Height);
            x += size.Width + spacing;
        }

        usedWidth = Math.Max(usedWidth, Math.Max(0, x - spacing));
        var height = y + rowHeight;
        return new Size(
            Math.Min(usedWidth, maxWidth),
            double.IsInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var spacing = Spacing;
        var maxWidth = Math.Min(finalSize.Width, TableColumnMetrics.ActionColumnMaxWidth);
        if (maxWidth <= 0)
        {
            maxWidth = TableColumnMetrics.ActionColumnMaxWidth;
        }

        double x = 0;
        double y = 0;
        double rowHeight = 0;

        foreach (UIElement child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > maxWidth)
            {
                x = 0;
                y += rowHeight + spacing;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            rowHeight = Math.Max(rowHeight, size.Height);
            x += size.Width + spacing;
        }

        return finalSize;
    }
}
