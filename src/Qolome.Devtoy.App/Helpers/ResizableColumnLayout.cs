using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>表头冻结 + 列宽拖动：绑定表头 <see cref="Grid"/> 与 <see cref="ListViewBase"/> 行内网格。</summary>
public sealed class ResizableColumnLayout
{
    private readonly ColumnSpec[] _specs;
    private readonly double _columnSpacing;
    private readonly double[] _widths;
    private readonly double[] _minWidths;

    private Grid? _header;
    private FrameworkElement? _measureHost;
    private FrameworkElement? _widthHost;
    private ListViewBase? _list;
    private Grid? _gripOverlay;
    private bool _measured;
    private bool _applying;
    private int _dragIndex = -1;
    private double _dragStartX;
    private double[] _dragSnapshot = [];

    private ResizableColumnLayout(ColumnSpec[] specs, double columnSpacing)
    {
        _specs = specs;
        _columnSpacing = columnSpacing;
        _widths = new double[specs.Length];
        _minWidths = specs.Select(s =>
        {
            var min = s.MinWidth;
            if (s.CapToHeaderMax)
            {
                min = Math.Min(min, TableColumnMetrics.MaxColumnWidth);
            }

            return Math.Max(TableColumnMetrics.MinColumnWidth * 0.5, min);
        }).ToArray();
    }

    private double MaxFor(int index)
    {
        var spec = _specs[index];
        if (!spec.CapToHeaderMax)
        {
            return double.PositiveInfinity;
        }

        return TableColumnMetrics.MaxColumnWidth;
    }

    /// <param name="widthHost">
    /// 外层横向滚动容器内的宿主（通常为包住 ListView 的 Grid）。
    /// 传入后关闭 ListView 自身横向滚动，并把 MinWidth 同步为列宽合计，避免 WinUI
    /// ItemsStackPanel 在 HorizontalScroll 下表头/行测量不一致导致错位。
    /// </param>
    public static ResizableColumnLayout Attach(
        Grid headerGrid,
        ListViewBase listView,
        IReadOnlyList<ColumnSpec> specs,
        double columnSpacing = 8,
        FrameworkElement? widthHost = null)
    {
        var layout = new ResizableColumnLayout(specs.ToArray(), columnSpacing);
        layout.AttachCore(headerGrid, listView, widthHost);
        return layout;
    }

