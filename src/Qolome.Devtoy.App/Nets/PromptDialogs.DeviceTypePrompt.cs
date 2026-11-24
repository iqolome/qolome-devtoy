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
        var (colorBox, autoColor, selectColorGroup) = CreateDeviceTypeColorCombo(current);
        var iconPicker = new DeviceTypeIconPicker(current, codeBox, colorBox);
        iconPicker.WireEvents(autoColor, selectColorGroup);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(codeBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(colorBox);
        panel.Children.Add(descBox);
        panel.Children.Add(new TextBlock { Text = "图标库（点击选用；也可导入 SVG）。描边/填充会套用颜色分组。" });
        foreach (var element in iconPicker.PanelChildren)
        {
            panel.Children.Add(element);
        }
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
        BindSaveOnPrimary(dialog, () => save(iconPicker.BuildSaveInput(nameBox, descBox)));
        await dialog.ShowAsync();
    }

    private static (ComboBox Box, DeviceTypeAutoColorFlag AutoColor, Action<string> SelectColorGroup) CreateDeviceTypeColorCombo(
        DeviceType? current)
    {
        var colorBox = new ComboBox
        {
            Header = "颜色分组",
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var selectedColor = DeviceTypeColorGroups.Normalize(current?.ColorGroup, current?.Code);
        var suppressColorChange = false;
        var autoColor = new DeviceTypeAutoColorFlag(current is null);
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
                autoColor.Enabled = false;
            }
        };

        return (colorBox, autoColor, SelectColorGroup);
    }

    private sealed class DeviceTypeAutoColorFlag(bool enabled)
    {
        public bool Enabled { get; set; } = enabled;
    }

    private sealed class DeviceTypeIconPicker
    {
        private readonly DeviceType? _current;
        private readonly TextBox _codeBox;
        private readonly ComboBox _colorBox;
        private string? _pendingIconPath;
        private string? _pendingBuiltinKey;
        private bool _restoreDefault;
        private readonly Image _preview;
        private readonly TextBlock _hint;
        private readonly GridView _library;
        private readonly TextBox _filter;

        public DeviceTypeIconPicker(DeviceType? current, TextBox codeBox, ComboBox colorBox)
        {
            _current = current;
            _codeBox = codeBox;
            _colorBox = colorBox;
            _preview = new Image
            {
                Width = 48,
                Height = 48,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _hint = new TextBlock { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
            _library = new GridView
            {
                MaxHeight = 200,
                MinHeight = 120,
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.Single
            };
            _library.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                """
                <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <ItemsWrapGrid Orientation="Horizontal" ItemWidth="40" ItemHeight="40"/>
                </ItemsPanelTemplate>
                """);
            _filter = new TextBox { PlaceholderText = "筛选图标库，例如 camera、wifi" };
        }

        public IEnumerable<UIElement> PanelChildren
        {
            get
            {
                yield return _preview;
                yield return CreateActionButtons();
                yield return _filter;
                yield return _library;
                yield return _hint;
            }
        }

        public void WireEvents(DeviceTypeAutoColorFlag autoColor, Action<string> selectColorGroup)
        {
            _library.ItemClick += (_, e) => OnLibraryItemClick(e);
            _filter.TextChanged += (_, _) => FillLibrary(_filter.Text);
            FillLibrary(null);
            _colorBox.SelectionChanged += (_, _) =>
            {
                FillLibrary(_filter.Text);
                RefreshPreview();
            };
            _codeBox.TextChanged += (_, _) => OnCodeChanged(autoColor, selectColorGroup);
            RefreshPreview();
        }

        public DeviceTypeEditInput BuildSaveInput(TextBox nameBox, TextBox descBox) => new()
        {
            Code = InventoryService.TrimInput(_codeBox.Text),
            Name = InventoryService.TrimInput(nameBox.Text),
            Description = InventoryService.TrimInput(descBox.Text, keepLineBreaks: true),
            IconSourcePath = _pendingIconPath,
            BuiltinIconKey = _pendingBuiltinKey,
            RestoreDefaultIcon = _restoreDefault,
            ColorGroup = (_colorBox.SelectedItem as ComboBoxItem)?.Tag as string
        };

        private UIElement CreateActionButtons()
        {
            var pick = new Button { Content = "导入 SVG" };
            pick.Click += async (_, _) => await ImportSvgAsync();
            var restore = new Button { Content = "按编码匹配" };
            restore.Click += (_, _) => UseCodeDefaultIcon();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(pick);
            buttons.Children.Add(restore);
            return buttons;
        }

        private async Task ImportSvgAsync()
        {
            var path = await FileDialogs.PickOpenIconAsync();
            if (path is null)
            {
                return;
            }

            _pendingIconPath = path;
            _pendingBuiltinKey = null;
            _restoreDefault = false;
            RefreshPreview();
        }

        private void UseCodeDefaultIcon()
        {
            _pendingIconPath = null;
            _pendingBuiltinKey = null;
            _restoreDefault = true;
            RefreshPreview();
        }

        private void OnLibraryItemClick(ItemClickEventArgs e)
        {
            if (e.ClickedItem is not Image img || img.Tag is not string key)
            {
                return;
            }

            _pendingBuiltinKey = key;
            _pendingIconPath = null;
            _restoreDefault = false;
            RefreshPreview();
        }

        private void OnCodeChanged(DeviceTypeAutoColorFlag autoColor, Action<string> selectColorGroup)
        {
            if (autoColor.Enabled)
            {
                selectColorGroup(DeviceTypeColorGroups.ForCode(_codeBox.Text));
            }

            if (_pendingIconPath is null && _pendingBuiltinKey is null)
            {
                RefreshPreview();
            }
        }

        private string SelectedColorKey() =>
            (_colorBox.SelectedItem as ComboBoxItem)?.Tag as string
            ?? DeviceTypeColorGroups.ForCode(_codeBox.Text);

        private void RefreshPreview()
        {
            var group = SelectedColorKey();
            var hex = DeviceTypeColorGroups.Hex(group, _codeBox.Text);
            if (_restoreDefault)
            {
                _preview.Source = DeviceTypeIconXaml.From(_codeBox.Text, null, false, group);
                _hint.Text = "将按编码使用软件自带 SVG，颜色跟随上方颜色分组。";
                return;
            }

            if (_pendingIconPath is not null)
            {
                _preview.Source = DeviceTypeIconXaml.FromPath(_pendingIconPath, hex);
                _hint.Text = $"将导入自定义 SVG：{Path.GetFileName(_pendingIconPath)}，颜色跟随分组。";
                return;
            }

            if (_pendingBuiltinKey is not null)
            {
                _preview.Source = DeviceTypeIconXaml.From(_pendingBuiltinKey, _pendingBuiltinKey, false, group);
                _hint.Text = $"将使用图标库「{_pendingBuiltinKey}」（随软件分发，不写入备份）。颜色跟随分组。";
                return;
            }

            _preview.Source = _current is null
                ? DeviceTypeIconXaml.From(_codeBox.Text, null, false, group)
                : DeviceTypeIconXaml.From(_current.Code, _current.IconFile, _current.IsCustomIcon, group);
            _hint.Text = _current?.IsCustomIcon == true
                ? "当前为自定义 SVG。设置导出时会一并打包该文件。颜色跟随分组。"
                : "当前为软件自带 SVG，不会写入备份。颜色跟随分组。";
        }

        private void FillLibrary(string? query)
        {
            _library.Items.Clear();
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
                    Source = DeviceTypeIconXaml.FromPath(icon.Path, DeviceTypeColorGroups.Hex(SelectedColorKey(), _codeBox.Text)),
                    Tag = icon.Key,
                    Margin = new Thickness(4)
                };
                ToolTipService.SetToolTip(img, icon.Key);
                _library.Items.Add(img);
            }
        }
    }
}
