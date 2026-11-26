using CommunityToolkit.WinUI.Behaviors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Xaml.Interactivity;
using Windows.UI;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 表格/列表统一视觉：奇偶行、列分割线、表头冻结；行线/选中/悬停由 <c>DevtoyTableListViewItemStyle</c> 承担。
/// </summary>
public static class TableListChrome
{
    private static readonly object ColumnLinesToken = new();
    private static readonly object StickyHeaderToken = new();

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
        list.Loaded -= OnListLoadedForStickyHeader;
        if (e.NewValue is true)
        {
            ApplyListChrome(list);
            list.ContainerContentChanging += OnContainerContentChanging;
            list.Loaded += OnListLoadedForStickyHeader;
            list.RegisterPropertyChangedCallback(
                ListViewBase.HeaderProperty,
                static (d, _) =>
                {
                    if (d is ListViewBase lv)
                    {
                        EnsureStickyHeader(lv);
                    }
                });
            EnsureStickyHeader(list);
        }
    }

    private static void OnListLoadedForStickyHeader(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewBase list)
        {
            EnsureStickyHeader(list);
        }
    }

    /// <summary>ListView.Header 滚动时保持置顶（CommunityToolkit StickyHeaderBehavior）。</summary>
    private static void EnsureStickyHeader(ListViewBase list)
    {
        if (list.Header is not FrameworkElement header)
        {
            return;
        }

        var behaviors = Interaction.GetBehaviors(header);
        if (behaviors.Any(b => Equals(b.GetValue(FrameworkElement.TagProperty), StickyHeaderToken)
            || b is StickyHeaderBehavior))
        {
            return;
        }

        var sticky = new StickyHeaderBehavior();
        sticky.SetValue(FrameworkElement.TagProperty, StickyHeaderToken);
        behaviors.Add(sticky);
    }

    private static void OnColumnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Grid grid && e.NewValue is true)
        {
            ApplyAdaptiveColumnCaps(grid, isHeader: false);
            EnsureGridColumnLines(grid);
            EnsureHorizontalScrollExtent(grid);
            grid.Loaded -= OnHeaderGridLoaded;
            grid.Loaded += OnHeaderGridLoaded;
            grid.SizeChanged -= OnHeaderGridSizeChangedForScroll;
            grid.SizeChanged += OnHeaderGridSizeChangedForScroll;
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
            ApplyAdaptiveColumnCaps(grid, isHeader: true);
            EnsureGridColumnLines(grid);
            EnsureHorizontalScrollExtent(grid);
        }
    }

    private static void OnHeaderGridSizeChangedForScroll(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid grid && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 0.5)
        {
            EnsureHorizontalScrollExtent(grid);
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

        // 列宽合计超出视口时出横向滚动条（页面显式 Disabled 的除外，如外层已包 ScrollViewer）
        if (ScrollViewer.GetHorizontalScrollMode(list) != ScrollMode.Disabled)
        {
            ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Enabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        }

        // 列表级覆盖选中 / 悬停色，不影响全局导航列表
        EnsureResource(list, "ListViewItemBackgroundPointerOver", "DevtoyTableRowPointerOverBrush");
        EnsureResource(list, "ListViewItemBackgroundSelected", "DevtoyTableRowSelectedBrush");
        EnsureResource(list, "ListViewItemBackgroundSelectedPointerOver", "DevtoyTableRowSelectedPointerOverBrush");
        EnsureResource(list, "ListViewItemBackgroundSelectedPressed", "DevtoyTableRowSelectedBrush");
        EnsureResource(list, "ListViewItemForegroundSelected", "DevtoyTableRowSelectedForegroundBrush");
        EnsureResource(list, "ListViewItemForegroundPointerOver", "DevtoyTableRowForegroundBrush");
    }

    /// <summary>
    /// 按列定义推算内容最小宽度，避免被视口压窄后裁切且无横向滚动。
    /// </summary>
    public static void EnsureHorizontalScrollExtent(Grid grid)
    {
        // ListView 已禁用横向滚动（改由外层 ScrollViewer）时，勿再给行/表头单独撑 MinWidth，
        // 否则会与宿主 MinWidth 叠加，造成表头与表体错位。
        if (FindAncestorListView(grid) is { } list
            && ScrollViewer.GetHorizontalScrollMode(list) == ScrollMode.Disabled)
        {
            return;
        }

        if (grid.ColumnDefinitions.Count == 0)
        {
            return;
        }

        var sum = 0d;
        foreach (var col in grid.ColumnDefinitions)
        {
            if (col.Width.IsAbsolute && col.Width.Value > 0)
            {
                sum += col.Width.Value;
            }
            else if (col.MinWidth > 0)
            {
                sum += col.MinWidth;
            }
            else
            {
                sum += TableColumnMetrics.MinColumnWidth;
            }
        }

        sum += grid.ColumnSpacing * Math.Max(0, grid.ColumnDefinitions.Count - 1);
        if (sum <= 1)
        {
            return;
        }

        if (Math.Abs(grid.MinWidth - sum) > 0.5)
        {
            grid.MinWidth = sum;
        }

        // 表头外层常带 Padding，一并撑开，否则滚动范围仍不够
        if (grid.Parent is FrameworkElement host)
        {
            var pad = host switch
            {
                Grid g => g.Padding,
                Border b => b.Padding,
                Control c => c.Padding,
                _ => new Thickness(0),
            };
            var hostMin = sum + pad.Left + pad.Right;
            if (Math.Abs(host.MinWidth - hostMin) > 0.5)
            {
                host.MinWidth = hostMin;
            }
        }
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

        // 多选表：仅点行首复选框才改选中；点行其它区域不勾选
        if (sender.SelectionMode is ListViewSelectionMode.Multiple or ListViewSelectionMode.Extended)
        {
            item.PointerPressed -= OnItemPointerPressedForCheckOnly;
            item.PointerPressed += OnItemPointerPressedForCheckOnly;
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
                    ApplyAdaptiveColumnCaps(row, isHeader: false);
                    EnsureGridColumnLines(row);
                    EnsureHorizontalScrollExtent(row);
                }
            });
        }
    }

    /// <summary>
    /// 拦截行点击选中；命中「行首选择框」（无 Content 的 CheckBox）时放行。
    /// 带文字的功能开关复选框不放行，避免误选中整行。
    /// </summary>
    private static void OnItemPointerPressedForCheckOnly(object sender, PointerRoutedEventArgs e)
    {
        if (IsHitOnRowSelectCheckBox(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
    }

    private static bool IsHitOnRowSelectCheckBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is CheckBox box)
            {
                return box.Content is null;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    /// <summary>
    /// 表头文字：列宽足够时单行；仅当列被挤窄时按词换行。
    /// 不设 MaxWidth（避免有空间仍被卡成竖排）；不改表体单元格。
    /// </summary>
    private static void ApplyAdaptiveColumnCaps(Grid grid, bool isHeader)
    {
        if (!isHeader)
        {
            return;
        }

        foreach (var child in grid.Children)
        {
            if (child is not FrameworkElement fe || IsActionColumnHost(fe))
            {
                continue;
            }

            if (fe is TextBlock tb)
            {
                ApplyHeaderTextPreferSingleLine(tb);
                continue;
            }

            // 排序按钮：Content 为纯 TextBlock 时可处理；StackPanel（图标+文字）不钻入
            if (fe is Button button && IsSortHeaderButton(button) && button.Content is TextBlock sortTb)
            {
                ApplyHeaderTextPreferSingleLine(sortTb);
            }
            else if (fe is HyperlinkButton link && link.Content is TextBlock linkTb)
            {
                ApplyHeaderTextPreferSingleLine(linkTb);
            }
            else if (fe is Panel panel)
            {
                foreach (var nested in panel.Children.OfType<TextBlock>())
                {
                    ApplyHeaderTextPreferSingleLine(nested);
                }

                foreach (var nestedLink in panel.Children.OfType<HyperlinkButton>())
                {
                    if (nestedLink.Content is TextBlock nestedTb)
                    {
                        ApplyHeaderTextPreferSingleLine(nestedTb);
                    }
                }
            }
        }
    }

    private static void ApplyHeaderTextPreferSingleLine(TextBlock tb)
    {
        // 清掉历史 MaxWidth=112，否则列再宽也会提前换行
        tb.ClearValue(FrameworkElement.MaxWidthProperty);
        tb.TextWrapping = TextWrapping.WrapWholeWords;
        tb.TextTrimming = TextTrimming.CharacterEllipsis;
        if (tb.FontWeight.Weight < 600)
        {
            tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
    }

    /// <summary>行内操作列（编辑/删除等）；排序表头按钮不算操作列。</summary>
    private static bool IsActionColumnHost(FrameworkElement fe)
    {
        if (fe is Button button)
        {
            return !IsSortHeaderButton(button);
        }

        if (fe is AppBarButton or ToggleButton)
        {
            return true;
        }

        if (fe is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Button b && !IsSortHeaderButton(b))
                {
                    return true;
                }

                if (child is AppBarButton or ToggleButton)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Style? _sortHeaderStyle;

    private static bool IsSortHeaderButton(Button button)
    {
        if (button.Style is null)
        {
            return false;
        }

        _sortHeaderStyle ??= Application.Current.Resources.TryGetValue("SortHeaderButtonStyle", out var o)
            && o is Style s
                ? s
                : null;
        if (_sortHeaderStyle is null)
        {
            return false;
        }

        for (var style = button.Style; style is not null; style = style.BasedOn)
        {
            if (ReferenceEquals(style, _sortHeaderStyle))
            {
                return true;
            }
        }

        return false;
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

    private static ListViewBase? FindAncestorListView(DependencyObject start)
    {
        for (var current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListViewBase list)
            {
                return list;
            }
        }

        return null;
    }
}
