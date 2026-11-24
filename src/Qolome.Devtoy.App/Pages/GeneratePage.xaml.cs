using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;
using Windows.System;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class GeneratePage : Page
{
    private readonly DevtoySession _session = AppServices.Session;

    private List<ModuleEntry> _javaModules = [];

    private IReadOnlyList<TableListItem> _allTables = Array.Empty<TableListItem>();

    private readonly HashSet<string> _checkedTableNames = new(StringComparer.OrdinalIgnoreCase);

    private bool _suppressHeaderSync;

    private bool _suppressModuleSuggest;

    public GeneratePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ReloadModules();
            RestoreHabit();
        };
        Unloaded += (_, _) => _session.ModulesChanged -= OnModulesChanged;
        _session.ModulesChanged += OnModulesChanged;
    }

    private void OnModulesChanged()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ReloadModules();
        }
        else
        {
            DispatcherQueue.TryEnqueue(ReloadModules);
        }
    }

    private void ReloadModules()
    {
        // 仅展示已启用「代码生成」的 Java 项目
        _javaModules = ModuleCatalogFilter.ForJavaCodegen(_session.Modules).ToList();

        var selected = _session.SelectedModule;
        if (selected is null
            || !selected.ProjectTypes.Java
            || !selected.EnableCodegen
            || selected.IsIgnored
            || _javaModules.All(m => m.ModulePath != selected.ModulePath))
        {
            if (selected is not null
                && (!selected.ProjectTypes.Java || !selected.EnableCodegen || selected.IsIgnored))
            {
                _session.SelectedModule = null;
            }

            selected = _session.SelectedModule;
        }

        _suppressModuleSuggest = true;
        ModuleSuggest.ItemsSource = _javaModules;
        if (selected is not null)
        {
            var match = _javaModules.FirstOrDefault(m => m.ModulePath == selected.ModulePath);
            ModuleSuggest.Text = match?.TargetModuleDisplay ?? "";
            _session.SelectedModule = match;
            if (match is not null)
            {
                ApplyModulePrefs(match);
            }
        }
        else
        {
            ModuleSuggest.Text = "";
            // 未选输出项目时不展示历史模块名
            ModelNameBox.Text = "";
        }

        _suppressModuleSuggest = false;
    }

    private void RestoreHabit()
    {
        var habit = _session.Settings.CodegenHabit;
        TaskDao.IsChecked = habit.Dao;
        TaskService.IsChecked = habit.Service;
        TaskController.IsChecked = habit.Controller;

        if (string.IsNullOrWhiteSpace(habit.LastModulePath))
        {
            ModelNameBox.Text = "";
            return;
        }

        var match = _javaModules.FirstOrDefault(m =>
            string.Equals(
                Path.GetFullPath(m.ModulePath),
                Path.GetFullPath(habit.LastModulePath),
                StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            // 上次模块已不在「代码生成」目标列表中：不回填模块名
            ModelNameBox.Text = "";
            return;
        }

        _session.SelectedModule = match;
        _suppressModuleSuggest = true;
        ModuleSuggest.Text = match.TargetModuleDisplay;
        _suppressModuleSuggest = false;
        ApplyModulePrefs(match);
        if (string.IsNullOrWhiteSpace(ModelNameBox.Text)
            && !string.IsNullOrWhiteSpace(habit.LastModelName))
        {
            ModelNameBox.Text = habit.LastModelName;
        }
    }

    private void ApplyModulePrefs(ModuleEntry entry)
    {
        ModelNameBox.Text = entry.CodegenModelName ?? "";
        if (entry.HasCodegenPrefs)
        {
            TaskDao.IsChecked = entry.CodegenDao;
            TaskService.IsChecked = entry.CodegenService;
            TaskController.IsChecked = entry.CodegenController;
        }
    }

    private async void OnSaveHabit(object sender, RoutedEventArgs e)
    {
        if (_session.SelectedModule is null)
        {
            _session.LogWarn("请先选择输出项目再保存习惯");
            return;
        }

        var path = _session.SelectedModule.ModulePath;
        await UiBusy.RunAsync(
                "正在保存代码生成习惯…",
                () => _session.SaveModuleCodegenPrefsAsync(
                    path,
                    ModelNameBox.Text,
                    TaskDao.IsChecked == true,
                    TaskService.IsChecked == true,
                    TaskController.IsChecked == true))
            .ConfigureAwait(true);
    }

    private const int ModuleSuggestEmptyLimit = 10;

    private void OnModuleSuggestGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not AutoSuggestBox box || _suppressModuleSuggest)
        {
            return;
        }

        RefreshModuleSuggestions(box);
    }

    private void OnModuleSuggestTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_suppressModuleSuggest || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        RefreshModuleSuggestions(sender);
    }

    private void RefreshModuleSuggestions(AutoSuggestBox sender)
    {
        var keyword = sender.Text?.Trim() ?? "";
        List<ModuleEntry> filtered;
        if (keyword.Length == 0)
        {
            // 无输入时点击/聚焦：默认展示前 10 条
            filtered = _javaModules.Take(ModuleSuggestEmptyLimit).ToList();
        }
        else
        {
            filtered = _javaModules
                .Where(m =>
                    m.TargetModuleDisplay.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || m.ProjectNameDisplay.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || m.ModulePath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();
        }

        sender.ItemsSource = filtered;
    }

    private void OnModuleSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not ModuleEntry entry)
        {
            return;
        }

        SelectModule(entry, sender);
    }

    private void OnModuleQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is ModuleEntry chosen)
        {
            SelectModule(chosen, sender);
            return;
        }

        var keyword = (args.QueryText ?? sender.Text)?.Trim() ?? "";
        var match = _javaModules.FirstOrDefault(m =>
            string.Equals(m.TargetModuleDisplay, keyword, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.ProjectNameDisplay, keyword, StringComparison.OrdinalIgnoreCase)
            || m.TargetModuleDisplay.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        SelectModule(match, sender);
    }

    private void SelectModule(ModuleEntry entry, AutoSuggestBox sender)
    {
        _session.SelectedModule = entry;
        _suppressModuleSuggest = true;
        sender.Text = entry.TargetModuleDisplay;
        _suppressModuleSuggest = false;
        ApplyModulePrefs(entry);
    }

    private async void OnLoadTables(object sender, RoutedEventArgs e) =>
        await QueryTablesAsync().ConfigureAwait(true);

    private void OnTableSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        _ = QueryTablesAsync();
    }

    private async Task QueryTablesAsync()
    {
        var tables = await UiBusy.RunAsync("正在读取表列表…", () => _session.ListTablesAsync())
            .ConfigureAwait(true);
        _allTables = tables;
        ApplyTableFilter();
    }

    private void ApplyTableFilter()
    {
        var keyword = TableSearchBox.Text?.Trim() ?? "";
        IEnumerable<TableListItem> filtered = _allTables;
        if (keyword.Length > 0)
        {
            filtered = _allTables.Where(t =>
                t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered
            .Select(t => new SelectableTableRow(t, _checkedTableNames.Contains(t.Name)))
            .ToList();
        TableList.ItemsSource = rows;
        SyncHeaderSelectAll();
        if (keyword.Length > 0)
        {
            _session.Log($"表搜索「{keyword}」：{rows.Count} / {_allTables.Count}");
        }
    }

    private void OnHeaderSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressHeaderSync || sender is not CheckBox box)
        {
            return;
        }

        if (box.IsChecked == true)
        {
            SetVisibleRowsChecked(true);
        }
        else if (box.IsChecked == false)
        {
            SetVisibleRowsChecked(false);
        }
    }

    private void SetVisibleRowsChecked(bool isChecked)
    {
        if (TableList.ItemsSource is not IEnumerable<SelectableTableRow> rows)
        {
            return;
        }

        foreach (var row in rows)
        {
            row.IsSelected = isChecked;
            if (isChecked)
            {
                _checkedTableNames.Add(row.Name);
            }
            else
            {
                _checkedTableNames.Remove(row.Name);
            }
        }
    }

    private void OnRowCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: SelectableTableRow row })
        {
            return;
        }

        if (row.IsSelected)
        {
            _checkedTableNames.Add(row.Name);
        }
        else
        {
            _checkedTableNames.Remove(row.Name);
        }

        SyncHeaderSelectAll();
    }

    private void SyncHeaderSelectAll()
    {
        if (TableList.ItemsSource is not IList<SelectableTableRow> rows || rows.Count == 0)
        {
            _suppressHeaderSync = true;
            HeaderSelectAll.IsChecked = false;
            _suppressHeaderSync = false;
            return;
        }

        var selected = rows.Count(r => r.IsSelected);
        _suppressHeaderSync = true;
        if (selected == 0)
        {
            HeaderSelectAll.IsChecked = false;
        }
        else if (selected >= rows.Count)
        {
            HeaderSelectAll.IsChecked = true;
        }
        else
        {
            HeaderSelectAll.IsChecked = null;
        }

        _suppressHeaderSync = false;
    }

    private bool _generating;

    private async void OnGenerate(object sender, RoutedEventArgs e)
    {
        if (_generating)
        {
            return;
        }

        var tasks = new List<string>();
        if (TaskDao.IsChecked == true)
        {
            tasks.Add("dao");
        }

        if (TaskService.IsChecked == true)
        {
            tasks.Add("service");
        }

        if (TaskController.IsChecked == true)
        {
            tasks.Add("controller");
        }

        if (tasks.Count == 0)
        {
            _session.LogWarn("请至少勾选 Dao、Service 或 Controller 之一");
            return;
        }

        if (_session.SelectedModule is null)
        {
            _session.LogWarn("请先选择输出项目");
            return;
        }

        var tables = (TableList.ItemsSource as IEnumerable<SelectableTableRow>)?
            .Where(r => r.IsSelected)
            .Select(r => r.Name)
            .ToList() ?? [];
        if (tables.Count == 0)
        {
            _session.LogWarn("请先查询并勾选要生成的表");
            return;
        }

        _generating = true;
        SetGenerateBusy(true);
        try
        {
            var result = await UiBusy.RunAsync(
                    "正在生成代码…",
                    () => _session.RunCodegenAsync(tasks, tables, null, ModelNameBox.Text))
                .ConfigureAwait(true);
            if (result.Success)
            {
                var module = _session.SelectedModule?.ProjectNameDisplay ?? "";
                var dialog = new ContentDialog
                {
                    Title = "生成完成",
                    Content = $"代码已生成完成。\n模块：{module}\n表：{tables.Count} 张",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                await dialog.ShowWithIconsAsync();
            }
            else
            {
                var detail = result.Lines.Count > 0
                    ? string.Join("\n", result.Lines)
                    : "未知错误";
                var dialog = new ContentDialog
                {
                    Title = "生成失败",
                    Content = new ScrollViewer
                    {
                        MaxHeight = 420,
                        Content = new TextBlock
                        {
                            Text = detail,
                            TextWrapping = TextWrapping.WrapWholeWords,
                            IsTextSelectionEnabled = true,
                        },
                    },
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                await dialog.ShowWithIconsAsync();
            }
        }
        finally
        {
            _generating = false;
            SetGenerateBusy(false);
        }
    }

    private void SetGenerateBusy(bool busy)
    {
        GenerateBtn.IsEnabled = !busy;
        GenerateBusyRing.IsActive = busy;
        GenerateBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        GenerateIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        GenerateBtnText.Text = busy ? "生成中…" : "生成";
    }

    private sealed partial class SelectableTableRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SelectableTableRow(TableListItem source, bool isSelected)
        {
            Name = source.Name;
            CommentDisplay = source.CommentDisplay;
            _isSelected = isSelected;
        }

        public string Name { get; }

        public string CommentDisplay { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
