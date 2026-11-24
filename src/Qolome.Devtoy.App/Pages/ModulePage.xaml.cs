using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;
using Windows.Storage.Pickers;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class ModulePage : Page
{
    private readonly DevtoySession _session = AppServices.Session;

    private ResizableColumnLayout? _columnLayout;

    private ResizableColumnLayout? _ignoredColumnLayout;

    private bool _suppressCheckSync;

    private bool _suppressFeatureSync;

    private bool _filterCodegenPrefsOnly;

    private string _filterName = "";

    private string _filterType = "all";

    private string _filterTag = "all";

    private bool _suppressListFilterEvents;

    /// <summary>首屏 Loaded/Initialize 完成前忽略 ComboBox 初值 SelectionChanged，避免进页 Reload 风暴。</summary>
    private bool _listFiltersReady;

    private bool _rebuildingTags;

    private DispatcherQueueTimer? _filterNameDebounce;

    private bool IsIgnoredTabActive =>
        ReferenceEquals(ModuleTabs.SelectedItem, IgnoredTab);

    public ModulePage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        _session.ModulesChanged += OnModulesChanged;
        _session.ModulesBusyChanged += OnModulesBusyChanged;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _listFiltersReady = false;
        if (_filterNameDebounce is not null)
        {
            _filterNameDebounce.Stop();
            _filterNameDebounce.Tick -= OnFilterNameDebounceTick;
        }

        _session.ModulesChanged -= OnModulesChanged;
        _session.ModulesBusyChanged -= OnModulesBusyChanged;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SyncModulesBusyOverlay();
        Reload();
        _listFiltersReady = true;

        // 启动时可能已在后台扫描：只展示页面 loading，不套全局 Busy，避免感觉「进不去/卡死」
        if (_session.Settings.WorkspaceRoots.Count > 0
            && _session.Modules.Count == 0
            && !_session.IsModulesBusy)
        {
            _ = _session.RefreshModulesAsync();
        }

        // 延后挂载列宽，避免与首帧列表生成互相触发布局死循环
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, AttachColumnLayouts);
    }

    private void OnModulesBusyChanged()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            SyncModulesBusyOverlay();
        }
        else
        {
            DispatcherQueue.TryEnqueue(SyncModulesBusyOverlay);
        }
    }

    private void SyncModulesBusyOverlay()
    {
        var busy = _session.IsModulesBusy;
        ModulesBusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ModulesBusyMessageText.Text = string.IsNullOrWhiteSpace(_session.ModulesBusyMessage)
            ? "正在处理模块…"
            : _session.ModulesBusyMessage;
        ModuleContentRoot.IsHitTestVisible = !busy;
        ModuleContentRoot.Opacity = busy ? 0.55 : 1;
    }

    private void AttachColumnLayouts()
    {
        if (_columnLayout is null)
        {
            _columnLayout = ResizableColumnLayout.Attach(
                ModuleHeaderGrid,
                ModuleList,
                [
                    new ColumnSpec(0, 36, 40),
                    new ColumnSpec(1.4, 72),
                    new ColumnSpec(0.8, 64),
                    new ColumnSpec(1.8, 96),
                    new ColumnSpec(0.7, 48),
                    new ColumnSpec(0.7, 48),
                    new ColumnSpec(0, 100, 120),
                    new ColumnSpec(0, 200, 240),
                ]);
        }

        if (_ignoredColumnLayout is null)
        {
            _ignoredColumnLayout = ResizableColumnLayout.Attach(
                IgnoredHeaderGrid,
                IgnoredList,
                [
                    new ColumnSpec(0, 36, 40),
                    new ColumnSpec(1.6, 80),
                    new ColumnSpec(1, 72),
                    new ColumnSpec(2.4, 120),
                    new ColumnSpec(0.9, 56),
                    new ColumnSpec(0.9, 56),
                    new ColumnSpec(0, 160, 180),
                ]);
        }
    }

    private void OnModulesChanged()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            Reload();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Reload);
        }
    }

    private void Reload()
    {
        _suppressFeatureSync = true;
        _suppressCheckSync = true;
        try
        {
            IEnumerable<ModuleEntry> activeQuery = _session.Modules.Where(m => !m.IsIgnored);
            if (_filterCodegenPrefsOnly)
            {
                activeQuery = activeQuery.Where(m => m.HasCodegenPrefs);
            }

            activeQuery = ApplyListFilters(activeQuery);
            var active = activeQuery.ToList();
            var ignored = ApplyListFilters(_session.Modules.Where(m => m.IsIgnored)).ToList();
            ModuleList.ItemsSource = active;
            ModuleList.SelectedItems.Clear();
            IgnoredList.ItemsSource = ignored;
            IgnoredList.SelectedItems.Clear();
            IgnoredTab.Header = ignored.Count > 0 ? $"忽略列表 ({ignored.Count})" : "忽略列表";
            FilterCodegenBtnText.Text = _filterCodegenPrefsOnly ? "显示全部模块" : "查询生成配置";
            UpdateBulkActionButtons();
            SyncHeaderSelectAll();
            SyncIgnoredHeaderSelectAll();
        }
        finally
        {
            // 等列表项生成/绑定完成后再允许功能勾选写回，避免进入页面时误触发保存风暴
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    _suppressFeatureSync = false;
                    _suppressCheckSync = false;
                });
        }
    }

    private IEnumerable<ModuleEntry> ApplyListFilters(IEnumerable<ModuleEntry> source)
    {
        var name = _filterName.Trim();
        if (name.Length > 0)
        {
            source = source.Where(m =>
                m.ProjectNameDisplay.Contains(name, StringComparison.OrdinalIgnoreCase)
                || m.ModulePath.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        source = _filterType switch
        {
            "java" => source.Where(m => m.ProjectTypes.Java),
            "web" => source.Where(m => m.ProjectTypes.Web),
            "flutter" => source.Where(m => m.ProjectTypes.Flutter),
            _ => source,
        };

        source = _filterTag switch
        {
            "codegen" => source.Where(m => m.EnableCodegen),
            "enum" => source.Where(m => m.EnableEnum),
            "boot-starter" => source.Where(m => m.TagBootStarter),
            "ts-dict" => source.Where(m => m.EnableTsDict),
            _ => source,
        };

        return source;
    }

    private void OnListFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (!_listFiltersReady || _suppressListFilterEvents)
        {
            return;
        }

        _filterName = FilterNameBox.Text ?? "";
        _filterNameDebounce ??= DispatcherQueue.CreateTimer();
        _filterNameDebounce.Interval = TimeSpan.FromMilliseconds(280);
        _filterNameDebounce.IsRepeating = false;
        _filterNameDebounce.Tick -= OnFilterNameDebounceTick;
        _filterNameDebounce.Tick += OnFilterNameDebounceTick;
        _filterNameDebounce.Stop();
        _filterNameDebounce.Start();
    }

    private void OnFilterNameDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        Reload();
    }

    private void OnListFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_listFiltersReady || _suppressListFilterEvents)
        {
            return;
        }

        var type = ReadComboTag(FilterTypeBox, "all");
        var tag = ReadComboTag(FilterTagBox, "all");
        if (type == _filterType && tag == _filterTag)
        {
            return;
        }

        _filterType = type;
        _filterTag = tag;
        Reload();
    }

    private void OnClearListFilters(object sender, RoutedEventArgs e)
    {
        _suppressListFilterEvents = true;
        try
        {
            FilterNameBox.Text = "";
            FilterTypeBox.SelectedIndex = 0;
            FilterTagBox.SelectedIndex = 0;
            _filterName = "";
            _filterType = "all";
            _filterTag = "all";
        }
        finally
        {
            _suppressListFilterEvents = false;
        }

        Reload();
    }

    private static string ReadComboTag(ComboBox box, string fallback)
    {
        if (box.SelectedItem is ComboBoxItem { Tag: string tag } && !string.IsNullOrWhiteSpace(tag))
        {
            return tag;
        }

        return fallback;
    }

    private void OnFilterCodegenPrefs(object sender, RoutedEventArgs e)
    {
        _filterCodegenPrefsOnly = !_filterCodegenPrefsOnly;
        Reload();
        if (_filterCodegenPrefsOnly)
        {
            _session.Log($"已筛选有代码生成配置的模块：{ModuleList.Items?.Count ?? 0} 个");
        }
    }

    private enum ModuleTagKind
    {
        Codegen,
        Enum,
        BootStarter,
        TsDict,
    }

    private void OnModuleTagsCellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not StackPanel panel)
        {
            return;
        }

        panel.DataContextChanged -= OnModuleTagsDataContextChanged;
        panel.DataContextChanged += OnModuleTagsDataContextChanged;
        RebuildModuleTagsCell(panel);
    }

    private void OnModuleTagsDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_rebuildingTags || sender is not StackPanel panel)
        {
            return;
        }

        RebuildModuleTagsCell(panel);
    }

    private void RebuildModuleTagsCell(StackPanel panel)
    {
        if (_rebuildingTags)
        {
            return;
        }

        if (panel.DataContext is not ModuleEntry entry)
        {
            _rebuildingTags = true;
            try
            {
                panel.Children.Clear();
                panel.Tag = null;
            }
            finally
            {
                _rebuildingTags = false;
            }

            return;
        }

        // 内容未变则跳过，避免 Clear/Add 再次触发 DataContextChanged 死循环
        var stamp = BuildTagCellStamp(entry);
        if (panel.Tag is string prev && prev == stamp && panel.Children.Count > 0)
        {
            return;
        }

        _rebuildingTags = true;
        try
        {
            panel.Children.Clear();
            var defs = GetTagDefs(entry);
            foreach (var def in defs.Where(d => d.IsOn(entry)))
            {
                panel.Children.Add(CreateTagChip(entry, def));
            }

            panel.Children.Add(CreateAddTagButton(entry, defs));
            panel.Tag = stamp;
        }
        finally
        {
            _rebuildingTags = false;
        }
    }

    private static string BuildTagCellStamp(ModuleEntry entry) =>
        string.Join(
            '|',
            entry.ModulePath,
            entry.IsJavaType,
            entry.IsWebType,
            entry.EnableCodegen,
            entry.EnableEnum,
            entry.TagBootStarter,
            entry.EnableTsDict);

    private static IReadOnlyList<TagDef> GetTagDefs(ModuleEntry entry) =>
    [
        new TagDef(
            ModuleTagKind.Codegen,
            "代码生成",
            null,
            e => e.IsJavaType,
            e => e.EnableCodegen),
        new TagDef(
            ModuleTagKind.Enum,
            "生成枚举",
            null,
            e => e.IsJavaType,
            e => e.EnableEnum),
        new TagDef(
            ModuleTagKind.BootStarter,
            "boot-starter",
            "存在 AutoConfiguration.imports 时自动识别；生成 Controller/Service 时写入 imports 并生成 Convert",
            e => e.IsJavaType,
            e => e.TagBootStarter),
        new TagDef(
            ModuleTagKind.TsDict,
            "生成字典",
            null,
            e => e.IsWebType,
            e => e.EnableTsDict),
    ];

    private Border CreateTagChip(ModuleEntry entry, TagDef def)
    {
        var label = new TextBlock
        {
            Text = def.Label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var remove = new Button
        {
            Padding = new Thickness(2),
            MinWidth = 0,
            MinHeight = 0,
            Background = null,
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 10,
            },
            Tag = (entry, def.Kind),
        };
        ToolTipService.SetToolTip(remove, "移除标签");
        remove.Click += OnRemoveModuleTagClick;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(label);
        row.Children.Add(remove);

        var chip = new Border
        {
            Child = row,
            Padding = new Thickness(8, 2, 4, 2),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush("ControlStrokeColorDefaultBrush"),
            Background = ResolveBrush("ControlFillColorDefaultBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (!string.IsNullOrWhiteSpace(def.Tip))
        {
            ToolTipService.SetToolTip(chip, def.Tip);
        }

        return chip;
    }

    private Button CreateAddTagButton(ModuleEntry entry, IReadOnlyList<TagDef> defs)
    {
        var flyout = new MenuFlyout();
        var available = defs.Where(d => d.Available(entry) && !d.IsOn(entry)).ToList();
        foreach (var def in available)
        {
            var item = new MenuFlyoutItem
            {
                Text = def.Label,
                Tag = (entry, def.Kind),
            };
            if (!string.IsNullOrWhiteSpace(def.Tip))
            {
                ToolTipService.SetToolTip(item, def.Tip);
            }

            item.Click += OnAddModuleTagClick;
            flyout.Items.Add(item);
        }

        var add = new Button
        {
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 28,
            MinHeight = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = null,
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush("ControlStrokeColorDefaultBrush"),
            CornerRadius = new CornerRadius(4),
            Content = new FontIcon { Glyph = "\uE710", FontSize = 12 },
            Flyout = available.Count > 0 ? flyout : null,
            IsEnabled = available.Count > 0,
        };
        ToolTipService.SetToolTip(add, available.Count > 0 ? "添加标签" : "无可添加标签");
        return add;
    }

    private static Brush ResolveBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private async void OnAddModuleTagClick(object sender, RoutedEventArgs e)
    {
        if (_suppressFeatureSync || sender is not MenuFlyoutItem { Tag: (ModuleEntry entry, ModuleTagKind kind) })
        {
            return;
        }

        await SetModuleTagAsync(entry, kind, enabled: true).ConfigureAwait(true);
    }

    private async void OnRemoveModuleTagClick(object sender, RoutedEventArgs e)
    {
        if (_suppressFeatureSync || sender is not Button { Tag: (ModuleEntry entry, ModuleTagKind kind) })
        {
            return;
        }

        await SetModuleTagAsync(entry, kind, enabled: false).ConfigureAwait(true);
    }

    private Task SetModuleTagAsync(ModuleEntry entry, ModuleTagKind kind, bool enabled) =>
        UiBusy.RunAsync(
            "正在保存标签…",
            () => kind switch
            {
                ModuleTagKind.Codegen => _session.SaveModuleFeatureFlagsAsync(
                    entry.ModulePath,
                    enableCodegen: enabled),
                ModuleTagKind.Enum => _session.SaveModuleFeatureFlagsAsync(
                    entry.ModulePath,
                    enableEnum: enabled),
                ModuleTagKind.BootStarter => _session.SaveModuleFeatureFlagsAsync(
                    entry.ModulePath,
                    tagBootStarter: enabled),
                ModuleTagKind.TsDict => _session.SaveModuleFeatureFlagsAsync(
                    entry.ModulePath,
                    enableTsDict: enabled),
                _ => Task.CompletedTask,
            });

    private sealed record TagDef(
        ModuleTagKind Kind,
        string Label,
        string? Tip,
        Func<ModuleEntry, bool> Available,
        Func<ModuleEntry, bool> IsOn);

    private async void OnEditCodegenPrefs(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        var modelBox = new TextBox
        {
            Text = entry.CodegenModelName ?? "",
            PlaceholderText = "Java ModelName / package 模块段",
        };
        var daoBox = new CheckBox { Content = "Dao（Entity / Mapper / XML）", IsChecked = entry.CodegenDao };
        var serviceBox = new CheckBox { Content = "Service", IsChecked = entry.CodegenService };
        var controllerBox = new CheckBox { Content = "Controller", IsChecked = entry.CodegenController };

        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(new TextBlock
        {
            Text = entry.TargetModuleDisplay,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        form.Children.Add(new TextBlock
        {
            Text = entry.ModulePath,
            Opacity = 0.75,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        form.Children.Add(Labeled("模块名", modelBox));
        form.Children.Add(new TextBlock { Text = "默认生成项", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        form.Children.Add(daoBox);
        form.Children.Add(serviceBox);
        form.Children.Add(controllerBox);

        var dialog = new ContentDialog
        {
            Title = "Java 生成配置",
            Content = form,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await UiBusy.RunAsync(
                "正在保存代码生成配置…",
                () => _session.SaveModuleCodegenPrefsAsync(
                    entry.ModulePath,
                    modelBox.Text,
                    daoBox.IsChecked == true,
                    serviceBox.IsChecked == true,
                    controllerBox.IsChecked == true))
            .ConfigureAwait(true);
    }

    private async void OnEditEnumPath(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        var globalDefault = _session.Settings.ResolveEnumPackagePath();
        var pathBox = new TextBox
        {
            Text = entry.EnumPackagePath ?? "",
            PlaceholderText = "留空则用全局: " + globalDefault,
        };
        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(new TextBlock
        {
            Text = entry.TargetModuleDisplay,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        form.Children.Add(Labeled("枚举包路径", pathBox));
        form.Children.Add(new TextBlock
        {
            Text = "全局默认: " + globalDefault,
            Opacity = 0.7,
            TextWrapping = TextWrapping.WrapWholeWords,
        });

        var dialog = new ContentDialog
        {
            Title = "枚举路径",
            Content = form,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await UiBusy.RunAsync(
                "正在保存枚举路径…",
                () => _session.SaveModuleEnumPathAsync(entry.ModulePath, pathBox.Text))
            .ConfigureAwait(true);
    }

    private async void OnEditTsDictPath(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        var globalDefault = _session.Settings.ResolveTsDictPath();
        var pathBox = new TextBox
        {
            Text = entry.TsDictPath ?? "",
            PlaceholderText = "留空则用全局: " + globalDefault,
        };
        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(new TextBlock
        {
            Text = entry.TargetModuleDisplay,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        form.Children.Add(Labeled("TS 字典相对路径", pathBox));
        form.Children.Add(new TextBlock
        {
            Text = "全局默认: " + globalDefault,
            Opacity = 0.7,
            TextWrapping = TextWrapping.WrapWholeWords,
        });

        var dialog = new ContentDialog
        {
            Title = "字典路径",
            Content = form,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await UiBusy.RunAsync(
                "正在保存字典路径…",
                () => _session.SaveModuleTsDictPathAsync(entry.ModulePath, pathBox.Text))
            .ConfigureAwait(true);
    }

    private void OnModuleTabChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBulkActionButtons();
        if (IsIgnoredTabActive)
        {
            SyncIgnoredHeaderSelectAll();
            SyncVisibleIgnoredRowChecks();
        }
        else
        {
            SyncHeaderSelectAll();
            SyncVisibleRowChecks();
        }
    }

    private void UpdateBulkActionButtons()
    {
        var selected = GetSelectedModules().Count;
        DeleteSelectedBtn.IsEnabled = selected > 0;
        var currentCount = IsIgnoredTabActive
            ? (IgnoredList.Items?.Count ?? 0)
            : (ModuleList.Items?.Count ?? 0);
        ClearAllBtn.IsEnabled = currentCount > 0;
    }

    private ListView ActiveList => IsIgnoredTabActive ? IgnoredList : ModuleList;

    private List<ModuleEntry> GetSelectedModules() =>
        ActiveList.SelectedItems?.OfType<ModuleEntry>().ToList() ?? new List<ModuleEntry>();

    private List<ModuleEntry> GetCurrentTabModules() =>
        ActiveList.Items?.OfType<ModuleEntry>().ToList() ?? new List<ModuleEntry>();

    private async void OnPickWorkspace(object sender, RoutedEventArgs e)
    {
        if (_session.IsModulesBusy)
        {
            return;
        }

        var path = await PickFolderAsync();
        if (path is null)
        {
            return;
        }

        await _session.AddWorkspaceAsync(path).ConfigureAwait(true);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_session.IsModulesBusy)
        {
            return;
        }

        await _session.RefreshModulesAsync(busyMessage: "正在刷新项目…").ConfigureAwait(true);
    }

    private async void OnAddExternal(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is null)
        {
            return;
        }

        var recognition = await UiBusy.RunBackgroundAsync(
                "正在识别工程…",
                () => ProjectRecognizer.Recognize(path))
            .ConfigureAwait(true);
        if (!await ShowModuleEditorAsync(path, recognition, isEdit: false).ConfigureAwait(true))
        {
            return;
        }
    }

    private void OnModulePathDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ModuleEntry entry })
        {
            return;
        }

        if (!TryOpenInExplorer(entry.ModulePath))
        {
            _session.LogError($"无法打开目录: {entry.ModulePath}");
        }

        e.Handled = true;
    }

    private static bool TryOpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        path = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(path))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
        return true;
    }

    private async void OnEditModule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        if (entry.IsSpecified)
        {
            var spec = _session.FindSpecifiedProject(entry.ModulePath);
            if (spec is null)
            {
                _session.LogWarn("未找到指定工程配置");
                return;
            }

            var recognition = new ProjectRecognition
            {
                ProjectRoot = entry.ModulePath,
                AutoRecognized = !spec.Manual,
                Types = new ProjectTypes
                {
                    Java = spec.Java,
                    Web = spec.Web || spec.JavaScript,
                    Flutter = spec.Flutter,
                },
                Version = spec.Version ?? entry.ProjectVersion,
                KindHint = "已登记",
            };
            if (!await ShowModuleEditorAsync(entry.ModulePath, recognition, isEdit: true).ConfigureAwait(true))
            {
                return;
            }
        }
        else
        {
            if (!await ShowWorkspaceModuleEditorAsync(entry))
            {
                return;
            }
        }

        Reload();
    }

    private async void OnIgnoreModule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        await _session
            .RunModulesWorkAsync("正在忽略模块…", () => _session.SetModuleIgnoredAsync(entry.ModulePath, true))
            .ConfigureAwait(true);
    }

    private async void OnUnignoreModule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        await _session
            .RunModulesWorkAsync("正在取消忽略…", () => _session.SetModuleIgnoredAsync(entry.ModulePath, false))
            .ConfigureAwait(true);
    }

    private async void OnDeleteModule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModuleEntry entry })
        {
            return;
        }

        if (!await ConfirmRemoveModulesAsync([entry]).ConfigureAwait(true))
        {
            return;
        }

        await _session
            .RunModulesWorkAsync("正在删除模块…", () => _session.RemoveModulesAsync([entry]))
            .ConfigureAwait(true);
    }

    private async void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedModules();
        if (selected.Count == 0)
        {
            return;
        }

        if (!await ConfirmRemoveModulesAsync(selected).ConfigureAwait(true))
        {
            return;
        }

        await _session
            .RunModulesWorkAsync("正在删除选中模块…", () => _session.RemoveModulesAsync(selected))
            .ConfigureAwait(true);
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        var all = GetCurrentTabModules();
        if (all.Count == 0)
        {
            return;
        }

        if (!await ConfirmRemoveModulesAsync(all, isClearAll: true).ConfigureAwait(true))
        {
            return;
        }

        await _session
            .RunModulesWorkAsync("正在清空列表…", () => _session.RemoveModulesAsync(all))
            .ConfigureAwait(true);
    }

    private async Task<bool> ShowWorkspaceModuleEditorAsync(ModuleEntry entry)
    {
        var path = entry.ModulePath;
        _session.Settings.ModuleOverrides.TryGetValue(path, out var ov);
        var displayName = entry.CatalogDisplayName
            ?? (ov?.DisplayName)
            ?? entry.ProjectNameDisplay;
        var version = ov?.Version ?? entry.ProjectVersion;
        var types = new ProjectTypes
        {
            Java = ov?.Java ?? entry.ProjectTypes.Java,
            Web = (ov?.Web ?? entry.ProjectTypes.Web) || (ov?.JavaScript == true),
            Flutter = ov?.Flutter ?? entry.ProjectTypes.Flutter,
        };
        var (javaBox, webBox, flutterBox) = CreateTypeCheckBoxes(types);

        var nameBox = new TextBox { Text = displayName, PlaceholderText = "显示名称（可选）" };
        var versionBox = new TextBox { Text = version, PlaceholderText = "版本号" };
        var pathText = new TextBlock { Text = path, TextWrapping = TextWrapping.WrapWholeWords, Opacity = 0.75 };

        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(new TextBlock { Text = "工作区扫描模块", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        form.Children.Add(pathText);
        form.Children.Add(Labeled("显示名称", nameBox));
        form.Children.Add(Labeled("版本号", versionBox));
        form.Children.Add(new TextBlock { Text = "类型", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        form.Children.Add(javaBox);
        form.Children.Add(webBox);
        form.Children.Add(flutterBox);

        var dialog = new ContentDialog
        {
            Title = "修改模块",
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

        if (!TryReadTypesFromCheckBoxes(javaBox, webBox, flutterBox, out var picked))
        {
            _session.LogWarn("请至少勾选一种类型：Java / Web / Flutter");
            return false;
        }

        await UiBusy.RunAsync(
                "正在保存模块…",
                () => _session.UpdateWorkspaceModuleOverrideAsync(
                    path,
                    nameBox.Text.Trim(),
                    versionBox.Text.Trim(),
                    picked))
            .ConfigureAwait(true);
        return true;
    }

    private async Task<bool> ShowModuleEditorAsync(
        string path,
        ProjectRecognition recognition,
        bool isEdit)
    {
        var (javaBox, webBox, flutterBox) = CreateTypeCheckBoxes(recognition.Types);
        var versionBox = new TextBox
        {
            Header = "版本号",
            Text = recognition.Version,
            PlaceholderText = "1.0.0",
        };
        var pathText = new TextBlock
        {
            Text = path,
            TextWrapping = TextWrapping.WrapWholeWords,
            Opacity = 0.8,
        };
        var hint = new TextBlock
        {
            Text = recognition.AutoRecognized
                ? $"已识别：{recognition.KindHint}"
                : "未自动识别，请手动勾选工程类型并填写版本。",
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(new TextBlock { Text = "项目根目录", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        form.Children.Add(pathText);
        form.Children.Add(hint);
        form.Children.Add(new TextBlock { Text = "类型", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        form.Children.Add(javaBox);
        form.Children.Add(webBox);
        form.Children.Add(flutterBox);
        form.Children.Add(versionBox);

        var dialog = new ContentDialog
        {
            Title = isEdit ? "修改指定工程" : "添加指定工程",
            Content = form,
            PrimaryButtonText = isEdit ? "保存" : "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        if (!TryReadTypesFromCheckBoxes(javaBox, webBox, flutterBox, out var picked))
        {
            _session.LogWarn("请至少勾选一种类型：Java / Web / Flutter");
            return false;
        }

        var manual = !recognition.AutoRecognized
            || picked.Java != recognition.Types.Java
            || picked.Web != recognition.Types.Web
            || picked.Flutter != recognition.Types.Flutter
            || versionBox.Text.Trim() != recognition.Version.Trim();
        var spec = new SpecifiedProjectEntry
        {
            Root = path,
            Java = picked.Java,
            Web = picked.Web,
            Flutter = picked.Flutter,
            Version = string.IsNullOrWhiteSpace(versionBox.Text) ? null : versionBox.Text.Trim(),
            Manual = manual,
        };

        if (isEdit)
        {
            await UiBusy.RunAsync("正在保存指定工程…", () => _session.UpdateSpecifiedProjectAsync(spec))
                .ConfigureAwait(true);
        }
        else
        {
            await UiBusy.RunAsync("正在添加指定工程…", () => _session.AddSpecifiedProjectAsync(spec))
                .ConfigureAwait(true);
        }

        return true;
    }

    private static (CheckBox Java, CheckBox Web, CheckBox Flutter) CreateTypeCheckBoxes(ProjectTypes types)
    {
        return (
            new CheckBox { Content = "Java", IsChecked = types.Java },
            new CheckBox { Content = "Web", IsChecked = types.Web },
            new CheckBox { Content = "Flutter", IsChecked = types.Flutter });
    }

    private static bool TryReadTypesFromCheckBoxes(
        CheckBox javaBox,
        CheckBox webBox,
        CheckBox flutterBox,
        out ProjectTypes types)
    {
        types = new ProjectTypes
        {
            Java = javaBox.IsChecked == true,
            Web = webBox.IsChecked == true,
            Flutter = flutterBox.IsChecked == true,
        };
        return types.Java || types.Web || types.Flutter;
    }

    private static UIElement Labeled(string label, TextBox field)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        return grid;
    }

    private static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBulkActionButtons();
        SyncHeaderSelectAll();
        SyncVisibleRowChecks();
        LogSelection(ModuleList);
    }

    private void OnIgnoredSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBulkActionButtons();
        SyncIgnoredHeaderSelectAll();
        SyncVisibleIgnoredRowChecks();
        LogSelection(IgnoredList);
    }

    private void LogSelection(ListView list)
    {
        var selected = list.SelectedItems?.OfType<ModuleEntry>().ToList() ?? [];
        if (selected.Count == 0)
        {
            return;
        }

        if (!IsIgnoredTabActive && list == ModuleList)
        {
            _session.SelectedModule = selected[^1];
        }

        if (selected.Count == 1)
        {
            var entry = selected[0];
            _session.Log($"已选模块: {entry.ProjectNameDisplay} ({entry.VersionDisplay}, {entry.TypeDisplay})");
        }
        else
        {
            _session.Log($"已选 {selected.Count} 个模块");
        }
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e) =>
        ApplySelectAll(HeaderSelectAll, ModuleList, sender);

    private void OnIgnoredSelectAllChanged(object sender, RoutedEventArgs e) =>
        ApplySelectAll(IgnoredHeaderSelectAll, IgnoredList, sender);

    private void ApplySelectAll(CheckBox header, ListView list, object sender)
    {
        if (_suppressCheckSync || sender is not CheckBox box || !ReferenceEquals(box, header))
        {
            return;
        }

        if (box.IsChecked == true)
        {
            SelectAllInList(list);
        }
        else if (box.IsChecked == false)
        {
            list.SelectedItems.Clear();
        }
    }

    private static void SelectAllInList(ListView list)
    {
        list.SelectedItems.Clear();
        foreach (var item in list.Items)
        {
            list.SelectedItems.Add(item);
        }
    }

    private void OnRowCheckLoaded(object sender, RoutedEventArgs e) =>
        SyncRowCheckLoaded(ModuleList, sender);

    private void OnIgnoredRowCheckLoaded(object sender, RoutedEventArgs e) =>
        SyncRowCheckLoaded(IgnoredList, sender);

    private void SyncRowCheckLoaded(ListView list, object sender)
    {
        if (sender is not CheckBox { Tag: ModuleEntry entry } box)
        {
            return;
        }

        _suppressCheckSync = true;
        box.IsChecked = list.SelectedItems.Contains(entry);
        _suppressCheckSync = false;
    }

    private void OnRowCheckChanged(object sender, RoutedEventArgs e) =>
        SyncRowCheckChanged(ModuleList, sender);

    private void OnIgnoredRowCheckChanged(object sender, RoutedEventArgs e) =>
        SyncRowCheckChanged(IgnoredList, sender);

    private void SyncRowCheckChanged(ListView list, object sender)
    {
        if (_suppressCheckSync || sender is not CheckBox { Tag: ModuleEntry entry } box)
        {
            return;
        }

        if (box.IsChecked == true)
        {
            if (!list.SelectedItems.Contains(entry))
            {
                list.SelectedItems.Add(entry);
            }
        }
        else
        {
            list.SelectedItems.Remove(entry);
        }
    }

    private void SyncHeaderSelectAll() =>
        SyncHeaderCheck(HeaderSelectAll, ModuleList);

    private void SyncIgnoredHeaderSelectAll() =>
        SyncHeaderCheck(IgnoredHeaderSelectAll, IgnoredList);

    private void SyncHeaderCheck(CheckBox header, ListView list)
    {
        var total = list.Items?.Count ?? 0;
        var selected = list.SelectedItems?.Count ?? 0;
        _suppressCheckSync = true;
        if (total == 0 || selected == 0)
        {
            header.IsChecked = false;
        }
        else if (selected >= total)
        {
            header.IsChecked = true;
        }
        else
        {
            header.IsChecked = null;
        }

        _suppressCheckSync = false;
    }

    private void SyncVisibleRowChecks() => SyncVisibleChecks(ModuleList);

    private void SyncVisibleIgnoredRowChecks() => SyncVisibleChecks(IgnoredList);

    private void SyncVisibleChecks(ListView list)
    {
        if (list.ItemsSource is null)
        {
            return;
        }

        _suppressCheckSync = true;
        foreach (var item in list.Items)
        {
            if (item is not ModuleEntry entry
                || list.ContainerFromItem(entry) is not ListViewItem container)
            {
                continue;
            }

            if (FindRowCheckBox(container) is { } box)
            {
                box.IsChecked = list.SelectedItems.Contains(entry);
            }
        }

        _suppressCheckSync = false;
    }

    /// <summary>行首选择框：无 Content；功能启用框带文字，避免误同步。</summary>
    private static CheckBox? FindRowCheckBox(DependencyObject root)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is CheckBox { Tag: ModuleEntry, Content: null } box)
            {
                return box;
            }

            var nested = FindRowCheckBox(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private async Task<bool> ConfirmRemoveModulesAsync(
        IReadOnlyList<ModuleEntry> entries,
        bool isClearAll = false)
    {
        var count = entries.Count;
        var specified = entries.Count(e => e.IsSpecified);
        var workspace = count - specified;
        var scope = isClearAll ? "清空当前模块列表" : $"选中删除 {count} 个模块";
        var detail = BuildRemoveDetail(specified, workspace);
        var names = string.Join(
            "\n",
            entries.Take(8).Select(e => $"· {e.ProjectNameDisplay}"));
        if (count > 8)
        {
            names += $"\n… 另有 {count - 8} 项";
        }

        var firstMessage = isClearAll
            ? $"确定{scope}（共 {count} 项）？\n{detail}\n\n{names}"
            : $"确定{scope}？\n{detail}\n\n{names}";

        if (!await ConfirmDeleteAsync("删除模块", firstMessage, BuildSecondRemoveMessage(count, isClearAll))
                .ConfigureAwait(true))
        {
            return false;
        }

        return true;
    }

    private static string BuildRemoveDetail(int specified, int workspace)
    {
        var parts = new List<string>();
        if (specified > 0)
        {
            parts.Add($"指定工程 {specified} 个将从配置中移除");
        }

        if (workspace > 0)
        {
            parts.Add($"工作区模块 {workspace} 个仅从当前列表移除（不删磁盘，刷新后可再次出现）");
        }

        return string.Join("；", parts) + "。";
    }

    private static string BuildSecondRemoveMessage(int count, bool isClearAll)
    {
        var verb = isClearAll ? "清空" : "删除";
        return $"再次确认{verb} {count} 个模块？不会删除磁盘上的工程目录。";
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
}