    private void AttachCore(Grid headerGrid, ListViewBase listView, FrameworkElement? widthHost)
    {
        _header = headerGrid;
        _list = listView;
        _widthHost = widthHost ?? listView.Parent as FrameworkElement;
        // 以表头列网格自身宽度为准（勿用外层带 Padding 的父级，否则列宽会偏大导致错位）
        _measureHost = headerGrid;
        _measureHost.SizeChanged += OnMeasureHostSizeChanged;
        if (_widthHost is not null && !ReferenceEquals(_widthHost, _measureHost))
        {
            _widthHost.SizeChanged += OnMeasureHostSizeChanged;
        }

        if (_widthHost is not null)
        {
            ScrollViewer.SetHorizontalScrollMode(listView, ScrollMode.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Disabled);
        }

        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) => RefreshAllRows());
        _list.Loaded += (_, _) => ApplyAll();
        BuildGripOverlay();
        TryMeasure();
    }

    private double GetContentWidth()
    {
        // 外层横向滚动时，以 ListView 实际宽度为列分配基准，保证表头/表体同一套宽度
        if (_widthHost is not null && _list is not null && _list.ActualWidth > 0)
        {
            var pad = GetHeaderHorizontalPadding();
            return Math.Max(0, _list.ActualWidth - pad - _list.BorderThickness.Left - _list.BorderThickness.Right);
        }

        if (_header is null || _header.ActualWidth <= 0)
        {
            return 0;
        }

        return Math.Max(
            0,
            _header.ActualWidth - _header.Padding.Left - _header.Padding.Right);
    }

    private double GetHeaderHorizontalPadding()
    {
        if (_list?.Header is FrameworkElement headerRoot)
        {
            return headerRoot switch
            {
                Grid g => g.Padding.Left + g.Padding.Right,
                Border b => b.Padding.Left + b.Padding.Right,
                Control c => c.Padding.Left + c.Padding.Right,
                _ => 24,
            };
        }

        return 24;
    }

    private double CurrentTotalContentWidth()
    {
        if (_widths.Length == 0)
        {
            return 0;
        }

        return _widths.Sum() + _columnSpacing * Math.Max(0, _widths.Length - 1);
    }

    private void SyncWidthHostMinWidth()
    {
        if (_widthHost is null)
        {
            return;
        }

        var min = CurrentTotalContentWidth() + GetHeaderHorizontalPadding();
        if (min > 1 && Math.Abs(_widthHost.MinWidth - min) > 0.5)
        {
            _widthHost.MinWidth = min;
        }
    }

    private void BuildGripOverlay()
    {
        if (_header?.Parent is not Panel headerRow)
        {
            return;
        }

        // 勿给 overlay 设 Transparent 背景，否则会吞掉整行点击（排序按钮无响应）。
        // 无 Background 时空白区域穿透；仅右侧 grip 捕获拖拽。
        _gripOverlay = new Grid
        {
            ColumnSpacing = _header.ColumnSpacing,
            IsHitTestVisible = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Canvas.SetZIndex(_gripOverlay, 20);
        headerRow.Children.Add(_gripOverlay);

        for (var i = 0; i < _specs.Length - 1; i++)
        {
            var index = i;
            var grip = new Border
            {
                Width = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = index,
                IsHitTestVisible = true,
            };
            grip.PointerEntered += (_, _) =>
            {
                grip.Background = Application.Current.Resources.TryGetValue(
                    "DividerStrokeColorDefaultBrush",
                    out var brush)
                    ? brush as Brush
                    : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(80, 128, 128, 128));
            };
            grip.PointerExited += (_, _) =>
            {
                if (_dragIndex != index)
                {
                    grip.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            };
            grip.PointerPressed += OnGripPointerPressed;
            grip.PointerMoved += OnGripPointerMoved;
            grip.PointerReleased += OnGripPointerReleased;
            grip.PointerCanceled += OnGripPointerReleased;
            Grid.SetColumn(grip, i);
            _gripOverlay.Children.Add(grip);
        }
    }

    private void OnMeasureHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 自身 ApplyAll 引发的细微宽度抖动忽略，避免 SizeChanged↔ApplyAll 死循环卡死 UI
        if (_applying || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5)
        {
            return;
        }

        TryMeasure();
    }

    private void TryMeasure()
    {
        var contentWidth = GetContentWidth();
        if (_applying || contentWidth <= 0)
        {
            return;
        }

        var before = _widths.ToArray();
        if (!_measured)
        {
            DistributeInitial(contentWidth);
            _measured = true;
        }
        else if (_dragIndex < 0)
        {
            BalanceToWidth(contentWidth);
        }

        if (_measured && WidthsEqual(before, _widths) && _dragIndex < 0)
        {
            return;
        }

        ApplyAll();
    }

    private static bool WidthsEqual(double[] a, double[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > 0.5)
            {
                return false;
            }
        }

        return true;
    }

    private void DistributeInitial(double totalWidth)
    {
        var columnCount = _specs.Length;
        var gaps = _columnSpacing * Math.Max(0, columnCount - 1);
        var available = Math.Max(0, totalWidth - gaps);
        var fixedSum = 0d;
        var flexWeight = 0d;

        for (var i = 0; i < columnCount; i++)
        {
            var spec = _specs[i];
            if (spec.InitialPixels is double px)
            {
                _widths[i] = Math.Min(Math.Max(_minWidths[i], px), MaxFor(i));
                fixedSum += _widths[i];
            }
            else
            {
                flexWeight += Math.Max(0, spec.StarWeight);
            }
        }

        var flexSpace = Math.Max(0, available - fixedSum);
        for (var i = 0; i < columnCount; i++)
        {
            var spec = _specs[i];
            if (spec.InitialPixels is not null)
            {
                continue;
            }

            var weight = Math.Max(0, spec.StarWeight);
            var share = flexWeight > 0 ? flexSpace * (weight / flexWeight) : flexSpace / Math.Max(1, columnCount);
            var width = Math.Max(_minWidths[i], share);
            _widths[i] = Math.Min(width, MaxFor(i));
        }

        ClampAllToMax();
    }

    private void ClampAllToMax()
    {
        for (var i = 0; i < _widths.Length; i++)
        {
            var max = MaxFor(i);
            if (_widths[i] > max)
            {
                _widths[i] = Math.Max(_minWidths[i], max);
            }
        }
    }

    private void BalanceToWidth(double totalWidth)
    {
        var columnCount = _widths.Length;
        var gaps = _columnSpacing * Math.Max(0, columnCount - 1);
        var available = Math.Max(0, totalWidth - gaps);
        var minTotal = _minWidths.Sum();
        var sum = _widths.Sum();
        // 滞后带宽：避免滚动条出现/消失导致 available 在临界值附近抖动引发死循环
        if (sum <= 0 || Math.Abs(sum - available) < 24)
        {
            return;
        }

        if (sum < available)
        {
            // 不再把余量全部塞进最后一列（会撑破「约 8 字」上限）；有余量则留白
            ClampAllToMax();
            return;
        }

        // 列最小宽合计已超过视口：不再强行压窄，交给横向滚动
        if (minTotal + gaps >= available - 0.5)
        {
            for (var i = 0; i < _widths.Length; i++)
            {
                _widths[i] = Math.Max(_widths[i], _minWidths[i]);
            }

            ClampAllToMax();
            return;
        }

        // 超出可用宽度时优先压缩弹性列（StarWeight>0），固定列（如操作）尽量保底
        var overflow = sum - available;
        ShrinkColumns(overflow, flexOnly: true);
        overflow = _widths.Sum() - available;
        if (overflow > 0.5)
        {
            ShrinkColumns(overflow, flexOnly: false);
        }

        ClampAllToMax();
    }

    private void ShrinkColumns(double overflow, bool flexOnly)
    {
        if (overflow <= 0.5)
        {
            return;
        }

        var indices = new List<int>();
        for (var i = 0; i < _widths.Length; i++)
        {
            if (flexOnly && _specs[i].StarWeight <= 0)
            {
                continue;
            }

            if (_widths[i] > _minWidths[i] + 0.5)
            {
                indices.Add(i);
            }
        }

        if (indices.Count == 0)
        {
            return;
        }

        var shrinkable = indices.Sum(i => _widths[i] - _minWidths[i]);
        if (shrinkable <= 0)
        {
            return;
        }

        var take = Math.Min(overflow, shrinkable);
        foreach (var i in indices)
        {
            var room = _widths[i] - _minWidths[i];
            _widths[i] -= take * (room / shrinkable);
        }
    }

    private void OnGripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement grip || grip.Tag is not int index || _header is null)
        {
            return;
        }

        _dragIndex = index;
        _dragStartX = e.GetCurrentPoint(_header).Position.X;
        _dragSnapshot = _widths.ToArray();
        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragIndex < 0 || sender is not UIElement || _header is null)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(_header).Position.X;
        var delta = currentX - _dragStartX;
        var left = _dragIndex;
        var right = _dragIndex + 1;
        var newLeft = _dragSnapshot[left] + delta;
        var newRight = _dragSnapshot[right] - delta;
        var leftMax = MaxFor(left);
        var rightMax = MaxFor(right);
        if (newLeft > leftMax)
        {
            newRight += newLeft - leftMax;
            newLeft = leftMax;
        }

        if (newRight > rightMax)
        {
            newLeft += newRight - rightMax;
            newRight = rightMax;
        }

        if (newLeft < _minWidths[left])
        {
            newRight -= _minWidths[left] - newLeft;
            newLeft = _minWidths[left];
        }

        if (newRight < _minWidths[right])
        {
            newLeft -= _minWidths[right] - newRight;
            newRight = _minWidths[right];
            newLeft = Math.Max(_minWidths[left], newLeft);
        }

        _widths[left] = newLeft;
        _widths[right] = newRight;
        ApplyAll();
        e.Handled = true;
    }

    private void OnGripPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragIndex < 0)
        {
            return;
        }

        if (sender is Border grip)
        {
            grip.ReleasePointerCapture(e.Pointer);
            grip.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        _dragIndex = -1;
        e.Handled = true;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            return;
        }

        if (args.Phase == 0)
        {
            args.RegisterUpdateCallback(OnContainerContentChanging);
            return;
        }

        if (args.ItemContainer.ContentTemplateRoot is Grid row)
        {
            ApplyToGrid(row);
        }
    }

    private void RefreshAllRows()
    {
        if (_list is null)
        {
            return;
        }

        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.ContainerFromIndex(i) is ListViewItem { ContentTemplateRoot: Grid row })
            {
                ApplyToGrid(row);
            }
        }
    }

    private void ApplyAll()
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            if (_header is not null)
            {
                ApplyToGrid(_header);
            }

            SyncGripColumns();
            RefreshAllRows();
            SyncWidthHostMinWidth();
        }
        finally
        {
            _applying = false;
        }
    }

    private void SyncGripColumns()
    {
        if (_gripOverlay is null || _header is null)
        {
            return;
        }

        _gripOverlay.ColumnSpacing = _header.ColumnSpacing;
        while (_gripOverlay.ColumnDefinitions.Count < _header.ColumnDefinitions.Count)
        {
            _gripOverlay.ColumnDefinitions.Add(new ColumnDefinition());
        }

        while (_gripOverlay.ColumnDefinitions.Count > _header.ColumnDefinitions.Count)
        {
            _gripOverlay.ColumnDefinitions.RemoveAt(_gripOverlay.ColumnDefinitions.Count - 1);
        }

        for (var i = 0; i < _header.ColumnDefinitions.Count; i++)
        {
            var src = _header.ColumnDefinitions[i];
            var dst = _gripOverlay.ColumnDefinitions[i];
            if (dst.Width != src.Width)
            {
                dst.Width = src.Width;
            }

            if (Math.Abs(dst.MinWidth - src.MinWidth) > 0.1)
            {
                dst.MinWidth = src.MinWidth;
            }
        }
    }

    private void ApplyToGrid(Grid grid)
    {
        if (_widths.Length == 0)
        {
            return;
        }

        grid.ColumnSpacing = _columnSpacing;
        while (grid.ColumnDefinitions.Count < _widths.Length)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        while (grid.ColumnDefinitions.Count > _widths.Length)
        {
            grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
        }

        for (var i = 0; i < _widths.Length; i++)
        {
            var w = _widths[i];
            // MinWidth 必须等于当前列宽：若仍用规格下限，表头在窄视口内会被 Grid 压窄，
            // 而行在横向滚动测量下保持完整宽，造成表头/表体错位。
            grid.ColumnDefinitions[i].Width = new GridLength(w, GridUnitType.Pixel);
            grid.ColumnDefinitions[i].MinWidth = w;
            var max = MaxFor(i);
            // 外层横向滚动模式下锁死列宽；内置横向滚动时仍允许上限约束
            if (_widthHost is not null)
            {
                grid.ColumnDefinitions[i].MaxWidth = w;
            }
            else
            {
                grid.ColumnDefinitions[i].MaxWidth = double.IsInfinity(max) ? double.PositiveInfinity : Math.Max(max, w);
            }
        }

        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        // 外层滚动宿主负责 MinWidth；勿再给行/表头单独撑 MinWidth，以免与 ListView 测量打架
        if (_widthHost is null)
        {
            TableListChrome.EnsureHorizontalScrollExtent(grid);
        }
        else
        {
            grid.ClearValue(FrameworkElement.MinWidthProperty);
        }
    }
}

/// <param name="CapToHeaderMax">
/// 为 true 时列宽不超过约 8 个汉字（仅适合短标签列）；数据列/操作列保持 false。
/// </param>
public sealed record ColumnSpec(
    double StarWeight,
    double MinWidth,
    double? InitialPixels = null,
    bool CapToHeaderMax = false);