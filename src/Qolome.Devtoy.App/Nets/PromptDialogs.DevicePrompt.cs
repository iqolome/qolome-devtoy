using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.App;

public static partial class PromptDialogs
{
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
        typeBox.SelectionChanged += (_, _) => RefreshDeviceTypePresentation(typeBox, typeIcon, nicHint, hostnameBox);
        RefreshDeviceTypePresentation(typeBox, typeIcon, nicHint, hostnameBox);

        var nicsPanel = new StackPanel { Spacing = 6 };
        SeedDeviceNicRows(nicsPanel, current, nicHint, vendorBox);

        var addNic = new Button { Content = "添加网卡" };
        addNic.Click += (_, _) => AppendDeviceNicRow(nicsPanel, nicHint, vendorBox);

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
        SeedDeviceDomainRows(domainsPanel, current);

        var addDomain = new Button { Content = "添加域名" };
        addDomain.Click += (_, _) => AppendDeviceDomainRow(domainsPanel);

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
        var fields = new DevicePromptFields
        {
            CodeBox = codeBox,
            HostnameBox = hostnameBox,
            VendorBox = vendorBox,
            BrandBox = brandBox,
            ModelBox = modelBox,
            DescriptionBox = descriptionBox,
            OsBox = osBox,
            PortsBox = portsBox,
            InfoBox = infoBox,
            RemarksBox = remarksBox,
            TypeBox = typeBox,
            GroupBox = groupBox,
            NicsPanel = nicsPanel,
            DomainsPanel = domainsPanel
        };
        BindSaveOnPrimary(dialog, () => save(BuildDeviceEditInput(fields)));
        await dialog.ShowAsync();
    }

    private static void RefreshDeviceTypePresentation(
        ComboBox typeBox,
        Image typeIcon,
        TextBlock nicHint,
        TextBox hostnameBox)
    {
        typeIcon.Source = DeviceTypeIconXaml.FromType(typeBox.SelectedItem as DeviceType);
        var type = typeBox.SelectedItem as DeviceType;
        var bluetooth = DeviceTypeCatalog.IsBluetooth(type?.Code, type?.Name);
        nicHint.Text = bluetooth
            ? "蓝牙设备可无 IP；请至少填写主机名或 MAC。多个 IP/MAC 用分号或换行分隔，缺项用 _ 占位。"
            : "多个 IP/MAC 用 ; 或换行按顺序对应。某块网卡没有 IP 或 MAC 时填 _，例如 _ ; 192.168.1.1。第一块为主网卡。";
        hostnameBox.PlaceholderText = bluetooth ? "建议填写，如耳机名称" : "选填";
    }

    private static void SeedDeviceNicRows(
        StackPanel nicsPanel,
        Device? current,
        TextBlock nicHint,
        TextBox vendorBox)
    {
        var existingNics = current?.OrderedNics().ToList() ?? [];
        if (existingNics.Count == 0)
        {
            AppendDeviceNicRow(nicsPanel, nicHint, vendorBox);
            return;
        }

        foreach (var nic in existingNics)
        {
            AppendDeviceNicRow(nicsPanel, nicHint, vendorBox, nic);
        }
    }

    private static void AppendDeviceNicRow(
        StackPanel nicsPanel,
        TextBlock nicHint,
        TextBox vendorBox,
        DeviceNic? nic = null)
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
        nicOui.Click += (_, _) => ApplyNicOuiLookup(nicMacBox, nicHint, vendorBox);
        var remove = new Button { Content = "删除", VerticalAlignment = VerticalAlignment.Bottom };
        remove.Click += (_, _) => RemoveNicRow(nicsPanel, row, nameBox, nicIpBox, nicMacBox);
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

    private static void ApplyNicOuiLookup(TextBox nicMacBox, TextBlock nicHint, TextBox vendorBox)
    {
        var macToken = InventoryService.SplitNicValues(nicMacBox.Text).FirstOrDefault(s => s.Length > 0);
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
    }

    private static void RemoveNicRow(
        StackPanel nicsPanel,
        Grid row,
        TextBox nameBox,
        TextBox nicIpBox,
        TextBox nicMacBox)
    {
        if (nicsPanel.Children.Count > 1)
        {
            nicsPanel.Children.Remove(row);
            return;
        }

        nameBox.Text = "";
        nicIpBox.Text = "";
        nicMacBox.Text = "";
    }

    private static void SeedDeviceDomainRows(StackPanel domainsPanel, Device? current)
    {
        var existingDomains = current?.Domains.Select(d => d.Domain).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
        if (existingDomains.Count == 0)
        {
            AppendDeviceDomainRow(domainsPanel);
            return;
        }

        foreach (var domain in existingDomains)
        {
            AppendDeviceDomainRow(domainsPanel, domain);
        }
    }

    private static void AppendDeviceDomainRow(StackPanel domainsPanel, string? text = null)
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
        remove.Click += (_, _) => RemoveDomainRow(domainsPanel, row, box);
        Grid.SetColumn(remove, 1);
        row.Children.Add(box);
        row.Children.Add(remove);
        domainsPanel.Children.Add(row);
    }

    private static void RemoveDomainRow(StackPanel domainsPanel, Grid row, TextBox box)
    {
        if (domainsPanel.Children.Count > 1)
        {
            domainsPanel.Children.Remove(row);
            return;
        }

        box.Text = "";
    }

    private static List<DeviceNicInput> CollectNicInputs(StackPanel nicsPanel)
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

        return nics;
    }

    private static List<string> CollectDomainInputs(StackPanel domainsPanel)
    {
        var domains = new List<string>();
        foreach (var child in domainsPanel.Children)
        {
            if (child is not Grid grid)
            {
                continue;
            }

            var box = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (box is not null)
            {
                domains.Add(InventoryService.TrimInput(box.Text));
            }
        }

        return domains;
    }

    private static DeviceEditInput BuildDeviceEditInput(DevicePromptFields fields) => new()
    {
        Code = InventoryService.TrimInput(fields.CodeBox.Text),
        Hostname = InventoryService.TrimInput(fields.HostnameBox.Text),
        Vendor = InventoryService.TrimInput(fields.VendorBox.Text),
        Brand = InventoryService.TrimInput(fields.BrandBox.Text),
        Nics = CollectNicInputs(fields.NicsPanel),
        Model = InventoryService.TrimInput(fields.ModelBox.Text),
        Description = InventoryService.TrimInput(fields.DescriptionBox.Text, keepLineBreaks: true),
        OsType = InventoryService.TrimInput(fields.OsBox.Text),
        OpenPorts = InventoryService.TrimInput(fields.PortsBox.Text),
        DeviceInfo = InventoryService.TrimInput(fields.InfoBox.Text, keepLineBreaks: true),
        Remarks = InventoryService.TrimInput(fields.RemarksBox.Text, keepLineBreaks: true),
        DeviceTypeId = (fields.TypeBox.SelectedItem as DeviceType)?.Id,
        DeviceGroupId = (fields.GroupBox.SelectedItem as DeviceGroup)?.Id,
        Domains = CollectDomainInputs(fields.DomainsPanel)
    };

    private sealed class DevicePromptFields
    {
        public required TextBox CodeBox { get; init; }
        public required TextBox HostnameBox { get; init; }
        public required TextBox VendorBox { get; init; }
        public required TextBox BrandBox { get; init; }
        public required TextBox ModelBox { get; init; }
        public required TextBox DescriptionBox { get; init; }
        public required TextBox OsBox { get; init; }
        public required TextBox PortsBox { get; init; }
        public required TextBox InfoBox { get; init; }
        public required TextBox RemarksBox { get; init; }
        public required ComboBox TypeBox { get; init; }
        public required ComboBox GroupBox { get; init; }
        public required StackPanel NicsPanel { get; init; }
        public required StackPanel DomainsPanel { get; init; }
    }
}
