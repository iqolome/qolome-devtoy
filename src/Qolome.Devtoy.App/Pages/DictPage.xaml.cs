using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Qolome_Devtoy_App.Helpers;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class DictPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;
    private readonly List<DictItemRecord> _items = [];

    private string _sortKey = "ItemCode";
    private bool _sortAscending = true;

    private Func<Task>? _reloadValuesAction;
    private ValueListUiSession? _valueListUi;

    private ResizableColumnLayout? _columnLayout;
    private bool _suppressCheckSync;
    private readonly HashSet<long> _selectedValueIds = [];

    public DictPage()
    {
        InitializeComponent();
        UpdateSortHeaders();
        Loaded += OnDictPageLoaded;
    }

    private void OnDictPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_columnLayout is not null)
        {
            return;
        }

        _columnLayout = ResizableColumnLayout.Attach(
            DictHeaderGrid,
            DictList,
            [
                TableColumnMetrics.CheckColumnSpec(),
                TableColumnMetrics.IndexColumnSpec(),
                new ColumnSpec(1.6, 96),
                new ColumnSpec(2.0, 96),
                new ColumnSpec(0, 56, 72),
                // 枚举 / 启用：表头 + 排序箭头，略宽于单字「是」
                new ColumnSpec(0, 56, 72),
                new ColumnSpec(0, 56, 72),
                TableColumnMetrics.DateTimeColumnSpec(),
                TableColumnMetrics.DateTimeColumnSpec(),
                // 生成 / 值列表 / 编辑 / 删除：4 钮刚好
                TableColumnMetrics.ActionColumnSpec(4),
            ],
            widthHost: DictTableHost);
    }

    private async void OnQuery(object sender, RoutedEventArgs e) => await ReloadItemsAsync().ConfigureAwait(true);

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        _ = ReloadItemsAsync();
    }

    private async void OnAddItem(object sender, RoutedEventArgs e)
    {
        var draft = new DictItemRecord();
        if (!await TryEditItemDialogAsync(draft, isEdit: false).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在创建字典项…", async () =>
        {
            var id = await _session.CreateDictItemAsync(draft).ConfigureAwait(true);
            if (id > 0)
            {
                await ReloadItemsAsync(showBusy: false).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async void OnGenerateItem(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
        {
            return;
        }

        var modules = ModuleCatalogFilter.ForDictTargets(_session.Modules).ToList();
        if (modules.Count == 0)
        {
            _session.LogWarn("无可用目标项目：请先在「项目管理」为 Java 启用「生成枚举」或为 Web 启用「生成字典」");
            return;
        }

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemsSource = modules,
            DisplayMemberPath = nameof(ModuleEntry.TargetModuleDisplay),
            Height = 280,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (Application.Current.Resources.TryGetValue("DevtoyTableListViewStyle", out var tableStyle)
            && tableStyle is Style style)
        {
            list.Style = style;
        }
        else
        {
            TableListChrome.Attach(list, columnLines: false);
        }
        var selectedModule = _session.SelectedModule;
        if (selectedModule is not null)
        {
            foreach (var m in modules.Where(m =>
                         string.Equals(m.ModulePath, selectedModule.ModulePath, StringComparison.OrdinalIgnoreCase)))
            {
                list.SelectedItems.Add(m);
            }
        }

        if (list.SelectedItems.Count == 0 && modules.Count > 0)
        {
            list.SelectedItems.Add(modules[0]);
        }

        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(new TextBlock
        {
            Text = "目标项目（可多选；按各项目已启用的枚举/字典分别生成）",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        form.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = "生成 " + item.ItemCode,
            Content = form,
            PrimaryButtonText = "生成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selected = list.SelectedItems.OfType<ModuleEntry>().ToList();
        if (selected.Count == 0)
        {
            _session.LogWarn("请至少选择一个目标项目");
            return;
        }

        await RunBusyAsync("正在生成字典代码…", async () =>
        {
            await _session.GenerateDictForItemToModulesAsync(item, selected).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async void OnShowValues(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
        {
            return;
        }

        await ShowValueListDialogAsync(item).ConfigureAwait(true);
    }

    private async void OnEditItem(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
        {
            return;
        }

        var snapshot = CloneItem(item);
        if (!await TryEditItemDialogAsync(snapshot, isEdit: true).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在更新字典项…", async () =>
        {
            if (await _session.UpdateDictItemAsync(snapshot).ConfigureAwait(true))
            {
                await ReloadItemsAsync(showBusy: false).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private void OnItemCodeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DictItemRecord item })
        {
            return;
        }

        e.Handled = true;
        var code = item.ItemCode?.Trim() ?? "";
        if (code.Length == 0)
        {
            _session.LogWarn("编码为空，无法复制");
            return;
        }

        var text = $"<dict-code>{code}</dict-code>";
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        AppMessage.Success($"复制成功：{text}");
    }

    private async void OnDeleteItem(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item)
        {
            return;
        }

        if (!await ConfirmDeleteAsync(
                "删除字典项",
                $"确定逻辑删除字典项？\n编码：{item.ItemCode}\n名称：{item.ItemName}",
                $"此操作不可撤销，确认删除？\n编码：{item.ItemCode}\n名称：{item.ItemName}").ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在删除字典项…", async () =>
        {
            if (await _session.DeleteDictItemAsync(item.ItemId).ConfigureAwait(true))
            {
                await ReloadItemsAsync(showBusy: false).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async void OnDeleteSelectedItems(object sender, RoutedEventArgs e)
    {
        var selected = DictList.SelectedItems.OfType<DictItemRecord>().ToList();
        if (selected.Count == 0)
        {
            _session.LogWarn("请先勾选要删除的字典项");
            return;
        }

        var preview = selected.Count <= 8
            ? string.Join("、", selected.Select(i => i.ItemCode))
            : string.Join("、", selected.Take(8).Select(i => i.ItemCode)) + $" 等 {selected.Count} 项";
        if (!await ConfirmDeleteAsync(
                "删除字典项",
                selected.Count == 1
                    ? $"确定逻辑删除字典项？\n编码：{selected[0].ItemCode}\n名称：{selected[0].ItemName}"
                    : $"确定逻辑删除选中的 {selected.Count} 个字典项？\n{preview}",
                selected.Count == 1
                    ? $"此操作不可撤销，确认删除？\n编码：{selected[0].ItemCode}"
                    : $"此操作不可撤销，确认删除选中的 {selected.Count} 项？").ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在删除字典项…", async () =>
        {
            foreach (var item in selected)
            {
                await _session.DeleteDictItemAsync(item.ItemId).ConfigureAwait(true);
            }

            await ReloadItemsAsync(showBusy: false).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void OnDictSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ListViewCheckOnlySelection.IsProgrammatic(DictList))
        {
            return;
        }

        SyncHeaderSelectAll();
        SyncVisibleRowChecks();
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync || sender is not CheckBox box || !ReferenceEquals(box, HeaderSelectAll))
        {
            return;
        }

        ListViewCheckOnlySelection.RunProgrammatic(DictList, () =>
        {
            if (box.IsChecked == true)
            {
                DictList.SelectedItems.Clear();
                foreach (var item in DictList.Items)
                {
                    DictList.SelectedItems.Add(item);
                }
            }
            else if (box.IsChecked == false)
            {
                DictList.SelectedItems.Clear();
            }
        });
    }

    private void OnRowCheckLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: DictItemRecord item } box)
        {
            return;
        }

        ListViewCheckOnlySelection.Attach(DictList);
        _suppressCheckSync = true;
        box.IsChecked = DictList.SelectedItems.Contains(item);
        _suppressCheckSync = false;
    }

    private void OnRowCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync || sender is not CheckBox { Tag: DictItemRecord item } box)
        {
            return;
        }

        ListViewCheckOnlySelection.RunProgrammatic(DictList, () =>
        {
            if (box.IsChecked == true)
            {
                if (!DictList.SelectedItems.Contains(item))
                {
                    DictList.SelectedItems.Add(item);
                }
            }
            else
            {
                DictList.SelectedItems.Remove(item);
            }
        });
    }

    private void SyncHeaderSelectAll()
    {
        if (HeaderSelectAll is null)
        {
            return;
        }

        _suppressCheckSync = true;
        var total = DictList.Items.Count;
        var selected = DictList.SelectedItems.Count;
        HeaderSelectAll.IsChecked = total == 0
            ? false
            : selected == 0
                ? false
                : selected >= total
                    ? true
                    : null;
        _suppressCheckSync = false;
    }

    private void SyncVisibleRowChecks()
    {
        _suppressCheckSync = true;
        for (var i = 0; i < DictList.Items.Count; i++)
        {
            if (DictList.ContainerFromIndex(i) is not ListViewItem container)
            {
                continue;
            }

            if (FindRowCheckBox(container) is { } box && DictList.Items[i] is DictItemRecord item)
            {
                box.IsChecked = DictList.SelectedItems.Contains(item);
            }
        }

        _suppressCheckSync = false;
    }

    private static CheckBox? FindRowCheckBox(DependencyObject root)
    {
        if (root is CheckBox { Tag: DictItemRecord } box)
        {
            return box;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindRowCheckBox(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async void OnEditValue(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictValueRow value } || _valueListUi is null)
        {
            return;
        }

        var snapshot = CloneValue(value);
        if (!await _valueListUi.EditAsync(snapshot, true).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在更新字典值…", async () =>
        {
            if (await _session.UpdateDictValueAsync(snapshot).ConfigureAwait(true))
            {
                await _valueListUi.ReloadAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async void OnDeleteValue(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictValueRow value } || _valueListUi is null)
        {
            return;
        }

        if (!await _valueListUi.ConfirmDeleteAsync(
                "删除字典值",
                $"确定逻辑删除字典值「{value.ValCode}」？",
                $"再次确认删除值「{value.ValName}」（{value.ValValue}）？").ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在删除字典值…", async () =>
        {
            if (await _session.DeleteDictValueAsync(value.ValId).ConfigureAwait(true))
            {
                await _valueListUi.ReloadAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async Task ShowValueListDialogAsync(DictItemRecord item)
    {
        var valuesPanel = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var listLayer = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var shell = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        shell.Children.Add(listLayer);

        Border? overlay = null;

        void ClearOverlay()
        {
            if (overlay is not null)
            {
                shell.Children.Remove(overlay);
                overlay = null;
            }

            listLayer.Visibility = Visibility.Visible;
        }

        async Task<bool> ShowOverlayAsync(string title, FrameworkElement body, string primaryText, Func<bool>? validate = null)
        {
            ClearOverlay();
            var tcs = new TaskCompletionSource<bool>();
            var panel = new StackPanel { Spacing = 12, MaxWidth = 480 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
            });
            panel.Children.Add(body);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var cancel = new Button { Content = "取消", MinWidth = 88 };
            var ok = new Button { Content = primaryText, MinWidth = 88 };
            cancel.Click += (_, _) =>
            {
                ClearOverlay();
                tcs.TrySetResult(false);
            };
            ok.Click += (_, _) =>
            {
                if (validate is not null && !validate())
                {
                    return;
                }

                ClearOverlay();
                tcs.TrySetResult(true);
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            overlay = new Border
            {
                Background = ResolveOverlayBackground(),
                Padding = new Thickness(4, 0, 4, 0),
                Child = panel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            listLayer.Visibility = Visibility.Collapsed;
            shell.Children.Add(overlay);
            return await tcs.Task.ConfigureAwait(true);
        }

        async Task<bool> EditInOverlayAsync(DictValueRow value, bool isEdit)
        {
            var form = BuildValueEditForm(value);
            return await ShowOverlayAsync(
                isEdit ? "编辑字典值" : "新增字典值",
                form.Panel,
                "保存",
                () =>
                {
                    if (!form.TryApply())
                    {
                        return false;
                    }

                    return true;
                }).ConfigureAwait(true);
        }

        async Task<bool> ConfirmInOverlayAsync(string title, string firstMessage, string secondMessage)
        {
            if (!await ShowOverlayAsync(title, new TextBlock { Text = firstMessage, TextWrapping = TextWrapping.WrapWholeWords }, "删除")
                    .ConfigureAwait(true))
            {
                return false;
            }

            return await ShowOverlayAsync(
                    "再次确认",
                    new TextBlock { Text = secondMessage, TextWrapping = TextWrapping.WrapWholeWords },
                    "删除")
                .ConfigureAwait(true);
        }

        async Task ReloadValuesAsync()
        {
            var loaded = await _session.ListDictValuesAsync(item.ItemId).ConfigureAwait(true);
            valuesPanel.Children.Clear();
            _selectedValueIds.Clear();
            var index = 1;
            foreach (var row in loaded)
            {
                row.RowIndex = index++;
                valuesPanel.Children.Add(BuildValueRow(row));
            }
        }

        _reloadValuesAction = ReloadValuesAsync;
        _valueListUi = new ValueListUiSession
        {
            EditAsync = EditInOverlayAsync,
            ConfirmDeleteAsync = ConfirmInOverlayAsync,
            ReloadAsync = ReloadValuesAsync,
        };

        await RunBusyAsync("正在加载字典值…", ReloadValuesAsync).ConfigureAwait(true);

        var addBtn = DevtoyButtons.Create("新增字典值", Symbol.Add);
        addBtn.Click += async (_, _) =>
        {
            var draft = new DictValueRow { ItemId = item.ItemId, Enabled = true };
            if (!await EditInOverlayAsync(draft, isEdit: false).ConfigureAwait(true))
            {
                return;
            }

            await RunBusyAsync("正在创建字典值…", async () =>
            {
                var id = await _session.CreateDictValueAsync(draft).ConfigureAwait(true);
                if (id > 0)
                {
                    await ReloadValuesAsync().ConfigureAwait(true);
                }
            }).ConfigureAwait(true);
        };

        var deleteSelectedBtn = DevtoyButtons.Create("删除选中", Symbol.Delete);
        deleteSelectedBtn.Click += async (_, _) =>
        {
            if (_selectedValueIds.Count == 0)
            {
                _session.LogWarn("请先勾选要删除的字典值");
                return;
            }

            var count = _selectedValueIds.Count;
            if (!await ConfirmInOverlayAsync(
                    "删除字典值",
                    $"确定删除选中的 {count} 个字典值？",
                    $"此操作不可撤销，确认删除选中的 {count} 项？").ConfigureAwait(true))
            {
                return;
            }

            var ids = _selectedValueIds.ToList();
            await RunBusyAsync("正在删除字典值…", async () =>
            {
                foreach (var id in ids)
                {
                    await _session.DeleteDictValueAsync(id).ConfigureAwait(true);
                }

                await ReloadValuesAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.Add(addBtn);
        toolbar.Children.Add(deleteSelectedBtn);

        var header = CreateValueColumnsGrid();
        header.Padding = new Thickness(0, 0, 0, 6);
        header.Opacity = 0.85;
        var headerSelectAll = new CheckBox
        {
            MinWidth = 0,
            IsThreeState = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(headerSelectAll, "全选 / 取消全选");
        headerSelectAll.Checked += (_, _) => SetAllValueChecks(valuesPanel, headerSelectAll, true);
        headerSelectAll.Unchecked += (_, _) => SetAllValueChecks(valuesPanel, headerSelectAll, false);
        AddCell(header, headerSelectAll, 0);
        AddHeaderCell(header, "序号", 1);
        AddHeaderCell(header, "值", 2);
        AddHeaderCell(header, "编码", 3);
        AddHeaderCell(header, "名称", 4);
        AddHeaderCell(header, "启用", 5);
        AddHeaderCell(header, "排序", 6);
        AddHeaderCell(header, "操作", 7);

        var tableWidth = MeasureValueColumnsWidth();
        var tableStack = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = tableWidth,
            MinWidth = tableWidth,
        };
        tableStack.Children.Add(header);
        tableStack.Children.Add(valuesPanel);

        var scroll = new ScrollViewer
        {
            Content = tableStack,
            MaxHeight = 420,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        listLayer.Children.Add(new TextBlock
        {
            Text = $"{item.ItemCode} · {item.ItemName}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        listLayer.Children.Add(toolbar);
        listLayer.Children.Add(scroll);

        var dialog = new ContentDialog
        {
            Title = "字典值列表",
            Content = shell,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
        };
        // WinUI ContentDialog 忽略控件 Min/MaxWidth，须改资源，否则默认 ~548 裁掉操作列
        dialog.Resources["ContentDialogMinWidth"] = 920.0;
        dialog.Resources["ContentDialogMaxWidth"] = 1120.0;
        await dialog.ShowWithIconsAsync();
        _reloadValuesAction = null;
        _valueListUi = null;
        _selectedValueIds.Clear();
    }

    /// <summary>列顺序：…名称 / 启用 / 排序 / 操作。</summary>
    private static Grid CreateValueColumnsGrid()
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = MeasureValueColumnsWidth(),
            MinWidth = MeasureValueColumnsWidth(),
        };
        foreach (var width in ValueColumnWidths())
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(width),
                MinWidth = width,
                MaxWidth = width,
            });
        }

        return grid;
    }

    private static double[] ValueColumnWidths()
    {
        var actionWidth = TableColumnMetrics.ActionColumnWidthFor(2);
        return
        [
            TableColumnMetrics.CheckColumnWidth,
            TableColumnMetrics.IndexColumnWidth,
            88,
            112,
            112,
            48, // 启用
            48, // 排序
            actionWidth, // 操作
        ];
    }

    private static double MeasureValueColumnsWidth()
    {
        var widths = ValueColumnWidths();
        return widths.Sum() + 8 * Math.Max(0, widths.Length - 1);
    }

    private static Brush ResolveOverlayBackground()
    {
        try
        {
            if (Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out var o)
                && o is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // ignore
        }

        return new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    private sealed class ValueListUiSession
    {
        public required Func<DictValueRow, bool, Task<bool>> EditAsync { get; init; }

        public required Func<string, string, string, Task<bool>> ConfirmDeleteAsync { get; init; }

        public required Func<Task> ReloadAsync { get; init; }
    }

    private readonly record struct ValueEditForm(StackPanel Panel, Func<bool> TryApply);

    private ValueEditForm BuildValueEditForm(DictValueRow value)
    {
        var val = new TextBox { Text = value.ValValue, PlaceholderText = "val_value" };
        var code = new TextBox { Text = value.ValCode, PlaceholderText = "val_code" };
        var name = new TextBox { Text = value.ValName, PlaceholderText = "val_name" };
        var enumName = new TextBox { Text = value.ValEnum ?? "", PlaceholderText = "val_enum（可选）" };
        var seq = new TextBox { Text = value.Seq.ToString(), PlaceholderText = "排序" };
        var enabled = new CheckBox { Content = "启用", IsChecked = value.Enabled };

        var form = new StackPanel { Spacing = 10, MinWidth = 400 };
        form.Children.Add(Labeled("字典值", val));
        form.Children.Add(Labeled("值编码", code));
        form.Children.Add(Labeled("值名称", name));
        form.Children.Add(Labeled("枚举名（可选）", enumName));
        form.Children.Add(Labeled("排序", seq));
        form.Children.Add(enabled);

        bool TryApply()
        {
            if (string.IsNullOrWhiteSpace(val.Text) || string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text))
            {
                _session.LogWarn("值、编码、名称不能为空");
                return false;
            }

            if (!int.TryParse(seq.Text.Trim(), out var seqNum))
            {
                _session.LogWarn("排序必须为整数");
                return false;
            }

            value.ValValue = val.Text.Trim();
            value.ValCode = code.Text.Trim();
            value.ValName = name.Text.Trim();
            value.ValEnum = string.IsNullOrWhiteSpace(enumName.Text) ? null : enumName.Text.Trim();
            value.Seq = seqNum;
            value.Enabled = enabled.IsChecked == true;
            return true;
        }

        return new ValueEditForm(form, TryApply);
    }

    private async Task<bool> ConfirmDeleteAsync(string title, string firstMessage, string secondMessage)
    {
        var first = new ContentDialog
        {
            Title = title,
            Content = firstMessage,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await first.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        var second = new ContentDialog
        {
            Title = "再次确认",
            Content = secondMessage,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await second.ShowWithIconsAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> TryEditItemDialogAsync(DictItemRecord item, bool isEdit)
    {
        var code = new TextBox { Text = item.ItemCode, PlaceholderText = "item_code" };
        var name = new TextBox { Text = item.ItemName, PlaceholderText = "item_name" };
        var desc = new TextBox { Text = item.ItemDesc ?? "", PlaceholderText = "item_desc（可选）" };
        var enumFlag = new CheckBox { Content = "枚举项", IsChecked = item.EnumFlag };
        var enabled = new CheckBox { Content = "启用", IsChecked = item.Enabled };
        var typeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var selectedType = item.DataReferenceType ?? (int)DataReferenceType.String;
        var typeIndex = 0;
        foreach (DataReferenceType type in Enum.GetValues<DataReferenceType>())
        {
            var value = (int)type;
            typeCombo.Items.Add(new ComboBoxItem
            {
                Content = DataReferenceTypeHelper.ToTypeCode(type),
                Tag = value,
            });
            if (value == selectedType)
            {
                typeIndex = typeCombo.Items.Count - 1;
            }
        }

        typeCombo.SelectedIndex = typeIndex;

        var form = new StackPanel { Spacing = 10, MinWidth = 400 };
        form.Children.Add(Labeled("字典项编码", code));
        form.Children.Add(Labeled("字典项名称", name));
        form.Children.Add(Labeled("说明（可选）", desc));
        form.Children.Add(enumFlag);
        form.Children.Add(Labeled("数据类型", typeCombo));
        form.Children.Add(enabled);

        var dialog = new ContentDialog
        {
            Title = isEdit ? "编辑字典项" : "新增字典项",
            Content = form,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text))
        {
            _session.LogWarn("编码与名称不能为空");
            return false;
        }

        item.ItemCode = code.Text.Trim();
        item.ItemName = name.Text.Trim();
        item.ItemDesc = string.IsNullOrWhiteSpace(desc.Text) ? null : desc.Text.Trim();
        item.EnumFlag = enumFlag.IsChecked == true;
        item.Enabled = enabled.IsChecked == true;
        if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is int typeValue)
        {
            item.DataReferenceType = typeValue;
        }

        return true;
    }

    private Grid BuildValueRow(DictValueRow value)
    {
        var grid = CreateValueColumnsGrid();
        grid.Padding = new Thickness(0, 4, 0, 4);

        var check = new CheckBox
        {
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = value,
            IsChecked = _selectedValueIds.Contains(value.ValId),
        };
        check.Checked += OnValueCheckChanged;
        check.Unchecked += OnValueCheckChanged;
        AddCell(grid, check, 0);
        AddCell(grid, new TextBlock { Text = value.RowIndex.ToString(), Opacity = 0.75 }, 1);
        AddCell(
            grid,
            new TextBlock
            {
                Text = value.ValValue,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            2);
        AddCell(
            grid,
            new TextBlock { Text = value.ValCode, TextTrimming = TextTrimming.CharacterEllipsis },
            3);
        AddCell(
            grid,
            new TextBlock { Text = value.ValName, TextTrimming = TextTrimming.CharacterEllipsis },
            4);
        AddCell(
            grid,
            new TextBlock
            {
                Text = value.EnabledDisplay,
                Opacity = value.Enabled ? 0.9 : 0.55,
            },
            5);
        AddCell(grid, new TextBlock { Text = value.Seq.ToString() }, 6);

        var actionWidth = TableColumnMetrics.ActionColumnWidthFor(2);
        var edit = DevtoyButtons.Create("编辑", Symbol.Edit, new Thickness(6, 2, 6, 2));
        edit.Tag = value;
        edit.Click += OnEditValue;
        edit.Style = (Style)Application.Current.Resources["DevtoyRowActionButtonStyle"];
        var delete = DevtoyButtons.Create("删除", Symbol.Delete, new Thickness(6, 2, 6, 2));
        delete.Tag = value;
        delete.Click += OnDeleteValue;
        delete.Style = (Style)Application.Current.Resources["DevtoyRowActionButtonStyle"];
        var ops = new ActionButtonWrapPanel { MaxWidth = actionWidth };
        ops.Children.Add(edit);
        ops.Children.Add(delete);
        AddCell(grid, ops, 7);
        return grid;
    }

    private void OnValueCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: DictValueRow value } box)
        {
            return;
        }

        if (box.IsChecked == true)
        {
            _selectedValueIds.Add(value.ValId);
        }
        else
        {
            _selectedValueIds.Remove(value.ValId);
        }
    }

    private void SetAllValueChecks(Panel valuesPanel, CheckBox header, bool? isChecked)
    {
        if (header.IsChecked is null)
        {
            return;
        }

        foreach (var child in valuesPanel.Children)
        {
            if (child is not Grid row)
            {
                continue;
            }

            foreach (var el in row.Children)
            {
                if (el is CheckBox { Tag: DictValueRow value } box)
                {
                    box.IsChecked = isChecked == true;
                    if (isChecked == true)
                    {
                        _selectedValueIds.Add(value.ValId);
                    }
                    else
                    {
                        _selectedValueIds.Remove(value.ValId);
                    }
                }
            }
        }
    }

    private static void AddHeaderCell(Grid grid, string text, int column)
    {
        var block = new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        AddCell(grid, block, column);
    }

    private static void AddCell(Grid grid, FrameworkElement child, int column)
    {
        child.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static Grid Labeled(string label, FrameworkElement field)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(field, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(field);
        return grid;
    }

    private static DictItemRecord? GetItem(object sender) =>
        sender is Button { Tag: DictItemRecord item } ? item : null;

    private static DictItemRecord CloneItem(DictItemRecord item) => new()
    {
        ItemId = item.ItemId,
        ItemCode = item.ItemCode,
        ItemName = item.ItemName,
        ItemScope = item.ItemScope,
        ItemDesc = item.ItemDesc,
        EnumFlag = item.EnumFlag,
        DataReferenceType = item.DataReferenceType,
        Remark = item.Remark,
        Enabled = item.Enabled,
    };

    private static DictValueRow CloneValue(DictValueRow value) => new()
    {
        ValId = value.ValId,
        ItemId = value.ItemId,
        ValValue = value.ValValue,
        ValCode = value.ValCode,
        ValName = value.ValName,
        ValEnum = value.ValEnum,
        ValScope = value.ValScope,
        ValDesc = value.ValDesc,
        Seq = value.Seq,
        Remark = value.Remark,
        Enabled = value.Enabled,
    };

    private async Task ReloadItemsAsync(bool showBusy = true)
    {
        var code = FilterCodeBox.Text;
        var name = FilterNameBox.Text;

        async Task LoadAsync()
        {
            var items = await _session.ListDictItemsAsync(code, name).ConfigureAwait(true);
            _items.Clear();
            _items.AddRange(items);
            ApplySort();
            UpdateSortHeaders();
        }

        if (showBusy)
        {
            await RunBusyAsync("正在查询字典…", LoadAsync).ConfigureAwait(true);
        }
        else
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    private void OnSortColumn(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        if (string.Equals(_sortKey, key, StringComparison.Ordinal))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortKey = key;
            _sortAscending = true;
        }

        ApplySort();
        UpdateSortHeaders();
    }

    private void ApplySort()
    {
        if (_items.Count == 0)
        {
            DictList.ItemsSource = null;
            return;
        }

        IEnumerable<DictItemRecord> sorted = _sortKey switch
        {
            "ItemName" => SortBy(i => i.ItemName),
            "ReferenceCode" => SortBy(i => i.ReferenceCode),
            "EnumFlag" => _sortAscending
                ? _items.OrderBy(i => i.EnumFlag).ThenBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase)
                : _items.OrderByDescending(i => i.EnumFlag).ThenBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase),
            "Enabled" => _sortAscending
                ? _items.OrderBy(i => i.Enabled).ThenBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase)
                : _items.OrderByDescending(i => i.Enabled).ThenBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase),
            "CreateTime" => SortBy(i => i.CreateTime ?? DateTime.MinValue),
            "ModifyTime" => SortBy(i => i.ModifyTime ?? DateTime.MinValue),
            _ => SortBy(i => i.ItemCode),
        };

        var list = sorted.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            list[i].RowIndex = i + 1;
        }

        DictList.ItemsSource = list;
        SyncHeaderSelectAll();
    }

    private IEnumerable<DictItemRecord> SortBy<TKey>(Func<DictItemRecord, TKey> keySelector)
        where TKey : IComparable<TKey>
    {
        return _sortAscending
            ? _items.OrderBy(keySelector)
            : _items.OrderByDescending(keySelector);
    }

    private void UpdateSortHeaders()
    {
        SetSortHeader(SortItemCode, "ItemCode", "编码");
        SetSortHeader(SortItemName, "ItemName", "名称");
        SetSortHeader(SortReference, "ReferenceCode", "类型");
        SetSortHeader(SortEnum, "EnumFlag", "枚举");
        SetSortHeader(SortEnabled, "Enabled", "启用");
        SetSortHeader(SortCreateTime, "CreateTime", "创建日期");
        SetSortHeader(SortModifyTime, "ModifyTime", "修改日期");
    }

    private void SetSortHeader(Button button, string key, string label)
    {
        var active = string.Equals(_sortKey, key, StringComparison.Ordinal);
        if (active)
        {
            // Segoe Fluent Icons: ChevronUp / ChevronDown
            var glyph = _sortAscending ? "\uE70E" : "\uE70D";
            button.Content = DevtoyButtons.BuildContentWithGlyph(label, glyph);
        }
        else
        {
            button.Content = DevtoyButtons.BuildContent(label, Symbol.Sort);
        }

        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static Task RunBusyAsync(string message, Func<Task> action) =>
        UiBusy.RunAsync(message, action);
}