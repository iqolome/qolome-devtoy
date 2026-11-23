using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;
using TextControlBoxNS;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class TemplatePage : Page
{
    private readonly DevtoySession _session = AppServices.Session;
    private CodegenTemplateInfo? _current;
    private bool _dirty;
    private bool _suppressTextChanged;

    public TemplatePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            EditorBox.ShowLineNumbers = true;
            EditorBox.ShowLineHighlighter = true;
            EditorBox.EnableSyntaxHighlighting = true;
            EditorBox.SelectSyntaxHighlightingById(SyntaxHighlightID.Java);
            ReloadList();
        };
    }

    private void ReloadList()
    {
        var selectedName = _current?.FileName;
        PathHint.Text =
            $"用户模板：{DevtoyPaths.TemplatesRoot}（首次从随包默认复制，可编辑；还原将覆盖本地修改）";
        var list = _session.Templates.ListTemplates();
        TemplateList.ItemsSource = list;
        if (!string.IsNullOrEmpty(selectedName))
        {
            TemplateList.SelectedItem = list.FirstOrDefault(t =>
                string.Equals(t.FileName, selectedName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void OnReloadList(object sender, RoutedEventArgs e) => ReloadList();

    private async void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is not CodegenTemplateInfo info)
        {
            ClearEditor();
            return;
        }

        if (_dirty
            && _current is not null
            && !string.Equals(_current.FileName, info.FileName, StringComparison.OrdinalIgnoreCase)
            && !await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            TemplateList.SelectedItem = _current;
            return;
        }

        LoadTemplate(info);
    }

    private void LoadTemplate(CodegenTemplateInfo info)
    {
        _current = info;
        EditorTitle.Text = $"{info.DisplayName} · {info.FileName}";
        ApplySyntaxHighlight(info.FileName);
        _suppressTextChanged = true;
        EditorBox.TextChanged -= OnEditorTextChanged;
        EditorBox.LoadText(_session.Templates.ReadContent(info.FileName));
        EditorBox.TextChanged += OnEditorTextChanged;
        _suppressTextChanged = false;
        EditorBox.IsEnabled = true;
        SaveBtn.IsEnabled = true;
        ResetOneBtn.IsEnabled = true;
        _dirty = false;
    }

    private void ClearEditor()
    {
        _current = null;
        EditorTitle.Text = "选择左侧模板进行编辑";
        _suppressTextChanged = true;
        EditorBox.TextChanged -= OnEditorTextChanged;
        EditorBox.LoadText("");
        _suppressTextChanged = false;
        EditorBox.IsEnabled = false;
        SaveBtn.IsEnabled = false;
        ResetOneBtn.IsEnabled = false;
        _dirty = false;
    }

    private void OnEditorTextChanged(TextControlBox sender)
    {
        if (_suppressTextChanged)
        {
            return;
        }

        _dirty = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        try
        {
            _session.Templates.SaveContent(_current.FileName, EditorBox.Text ?? "");
            _dirty = false;
            _session.Log($"已保存模板: {_current.FileName}");
            ReloadList();
        }
        catch (Exception ex)
        {
            _session.LogError("保存模板失败: " + ex.Message);
        }
    }

    private async void OnResetOne(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "还原模板",
            Content = $"用随包默认覆盖「{_current.FileName}」？本地修改将丢失。",
            PrimaryButtonText = "还原",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (_session.Templates.ResetToBundled(_current.FileName))
        {
            _session.Log($"已还原模板: {_current.FileName}");
            LoadTemplate(_current);
            _dirty = false;
        }
        else
        {
            _session.LogWarn($"未找到默认模板: {_current.FileName}");
        }
    }

    private async void OnResetAll(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "全部还原默认",
            Content = "用随包默认覆盖全部用户模板？本地修改将全部丢失。",
            PrimaryButtonText = "全部还原",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var count = await UiBusy.RunBackgroundAsync(
                "正在还原全部模板…",
                () => _session.Templates.ResetAllToBundled())
            .ConfigureAwait(true);
        _session.Log($"已还原 {count} 个模板为默认");
        _dirty = false;
        ReloadList();
        if (TemplateList.SelectedItem is CodegenTemplateInfo info)
        {
            LoadTemplate(info);
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "未保存的修改",
            Content = "当前模板有未保存修改，切换后将丢失。是否继续？",
            PrimaryButtonText = "丢弃并切换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowWithIconsAsync() == ContentDialogResult.Primary;
    }

    private void ApplySyntaxHighlight(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        var id = name switch
        {
            _ when name.EndsWith(".ts.vm", StringComparison.Ordinal) => SyntaxHighlightID.Javascript,
            _ when name.EndsWith(".xml.vm", StringComparison.Ordinal) => SyntaxHighlightID.XML,
            _ when name.EndsWith(".kt.vm", StringComparison.Ordinal) => SyntaxHighlightID.Java,
            _ when name.Contains(".java", StringComparison.Ordinal) => SyntaxHighlightID.Java,
            _ => SyntaxHighlightID.Generic,
        };
        EditorBox.SelectSyntaxHighlightingById(id);
        EditorBox.EnableSyntaxHighlighting = true;
    }
}
