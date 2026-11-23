using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Qolome_Devtoy_App.Helpers;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Windows.System;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class DictPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;
    private readonly List<DictItemRecord> _items = [];

    private string _sortKey = "ItemCode";
    private bool _sortAscending = true;

    private Func<Task>? _reloadValuesAction;

    private ResizableColumnLayout? _columnLayout;

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
                new ColumnSpec(1.4, 80),
                new ColumnSpec(1.8, 72),
                new ColumnSpec(0, 48, 64),
                new ColumnSpec(0, 36, 44),
                new ColumnSpec(0, 88, 100),
                new ColumnSpec(0, 88, 100),
                // 生成/值列表/编辑/删除：固定列宽，优先保留操作列完整可见
                new ColumnSpec(0, 340, 360),
            ]);
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

        var modules = ModuleCatalogFilter.ForCodegen(_session.Modules).ToList();
        if (modules.Count == 0)
        {
            _session.LogWarn("无可用模块（可能均已忽略），请先在「模块管理」刷新、取消忽略或添加指定工程");
            return;
        }

        var moduleCombo = new ComboBox
        {
            ItemsSource = modules,
            DisplayMemberPath = nameof(ModuleEntry.TargetModuleDisplay),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (_session.SelectedModule is { IsIgnored: false })
        {
            moduleCombo.SelectedItem = modules
                .FirstOrDefault(m => m.ModulePath == _session.SelectedModule.ModulePath);
        }

        var enumBox = new CheckBox { Content = "Java Enum" };
        var tsBox = new CheckBox { Content = "TS 字典" };
        void SyncTypes()
        {
            if (moduleCombo.SelectedItem is not ModuleEntry entry)
            {
                enumBox.IsChecked = false;
                tsBox.IsChecked = false;
                return;
            }

            enumBox.IsChecked = entry.ProjectTypes.Java;
            tsBox.IsChecked = entry.ProjectTypes.SupportsTypeScript;
        }

        moduleCombo.SelectionChanged += (_, _) => SyncTypes();
        SyncTypes();

        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(new TextBlock { Text = "目标模块", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        form.Children.Add(moduleCombo);
        form.Children.Add(new TextBlock { Text = "生成类型" });
        form.Children.Add(enumBox);
        form.Children.Add(tsBox);

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

        if (moduleCombo.SelectedItem is not ModuleEntry module)
        {
            _session.LogWarn("请选择目标模块");
            return;
        }

        await RunBusyAsync("正在生成字典代码…", async () =>
        {
            await _session.GenerateDictForItemAsync(
                item,
                module,
                enumBox.IsChecked == true,
                tsBox.IsChecked == true).ConfigureAwait(true);
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

    private async void OnEditValue(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictValueRow value })
        {
            return;
        }

        var snapshot = CloneValue(value);
        if (!await TryEditValueDialogAsync(snapshot, isEdit: true).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在更新字典值…", async () =>
        {
            if (await _session.UpdateDictValueAsync(snapshot).ConfigureAwait(true) && _reloadValuesAction is not null)
            {
                await _reloadValuesAction().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async void OnDeleteValue(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DictValueRow value })
        {
            return;
        }

        if (!await ConfirmDeleteAsync(
                "删除字典值",
                $"确定逻辑删除字典值「{value.ValCode}」？",
                $"再次确认删除值「{value.ValName}」（{value.ValValue}）？").ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync("正在删除字典值…", async () =>
        {
            if (await _session.DeleteDictValueAsync(value.ValId).ConfigureAwait(true) && _reloadValuesAction is not null)
            {
                await _reloadValuesAction().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async Task ShowValueListDialogAsync(DictItemRecord item)
    {
        var valuesPanel = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        async Task ReloadValuesAsync()
        {
            var loaded = await _session.ListDictValuesAsync(item.ItemId).ConfigureAwait(true);
            valuesPanel.Children.Clear();
            foreach (var row in loaded)
            {
                valuesPanel.Children.Add(BuildValueRow(row));
            }
        }

        _reloadValuesAction = ReloadValuesAsync;
        await RunBusyAsync("正在加载字典值…", ReloadValuesAsync).ConfigureAwait(true);

        var addBtn = DevtoyButtons.Create("新增字典值", Symbol.Add);
        addBtn.Click += async (_, _) =>
        {
            var draft = new DictValueRow { ItemId = item.ItemId, Enabled = true };
            if (!await TryEditValueDialogAsync(draft, isEdit: false).ConfigureAwait(true))
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

        var header = CreateValueColumnsGrid();
        header.Padding = new Thickness(0, 0, 0, 6);
        header.Opacity = 0.85;
        AddHeaderCell(header, "值", 0);
        AddHeaderCell(header, "编码", 1);
        AddHeaderCell(header, "名称", 2);
        AddHeaderCell(header, "排序", 3);
        AddHeaderCell(header, "操作", 4);

        var listHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        listHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        var scroll = new ScrollViewer
        {
            Content = valuesPanel,
            MaxHeight = 360,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetRow(scroll, 1);
        listHost.Children.Add(header);
        listHost.Children.Add(scroll);

        var root = new StackPanel
        {
            Spacing = 10,
            MinWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(new TextBlock
        {
            Text = $"{item.ItemCode} · {item.ItemName}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        root.Children.Add(addBtn);
        root.Children.Add(listHost);

        var dialog = new ContentDialog
        {
            Title = "字典值列表",
            Content = root,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
            // WinUI 默认约 548，会裁切「操作」列按钮
            MinWidth = 820,
            MaxWidth = 960,
        };
        await dialog.ShowWithIconsAsync();
        _reloadValuesAction = null;
    }

    /// <summary>表头与数据行共用同一套列宽，避免错位。</summary>
    private static Grid CreateValueColumnsGrid()
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64), MinWidth = 48 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 72 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 72 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48), MinWidth = 40 });
        // Auto：按「编辑/删除」按钮实际宽度占位，避免被裁切
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 168 });
        return grid;
    }

    private async Task<bool> ConfirmDeleteAsync(string title, string firstMessage, string secondMessage)
    {
        var first = new ContentDialog
        {
            Title = title,
            Content = firstMessage,
            PrimaryButtonText = "继续",
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

    private async Task<bool> TryEditValueDialogAsync(DictValueRow value, bool isEdit)
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

        var dialog = new ContentDialog
        {
            Title = isEdit ? "编辑字典值" : "新增字典值",
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

    private UIElement BuildValueRow(DictValueRow value)
    {
        var grid = CreateValueColumnsGrid();
        grid.Padding = new Thickness(0, 4, 0, 4);

        AddCell(
            grid,
            new TextBlock
            {
                Text = value.ValValue,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            0);
        AddCell(
            grid,
            new TextBlock { Text = value.ValCode, TextTrimming = TextTrimming.CharacterEllipsis },
            1);
        AddCell(
            grid,
            new TextBlock { Text = value.ValName, TextTrimming = TextTrimming.CharacterEllipsis },
            2);
        AddCell(grid, new TextBlock { Text = value.Seq.ToString() }, 3);

        var edit = DevtoyButtons.Create("编辑", Symbol.Edit, new Thickness(6, 2, 6, 2));
        edit.Tag = value;
        edit.Click += OnEditValue;
        edit.Style = (Style)Application.Current.Resources["DevtoyRowActionButtonStyle"];
        var delete = DevtoyButtons.Create("删除", Symbol.Delete, new Thickness(6, 2, 6, 2));
        delete.Tag = value;
        delete.Click += OnDeleteValue;
        delete.Style = (Style)Application.Current.Resources["DevtoyRowActionButtonStyle"];
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        ops.Children.Add(edit);
        ops.Children.Add(delete);
        AddCell(grid, ops, 4);
        return grid;
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

    private static UIElement Labeled(string label, FrameworkElement field)
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
            "CreateTime" => SortBy(i => i.CreateTime ?? DateTime.MinValue),
            "ModifyTime" => SortBy(i => i.ModifyTime ?? DateTime.MinValue),
            _ => SortBy(i => i.ItemCode),
        };

        DictList.ItemsSource = sorted.ToList();
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
        SetSortHeader(SortCreateTime, "CreateTime", "创建日期");
        SetSortHeader(SortModifyTime, "ModifyTime", "修改日期");
    }

    private void SetSortHeader(Button button, string key, string label)
    {
        var active = string.Equals(_sortKey, key, StringComparison.Ordinal);
        // Segoe Fluent Icons: ChevronUp / ChevronDown；未激活用 Sort
        button.Content = active
            ? DevtoyButtons.BuildContentWithGlyph(label, _sortAscending ? "\uE70E" : "\uE70D")
            : DevtoyButtons.BuildContent(label, Symbol.Sort);
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private Task RunBusyAsync(string message, Func<Task> action) =>
        UiBusy.RunAsync(message, action);
}