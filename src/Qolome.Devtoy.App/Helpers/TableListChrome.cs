using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 表格/列表统一视觉：奇偶行、列分割线；行线/选中/悬停由 <c>DevtoyTableListViewItemStyle</c> 承担。
/// </summary>
public static class TableListChrome
{
    private static readonly object ColumnLinesToken = new();

    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(TableListChrome),
        new PropertyMetadata(false, OnEnableChanged));

    public static readonly DependencyProperty ColumnLinesProperty = DependencyProperty.RegisterAttached(
        "ColumnLines",
        typeof(bool),
        typeof(TableListChrome),
        new PropertyMetadata(false, OnColumnLinesChanged));

    public static void SetEnable(DependencyObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) =>
        (bool)element.GetValue(EnableProperty);

    public static void SetColumnLines(DependencyObject element, bool value) =>
        element.SetValue(ColumnLinesProperty, value);

    public static bool GetColumnLines(DependencyObject element) =>
        (bool)element.GetValue(ColumnLinesProperty);

    /// <summary>代码创建的 ListView 也可调用。</summary>
    public static void Attach(ListViewBase list, bool columnLines = true)
    {
        SetEnable(list, true);
        if (columnLines)
        {
            SetColumnLines(list, true);
        }
    }

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list)
        {
            return;
        }

        list.ContainerContentChanging -= OnContainerContentChanging;
        if (e.NewValue is true)
        {
            ApplyListChrome(list);
            list.ContainerContentChanging += OnContainerContentChanging;
        }
    }

    private static void OnColumnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Grid grid && e.NewValue is true)
        {
            EnsureGridColumnLines(grid);
            grid.Loaded -= OnHeaderGridLoaded;
            grid.Loaded += OnHeaderGridLoaded;
            return;
        }

        if (d is ListViewBase list && e.NewValue is true)
        {
            list.ContainerContentChanging -= OnContainerContentChanging;
            list.ContainerContentChanging += OnContainerContentChanging;
        }
    }

    private static void OnHeaderGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            EnsureGridColumnLines(grid);
        }
    }

    private static void ApplyListChrome(ListViewBase list)
    {
        list.BorderBrush ??= ResolveBrush(list, "DevtoyTableBorderBrush")
            ?? ResolveThemeBrush(list, "CardStrokeColorDefaultBrush");
        if (list.BorderThickness == new Thickness(0))
        {
            list.BorderThickness = new Thickness(1);
        }

        // 列表级覆盖选中 / 悬停色，不影响全局导航列表
        EnsureResource(list, "ListViewItemBackgroundPointerOver", "DevtoyTableRowPointerOverBrush");
        EnsureResource(list, "ListViewItemBackgroundSelected", "DevtoyTableRowSelectedBrush");
        EnsureResource(list, "ListViewItemBackgroundSelectedPointerOver", "DevtoyTableRowSelectedPointerOverBrush");
        EnsureResource(list, "ListViewItemBackgroundSelectedPressed", "DevtoyTableRowSelectedBrush");
        EnsureResource(list, "ListViewItemForegroundSelected", "DevtoyTableRowSelectedForegroundBrush");
        EnsureResource(list, "ListViewItemForegroundPointerOver", "DevtoyTableRowForegroundBrush");
    }

    private static void EnsureResource(FrameworkElement host, string targetKey, string sourceKey)
    {
        if (host.Resources.ContainsKey(targetKey))
        {
            return;
        }

        var brush = ResolveBrush(host, sourceKey);
        if (brush is not null)
        {
            host.Resources[targetKey] = brush;
        }
    }

    private static void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem item)
        {
            return;
        }

        if (GetEnable(sender) && UiPerfFlags.RichTableChrome)
        {
            ApplyZebra(sender, item, args.ItemIndex);
        }

        if (GetColumnLines(sender) && UiPerfFlags.RichTableChrome && args.Phase == 0)
        {
            args.RegisterUpdateCallback(static (_, a) =>
            {
                if (!UiPerfFlags.RichTableChrome)
                {
                    return;
                }

                if (a.ItemContainer is ListViewItem { ContentTemplateRoot: Grid row })
                {
                    EnsureGridColumnLines(row);
                }
            });
        }
    }

    private static void ApplyZebra(ListViewBase list, ListViewItem item, int index)
    {
        if (index < 0 || !UiPerfFlags.RichTableChrome)
        {
            return;
        }

        var even = index % 2 == 0;
        var brush = ResolveBrush(
                list,
                even ? "DevtoyTableRowEvenBrush" : "DevtoyTableRowOddBrush")
            ?? new SolidColorBrush(even
                ? Color.FromArgb(0, 0, 0, 0)
                : Color.FromArgb(18, 128, 128, 128));

        item.Background = brush;
    }

    private static void EnsureGridColumnLines(Grid grid)
    {
        if (!UiPerfFlags.RichTableChrome || grid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        if (grid.Children.OfType<Rectangle>().Any(r => Equals(r.Tag, ColumnLinesToken)))
        {
            return;
        }

        var brush = ResolveBrush(grid, "DevtoyTableGridLineBrush")
            ?? ResolveThemeBrush(grid, "DividerStrokeColorDefaultBrush")
            ?? new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));

        var rowSpan = Math.Max(1, grid.RowDefinitions.Count);
        for (var col = 0; col < grid.ColumnDefinitions.Count - 1; col++)
        {
            var line = new Rectangle
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Fill = brush,
                IsHitTestVisible = false,
                Tag = ColumnLinesToken,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetColumn(line, col);
            Grid.SetRowSpan(line, rowSpan);
            Canvas.SetZIndex(line, 10);
            grid.Children.Add(line);
        }
    }

    private static Brush? ResolveBrush(FrameworkElement host, string key)
    {
        if (host.Resources.TryGetValue(key, out var local) && local is Brush b1)
        {
            return b1;
        }

        try
        {
            if (Application.Current.Resources[key] is Brush app)
            {
                return app;
            }
        }
        catch
        {
            // theme key may be absent
        }

        return null;
    }

    private static Brush? ResolveThemeBrush(FrameworkElement host, string key)
    {
        try
        {
            if (Application.Current.Resources[key] is Brush app)
            {
                return app;
            }
        }
        catch
        {
            // ignore
        }

        return host.Resources.TryGetValue(key, out var local) && local is Brush b2 ? b2 : null;
    }
}
