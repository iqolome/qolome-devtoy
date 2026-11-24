using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.App;

public static class PromptDialogs
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<DeviceTypeIconExportFormat?> PromptIconExportFormatAsync(XamlRoot root)
    {
        var svg = new RadioButton
        {
            Content = "SVG（矢量原图）",
            GroupName = "DeviceTypeIconExport",
            IsChecked = true
        };
        var png = new RadioButton
        {
            Content = "PNG（由 SVG 转换，深灰描边风格）",
            GroupName = "DeviceTypeIconExport"
        };
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = "图标将打包为 ZIP。选择 PNG 时按浅底深灰描边渲染，风格接近常见设备列表图标。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });
        panel.Children.Add(svg);
        panel.Children.Add(png);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "导出设备类型图标",
            Content = panel,
            PrimaryButtonText = "导出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return png.IsChecked == true
            ? DeviceTypeIconExportFormat.Png
            : DeviceTypeIconExportFormat.Svg;
    }

    public static async Task PromptTwoFieldsAsync(
        XamlRoot root,
        string title,
        string nameLabel,
        string extraLabel,
        Func<string, string, Task> save,
        string? name = null,
        string? extra = null)
    {
        var nameBox = new TextBox { Header = nameLabel, Text = name ?? "", PlaceholderText = nameLabel };
        var extraBox = new TextBox { Header = extraLabel, Text = extra ?? "", PlaceholderText = extraLabel };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(extraBox);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        BindSaveOnPrimary(dialog, () => save(nameBox.Text, extraBox.Text));
        await dialog.ShowAsync();
    }

    public static async Task PromptPortPurposeAsync(
        XamlRoot root,
        int port,
        string protocol,
        Func<(string Service, string Description), Task> save,
        string? service = null,
        string? description = null)
    {
        var serviceBox = new TextBox
        {
            Header = "服务名称",
            Text = service ?? "",
            PlaceholderText = "例如 SSH、DNS、自定义业务"
        };
        var descBox = new TextBox
        {
            Header = "用途说明",
            Text = description ?? "",
            PlaceholderText = "例如 远程登录、打印机、监控平台",
            AcceptsReturn = true,
            MinHeight = 64
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{port}/{protocol.ToLowerInvariant()} 的用途会应用到之后的扫描结果。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });
        panel.Children.Add(serviceBox);
        panel.Children.Add(descBox);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "标记端口用途",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        BindSaveOnPrimary(dialog, () => save((serviceBox.Text, descBox.Text)));
        await dialog.ShowAsync();
    }

    public static async Task PromptDeviceTypeAsync(
        XamlRoot root,
        string title,
        Func<DeviceTypeEditInput, Task> save,
        DeviceType? current = null)
    {
        var codeBox = new TextBox { Header = "编码", Text = InventoryService.TrimInput(current?.Code), PlaceholderText = "例如 server" };
        var nameBox = new TextBox { Header = "名称", Text = InventoryService.TrimInput(current?.Name) };
        var descBox = new TextBox { Header = "说明", Text = InventoryService.TrimInput(current?.Description, keepLineBreaks: true) };
        AttachTrim(codeBox);
        AttachTrim(nameBox);
        AttachTrim(descBox, keepLineBreaks: true);
        var colorBox = new ComboBox
        {
            Header = "颜色分组",
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var selectedColor = DeviceTypeColorGroups.Normalize(current?.ColorGroup, current?.Code);
        var suppressColorChange = false;
        var autoColor = current is null;
        foreach (var group in DeviceTypeColorGroups.All)
        {
            var item = new ComboBoxItem
            {
                Tag = group.Key,
                Content = DeviceTypeColorXaml.Label(group)
            };
            colorBox.Items.Add(item);
            if (group.Key == selectedColor)
            {
                colorBox.SelectedItem = item;
            }
        }

        if (colorBox.SelectedItem is null)
        {
            colorBox.SelectedItem = colorBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag as string == DeviceTypeColorGroups.Other)
                ?? colorBox.Items.LastOrDefault();
        }

        void SelectColorGroup(string key)
        {
            suppressColorChange = true;
            colorBox.SelectedItem = colorBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag as string == key)
                ?? colorBox.SelectedItem;
            suppressColorChange = false;
        }

        colorBox.SelectionChanged += (_, _) =>
        {
            if (!suppressColorChange)
            {
                autoColor = false;
            }
        };
        string? pendingIconPath = null;
        string? pendingBuiltinKey = null;
        var restoreDefault = false;
        var preview = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var hint = new TextBlock { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

        void RefreshPreview()
        {
            var group = SelectedColorKey();
            var hex = DeviceTypeColorGroups.Hex(group, codeBox.Text);
            if (restoreDefault)
            {
                preview.Source = DeviceTypeIconXaml.From(codeBox.Text, null, false, group);
                hint.Text = "将按编码使用软件自带 SVG，颜色跟随上方颜色分组。";
                return;
            }

            if (pendingIconPath is not null)
            {
                preview.Source = DeviceTypeIconXaml.FromPath(pendingIconPath, hex);
                hint.Text = $"将导入自定义 SVG：{Path.GetFileName(pendingIconPath)}，颜色跟随分组。";
                return;
            }

            if (pendingBuiltinKey is not null)
            {
                preview.Source = DeviceTypeIconXaml.From(pendingBuiltinKey, pendingBuiltinKey, false, group);
                hint.Text = $"将使用图标库「{pendingBuiltinKey}」（随软件分发，不写入备份）。颜色跟随分组。";
                return;
            }

            preview.Source = current is null
                ? DeviceTypeIconXaml.From(codeBox.Text, null, false, group)
                : DeviceTypeIconXaml.From(current.Code, current.IconFile, current.IsCustomIcon, group);
            hint.Text = current?.IsCustomIcon == true
                ? "当前为自定义 SVG。设置导出时会一并打包该文件。颜色跟随分组。"
                : "当前为软件自带 SVG，不会写入备份。颜色跟随分组。";
        }

        string SelectedColorKey() =>
            (colorBox.SelectedItem as ComboBoxItem)?.Tag as string
            ?? DeviceTypeColorGroups.ForCode(codeBox.Text);

        var library = new GridView
        {
            MaxHeight = 200,
            MinHeight = 120,
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.Single
        };
        library.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            """
            <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ItemsWrapGrid Orientation="Horizontal" ItemWidth="40" ItemHeight="40"/>
            </ItemsPanelTemplate>
            """);

        void FillLibrary(string? query)
        {
            library.Items.Clear();
            var q = query?.Trim() ?? "";
            foreach (var icon in DeviceTypeIconStore.ListBuiltin())
            {
                if (q.Length > 0 && !icon.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var img = new Image
                {
                    Width = 28,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                    Source = DeviceTypeIconXaml.FromPath(icon.Path, DeviceTypeColorGroups.Hex(SelectedColorKey(), codeBox.Text)),
                    Tag = icon.Key,
                    Margin = new Thickness(4)
                };
                ToolTipService.SetToolTip(img, icon.Key);
                library.Items.Add(img);
            }
        }

        library.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not Image img || img.Tag is not string key)
            {
                return;
            }

            pendingBuiltinKey = key;
            pendingIconPath = null;
            restoreDefault = false;
            RefreshPreview();
        };

        var filter = new TextBox { PlaceholderText = "筛选图标库，例如 camera、wifi" };
        filter.TextChanged += (_, _) => FillLibrary(filter.Text);
        FillLibrary(null);
        colorBox.SelectionChanged += (_, _) =>
        {
            FillLibrary(filter.Text);
            RefreshPreview();
        };

        codeBox.TextChanged += (_, _) =>
        {
            if (autoColor)
            {
                SelectColorGroup(DeviceTypeColorGroups.ForCode(codeBox.Text));
            }

            if (pendingIconPath is null && pendingBuiltinKey is null)
            {
                RefreshPreview();
            }
        };

        var pick = new Button { Content = "导入 SVG" };
        pick.Click += async (_, _) =>
        {
            var path = await FileDialogs.PickOpenIconAsync();
            if (path is null)
            {
                return;
            }

            pendingIconPath = path;
            pendingBuiltinKey = null;
            restoreDefault = false;
            RefreshPreview();
        };
        var restore = new Button { Content = "按编码匹配" };
        restore.Click += (_, _) =>
        {
            pendingIconPath = null;
            pendingBuiltinKey = null;
            restoreDefault = true;
            RefreshPreview();
        };
        RefreshPreview();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(pick);
        buttons.Children.Add(restore);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(codeBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(colorBox);
        panel.Children.Add(descBox);
        panel.Children.Add(new TextBlock { Text = "图标库（点击选用；也可导入 SVG）。描边/填充会套用颜色分组。" });
        panel.Children.Add(preview);
        panel.Children.Add(buttons);
        panel.Children.Add(filter);
        panel.Children.Add(library);
        panel.Children.Add(hint);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        BindSaveOnPrimary(dialog, () => save(new DeviceTypeEditInput
        {
            Code = InventoryService.TrimInput(codeBox.Text),
            Name = InventoryService.TrimInput(nameBox.Text),
            Description = InventoryService.TrimInput(descBox.Text, keepLineBreaks: true),
            IconSourcePath = pendingIconPath,
            BuiltinIconKey = pendingBuiltinKey,
            RestoreDefaultIcon = restoreDefault,
            ColorGroup = (colorBox.SelectedItem as ComboBoxItem)?.Tag as string
        }));
        await dialog.ShowAsync();
    }

    public static async Task PromptDeviceAsync(
        XamlRoot root,
        string title,
        IReadOnlyList<DeviceType> types,
        IReadOnlyList<DeviceGroup> groups,
        Device? current,
        Func<DeviceEditInput, Task> save)
    {
        var codeBox = Field("设备编码", current?.Code, "留空则自动生成");
        var hostnameBox = Field("主机名", current?.Hostname, "选填");
        var vendorBox = Field("厂商", current?.Vendor, "选填，OUI 或制造商");
        var brandBox = Field("品牌", current?.Brand, "选填");
        var modelBox = Field("型号", current?.Model);
        var descriptionBox = Field("设备说明", current?.Description, "选填", multiline: true);
        var osBox = Field("系统类型", current?.OsType, "Windows / Linux/Unix");
        var portsBox = Field("开放端口", current?.OpenPorts, "22, 80, 443");
        var infoBox = Field("设备信息", current?.DeviceInfo, multiline: true);
        var remarksBox = Field("备注", current?.Remarks, multiline: true);

        var typeBox = new ComboBox
        {
            Header = "设备类型",
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "未分类",
            DisplayMemberPath = "Name"
        };
        foreach (var type in types)
        {
            typeBox.Items.Add(type);
        }

        if (current?.DeviceTypeId is int typeId)
        {
            typeBox.SelectedItem = types.FirstOrDefault(t => t.Id == typeId);
        }

        var typeIcon = new Image
        {
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 22, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var nicHint = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        };
        void RefreshTypeIcon() =>
            typeIcon.Source = DeviceTypeIconXaml.FromType(typeBox.SelectedItem as DeviceType);
        void RefreshBluetoothHints()
        {
            var type = typeBox.SelectedItem as DeviceType;
            var bluetooth = DeviceTypeCatalog.IsBluetooth(type?.Code, type?.Name);
            nicHint.Text = bluetooth
                ? "蓝牙设备可无 IP；请至少填写主机名或 MAC。多个 IP/MAC 用分号或换行分隔，缺项用 _ 占位。"
                : "多个 IP/MAC 用 ; 或换行按顺序对应。某块网卡没有 IP 或 MAC 时填 _，例如 _ ; 192.168.1.1。第一块为主网卡。";
            hostnameBox.PlaceholderText = bluetooth ? "建议填写，如耳机名称" : "选填";
        }

        typeBox.SelectionChanged += (_, _) =>
        {
            RefreshTypeIcon();
            RefreshBluetoothHints();
        };
        RefreshTypeIcon();
        RefreshBluetoothHints();

        var nicsPanel = new StackPanel { Spacing = 6 };
        void AddNicRow(DeviceNic? nic = null)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameBox = new TextBox
            {
                PlaceholderText = "名称，如 LAN",
                Text = InventoryService.TrimInput(nic?.Name)
            };
            AttachTrim(nameBox);
            var nicIpBox = new TextBox
            {
                PlaceholderText = "IP，多个用 ; 或换行，空位填 _",
                Text = nic is null ? "" : InventoryService.NicSlotDisplay(nic.Ip),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 32
            };
            AttachTrim(nicIpBox, keepLineBreaks: true);
            var nicMacBox = new TextBox
            {
                PlaceholderText = "MAC，多个用 ; 或换行，空位填 _",
                Text = nic is null ? "" : InventoryService.NicSlotDisplay(nic.Mac),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 32
            };
            AttachTrim(nicMacBox, keepLineBreaks: true);
            var nicOui = new Button { Content = "OUI", VerticalAlignment = VerticalAlignment.Bottom };
            ToolTipService.SetToolTip(nicOui, "根据该网卡 MAC 查询 IEEE OUI 并填入厂商");
            nicOui.Click += (_, _) =>
            {
                var macToken = InventoryService.SplitNicValues(nicMacBox.Text)
                    .FirstOrDefault(s => s.Length > 0);
                if (string.IsNullOrWhiteSpace(macToken))
                {
                    nicHint.Text = "请先填写该网卡的 MAC 地址。";
                    return;
                }

                var formatted = InventoryService.NormalizeMac(macToken);
                if (InventoryService.SplitNicValues(nicMacBox.Text).Count(s => s.Length > 0) == 1
                    && !string.IsNullOrWhiteSpace(formatted)
                    && formatted.Contains(':'))
                {
                    nicMacBox.Text = formatted;
                }

                var vendor = OuiStore.Lookup(formatted ?? macToken);
                if (string.IsNullOrWhiteSpace(vendor))
                {
                    nicHint.Text = "未找到对应厂家信息";
                    return;
                }

                vendorBox.Text = vendor;
                nicHint.Text = $"已填入厂商：{vendor}";
            };
            var remove = new Button { Content = "删除", VerticalAlignment = VerticalAlignment.Bottom };
            remove.Click += (_, _) =>
            {
                if (nicsPanel.Children.Count > 1)
                {
                    nicsPanel.Children.Remove(row);
                }
                else
                {
                    nameBox.Text = "";
                    nicIpBox.Text = "";
                    nicMacBox.Text = "";
                }
            };
            Grid.SetColumn(nicIpBox, 1);
            Grid.SetColumn(nicMacBox, 2);
            Grid.SetColumn(nicOui, 3);
            Grid.SetColumn(remove, 4);
            row.Children.Add(nameBox);
            row.Children.Add(nicIpBox);
            row.Children.Add(nicMacBox);
            row.Children.Add(nicOui);
            row.Children.Add(remove);
            nicsPanel.Children.Add(row);
        }

        var existingNics = current?.OrderedNics().ToList() ?? [];
        if (existingNics.Count == 0)
        {
            AddNicRow();
        }
        else
        {
            foreach (var nic in existingNics)
            {
                AddNicRow(nic);
            }
        }

        var addNic = new Button { Content = "添加网卡" };
        addNic.Click += (_, _) => AddNicRow();

        var typeRow = new Grid { ColumnSpacing = 8 };
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(typeBox, 1);
        typeRow.Children.Add(typeIcon);
        typeRow.Children.Add(typeBox);

        var groupBox = new ComboBox
        {
            Header = "网络分组",
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "未分组",
            DisplayMemberPath = "Name"
        };
        foreach (var group in groups)
        {
            groupBox.Items.Add(group);
        }

        if (current?.DeviceGroupId is int groupId)
        {
            groupBox.SelectedItem = groups.FirstOrDefault(g => g.Id == groupId);
        }

        var domainsPanel = new StackPanel { Spacing = 6 };
        void AddDomainRow(string? text = null)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new TextBox
            {
                Text = InventoryService.TrimInput(text),
                PlaceholderText = "例如 app.local 或多个用 ; 分隔"
            };
            AttachTrim(box);
            var remove = new Button { Content = "删除", VerticalAlignment = VerticalAlignment.Bottom };
            remove.Click += (_, _) =>
            {
                if (domainsPanel.Children.Count > 1)
                {
                    domainsPanel.Children.Remove(row);
                }
                else
                {
                    box.Text = "";
                }
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(box);
            row.Children.Add(remove);
            domainsPanel.Children.Add(row);
        }

        var existingDomains = current?.Domains.Select(d => d.Domain).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
        if (existingDomains.Count == 0)
        {
            AddDomainRow();
        }
        else
        {
            foreach (var domain in existingDomains)
            {
                AddDomainRow(domain);
            }
        }

        var addDomain = new Button { Content = "添加域名" };
        addDomain.Click += (_, _) => AddDomainRow();

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(codeBox);
        left.Children.Add(hostnameBox);
        left.Children.Add(vendorBox);
        left.Children.Add(brandBox);
        left.Children.Add(modelBox);
        left.Children.Add(descriptionBox);
        left.Children.Add(osBox);

        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(typeRow);
        right.Children.Add(groupBox);
        right.Children.Add(portsBox);
        right.Children.Add(infoBox);
        right.Children.Add(remarksBox);

        var columns = new Grid { ColumnSpacing = 16 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var rootPanel = new StackPanel { Spacing = 12 };
        rootPanel.Children.Add(columns);
        rootPanel.Children.Add(new TextBlock
        {
            Text = "网卡（; 或换行分隔；缺 IP/MAC 用 _ 占位；第一块为主网卡）",
            Opacity = 0.7
        });
        rootPanel.Children.Add(nicHint);
        rootPanel.Children.Add(nicsPanel);
        rootPanel.Children.Add(addNic);
        rootPanel.Children.Add(new TextBlock
        {
            Text = "域名（可添加多条，每条一组；也可用 ; , 分隔）",
            Opacity = 0.7
        });
        rootPanel.Children.Add(domainsPanel);
        rootPanel.Children.Add(addDomain);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer
            {
                Content = rootPanel,
                MaxHeight = 560,
                MinWidth = 640
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = 820.0;
        BindSaveOnPrimary(dialog, () =>
        {
            var nics = new List<DeviceNicInput>();
            foreach (var child in nicsPanel.Children)
            {
                if (child is not Grid grid)
                {
                    continue;
                }

                var boxes = grid.Children.OfType<TextBox>().ToList();
                if (boxes.Count < 3)
                {
                    continue;
                }

                nics.Add(new DeviceNicInput
                {
                    Name = InventoryService.TrimInput(boxes[0].Text),
                    Ip = InventoryService.TrimInput(boxes[1].Text, keepLineBreaks: true),
                    Mac = InventoryService.TrimInput(boxes[2].Text, keepLineBreaks: true)
                });
            }

            var domains = new List<string>();
            foreach (var child in domainsPanel.Children)
            {
                if (child is Grid grid)
                {
                    var box = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (box is not null)
                    {
                        domains.Add(InventoryService.TrimInput(box.Text));
                    }
                }
            }

            return save(new DeviceEditInput
            {
                Code = InventoryService.TrimInput(codeBox.Text),
                Hostname = InventoryService.TrimInput(hostnameBox.Text),
                Vendor = InventoryService.TrimInput(vendorBox.Text),
                Brand = InventoryService.TrimInput(brandBox.Text),
                Nics = nics,
                Model = InventoryService.TrimInput(modelBox.Text),
                Description = InventoryService.TrimInput(descriptionBox.Text, keepLineBreaks: true),
                OsType = InventoryService.TrimInput(osBox.Text),
                OpenPorts = InventoryService.TrimInput(portsBox.Text),
                DeviceInfo = InventoryService.TrimInput(infoBox.Text, keepLineBreaks: true),
                Remarks = InventoryService.TrimInput(remarksBox.Text, keepLineBreaks: true),
                DeviceTypeId = (typeBox.SelectedItem as DeviceType)?.Id,
                DeviceGroupId = (groupBox.SelectedItem as DeviceGroup)?.Id,
                Domains = domains
            });
        });
        await dialog.ShowAsync();
    }

    public static async Task<(string Label, string IspKey)?> PromptIspAsync(
        XamlRoot root,
        string? currentLabel = null,
        string? currentKey = null)
    {
        var key = IspPresets.Normalize(currentKey, currentLabel);
        var combo = new ComboBox
        {
            Header = "运营商 / 远程来源",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 280
        };
        ComboBoxItem? selected = null;
        foreach (var preset in IspPresets.All)
        {
            var item = new ComboBoxItem
            {
                Tag = preset.Key,
                Content = IspRow(preset.Key, preset.Label)
            };
            combo.Items.Add(item);
            if (preset.Key == key)
            {
                selected = item;
            }
        }

        combo.SelectedItem = selected ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();

        var customBox = new TextBox
        {
            Header = "自定义批注",
            PlaceholderText = "例如 教育网、专线",
            Text = key == IspPresets.Custom && !string.IsNullOrWhiteSpace(currentLabel)
                ? currentLabel
                : ""
        };
        UpdateCustomVisibility();
        combo.SelectionChanged += (_, _) => UpdateCustomVisibility();

        var panel = new StackPanel { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(combo);
        panel.Children.Add(customBox);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = currentLabel is null && currentKey is null ? "添加互联网 ISP" : "编辑互联网",
            Content = panel,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var picked = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? IspPresets.Custom;
        picked = IspPresets.Normalize(picked, null);
        if (IspPresets.IsCarrier(picked))
        {
            return (IspPresets.DefaultLabel(picked), picked);
        }

        var text = string.IsNullOrWhiteSpace(customBox.Text)
            ? IspPresets.DefaultLabel(IspPresets.Custom)
            : customBox.Text.Trim();
        return (text, IspPresets.Custom);

        void UpdateCustomVisibility()
        {
            var pickedKey = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
            customBox.Visibility = IspPresets.Normalize(pickedKey, null) == IspPresets.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    public static async Task<string?> PromptIspCustomLabelAsync(XamlRoot root)
    {
        var box = new TextBox
        {
            Header = "自定义批注",
            PlaceholderText = "例如 教育网、专线",
            Text = IspPresets.DefaultLabel(IspPresets.Custom)
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "添加互联网（自定义）",
            Content = box,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(box.Text)
            ? IspPresets.DefaultLabel(IspPresets.Custom)
            : box.Text.Trim();
    }

    public static async Task<string?> PromptContainerNameAsync(XamlRoot root, string? current = null)
    {
        var box = new TextBox
        {
            Header = "容器名称",
            PlaceholderText = "例如 无线、核心交换机、办公终端",
            Text = string.IsNullOrWhiteSpace(current) ? "容器" : current.Trim()
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = string.IsNullOrWhiteSpace(current) ? "添加容器" : "编辑容器名称",
            Content = box,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(box.Text) ? "容器" : box.Text.Trim();
    }

    private static UIElement IspRow(string key, string label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var source = DeviceTypeIconXaml.Isp(key);
        if (source is not null)
        {
            row.Children.Add(new Image
            {
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform,
                Source = source,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    public static async Task<(string Label, string? ColorHex, bool? Dashed)?> PromptLinkEditAsync(
        XamlRoot root,
        TopologyLinkDto link)
    {
        var labelBox = Field("连线批注", link.Label, "显示在连线旁，可留空");
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(labelBox);
        TextBox? colorBox = null;
        CheckBox? dashBox = null;
        var isCustom = TopologyLinkKinds.Normalize(link.Kind) == TopologyLinkKinds.Custom;
        if (isCustom)
        {
            colorBox = Field("颜色 (#RRGGBB)", TopologyLinkKinds.ResolveColor(link), "#7C3AED");
            dashBox = new CheckBox
            {
                Content = "虚线",
                IsChecked = TopologyLinkKinds.ResolveDashed(link)
            };
            panel.Children.Add(colorBox);
            panel.Children.Add(dashBox);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "编辑连线",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return (
            labelBox.Text.Trim(),
            isCustom ? colorBox!.Text.Trim() : null,
            isCustom ? dashBox!.IsChecked == true : null);
    }

    public static async Task<DeviceRefreshOptions?> PromptDeviceRefreshOptionsAsync(XamlRoot root, int deviceCount)
    {
        var ports = new CheckBox { Content = "端口扫描（默认 TCP/UDP 常用端口）", IsChecked = true };
        var osType = new CheckBox { Content = "系统类型信息（TTL / 发现方式）", IsChecked = true };
        var ip = new CheckBox { Content = "IP（按 MAC 对照本机 ARP 表回填）", IsChecked = true };
        var vendor = new CheckBox { Content = "厂商信息（仅当厂商为空时通过 MAC 查询 OUI）", IsChecked = true };
        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = $"将更新当前列表 {deviceCount} 台设备，不新增记录。离线时跳过对应项，不会清空已有值。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });
        panel.Children.Add(ports);
        panel.Children.Add(osType);
        panel.Children.Add(ip);
        panel.Children.Add(vendor);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "一键更新",
            Content = panel,
            PrimaryButtonText = "开始更新",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var options = new DeviceRefreshOptions
        {
            Ports = ports.IsChecked == true,
            OsType = osType.IsChecked == true,
            Ip = ip.IsChecked == true,
            Vendor = vendor.IsChecked == true
        };
        return options;
    }

    public static async Task<Windows.UI.Color?> PromptColorAsync(XamlRoot root, Windows.UI.Color current)
    {
        var picker = new ColorPicker
        {
            Color = current,
            IsAlphaEnabled = false,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            Width = 312
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "选择连线颜色",
            Content = picker,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return picker.Color;
    }

    private static void BindSaveOnPrimary(ContentDialog dialog, Func<Task> save)
    {
        var error = new InfoBar
        {
            Title = "校验未通过",
            Severity = InfoBarSeverity.Error,
            IsClosable = false,
            IsOpen = false
        };
        var host = new StackPanel { Spacing = 8 };
        host.Children.Add(error);
        if (dialog.Content is UIElement content)
        {
            host.Children.Add(content);
        }

        dialog.Content = host;
        dialog.Closing += (sender, args) =>
        {
            if (args.Result != ContentDialogResult.Primary)
            {
                return;
            }

            var deferral = args.GetDeferral();
            _ = CompleteAsync();

            async Task CompleteAsync()
            {
                sender.IsPrimaryButtonEnabled = false;
                try
                {
                    error.IsOpen = false;
                    error.Message = "";
                    await save();
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    error.Message = ex.Message;
                    error.IsOpen = true;
                }
                finally
                {
                    sender.IsPrimaryButtonEnabled = true;
                    deferral.Complete();
                }
            }
        };
    }

    private static TextBox Field(string header, string? text, string? placeholder = null, bool multiline = false)
    {
        var box = new TextBox
        {
            Header = header,
            Text = InventoryService.TrimInput(text, multiline),
            PlaceholderText = placeholder ?? header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            AcceptsReturn = multiline
        };
        if (multiline)
        {
            box.MinHeight = 56;
        }

        AttachTrim(box, multiline);
        return box;
    }

    private static void AttachTrim(TextBox box, bool keepLineBreaks = false)
    {
        box.LostFocus += (_, _) =>
        {
            var trimmed = InventoryService.TrimInput(box.Text, keepLineBreaks);
            if (box.Text != trimmed)
            {
                box.Text = trimmed;
            }
        };
    }
}
