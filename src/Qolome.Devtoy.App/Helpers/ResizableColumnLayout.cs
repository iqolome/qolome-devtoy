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
        _minWidths = specs.Select(s => s.MinWidth).ToArray();
    }

    public static ResizableColumnLayout Attach(
        Grid headerGrid,
        ListViewBase listView,
        IReadOnlyList<ColumnSpec> specs,
        double columnSpacing = 8)
    {
        var layout = new ResizableColumnLayout(specs.ToArray(), columnSpacing);
        layout.AttachCore(headerGrid, listView);
        return layout;
    }

    private void AttachCore(Grid headerGrid, ListViewBase listView)
    {
        _header = headerGrid;
        _list = listView;
        _measureHost = headerGrid.Parent as FrameworkElement ?? headerGrid;
        _measureHost.SizeChanged += OnMeasureHostSizeChanged;
        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) => RefreshAllRows());
        _list.Loaded += (_, _) => ApplyAll();
        BuildGripOverlay();
        TryMeasure();
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
        if (_applying || _measureHost is null || _measureHost.ActualWidth <= 0)
        {
            return;
        }

        var before = _widths.ToArray();
        if (!_measured)
        {
            DistributeInitial(_measureHost.ActualWidth);
            _measured = true;
        }
        else if (_dragIndex < 0)
        {
            BalanceToWidth(_measureHost.ActualWidth);
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
                _widths[i] = Math.Max(spec.MinWidth, px);
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
            _widths[i] = Math.Max(spec.MinWidth, share);
        }
    }

    private void BalanceToWidth(double totalWidth)
    {
        var columnCount = _widths.Length;
        var gaps = _columnSpacing * Math.Max(0, columnCount - 1);
        var available = Math.Max(0, totalWidth - gaps);
        var sum = _widths.Sum();
        // 滞后阈值：避免滚动条出现/消失导致 available 在临界值附近抖动引发死循环
        if (sum <= 0 || Math.Abs(sum - available) < 24)
        {
            return;
        }

        if (sum < available)
        {
            _widths[^1] += available - sum;
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
            grid.ColumnDefinitions[i].Width = new GridLength(_widths[i], GridUnitType.Pixel);
            grid.ColumnDefinitions[i].MinWidth = _minWidths[i];
        }
    }
}

public sealed record ColumnSpec(double StarWeight, double MinWidth, double? InitialPixels = null);