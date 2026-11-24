using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.App;

public static partial class PromptDialogs
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

        if (isCustom && colorBox is not null && dashBox is not null)
        {
            return (labelBox.Text.Trim(), colorBox.Text.Trim(), dashBox.IsChecked == true);
        }

        return (labelBox.Text.Trim(), null, null);
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
