using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Services;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class ThemeSettingsPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;
    private bool _suppressThemeChange;
    private bool _suppressIconChange;

    public ThemeSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind();
    }

    private void Bind()
    {
        _suppressThemeChange = true;
        AppThemeCombo.ItemsSource = AppThemeMode.All
            .Select(x => new ThemeOption(x.Id, x.DisplayName))
            .ToList();
        AppThemeCombo.DisplayMemberPath = nameof(ThemeOption.DisplayName);
        var themeId = AppThemeMode.Resolve(_session.Settings.AppTheme);
        AppThemeCombo.SelectedItem = AppThemeCombo.Items
            .OfType<ThemeOption>()
            .FirstOrDefault(x => x.Id == themeId);
        _suppressThemeChange = false;

        BindIconVariantCombo(themeId);
        UpdateIconThemeUi(themeId);
        RefreshIconPreview();
    }

    private void BindIconVariantCombo(string themeId)
    {
        _suppressIconChange = true;
        IconVariantCombo.ItemsSource = IconVariantInfo.All;
        IconVariantCombo.DisplayMemberPath = nameof(IconVariantInfo.DisplayName);
        var effective = AppServices.IconTheme.ResolveEffectiveVariant(themeId);
        IconVariantCombo.SelectedItem = IconVariantInfo.All
            .FirstOrDefault(v => v.Id == effective) ?? IconVariantInfo.All[0];
        _suppressIconChange = false;
    }

    private void UpdateIconThemeUi(string themeId) =>
        IconVariantCombo.IsEnabled = themeId != AppThemeMode.System;

    private void OnAppThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || AppThemeCombo.SelectedItem is not ThemeOption option)
        {
            return;
        }

        _session.Settings.AppTheme = option.Id;
        _session.SaveSettings();
        AppServices.AppTheme.Apply(option.Id);
        AppServices.IconTheme.SyncWithAppTheme(coerceUserVariant: true, log: true);
        BindIconVariantCombo(option.Id);
        UpdateIconThemeUi(option.Id);
        RefreshIconPreview();
        _session.Log($"已切换界面主题: {option.DisplayName}");
    }

    private void OnIconVariantChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressIconChange)
        {
            return;
        }

        if (AppThemeMode.Resolve(_session.Settings.AppTheme) == AppThemeMode.System)
        {
            RefreshIconPreview();
            return;
        }

        if (IconVariantCombo.SelectedItem is IconVariantInfo info)
        {
            AppServices.IconTheme.SetVariant(info.Id);
        }

        RefreshIconPreview();
    }

    private void RefreshIconPreview()
    {
        IconPreviewHost.Child = null;
        var theme = AppThemeMode.Resolve(_session.Settings.AppTheme);
        string previewId;
        if (theme == AppThemeMode.System)
        {
            previewId = AppServices.IconTheme.ResolveEffectiveVariant(theme);
        }
        else if (IconVariantCombo.SelectedItem is IconVariantInfo info)
        {
            previewId = info.Id;
        }
        else
        {
            previewId = AppServices.IconTheme.ResolveEffectiveVariant(theme);
        }

        IconPreviewHost.Child = IconThemeService.CreatePreviewImage(previewId, 64);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (AppThemeCombo.SelectedItem is ThemeOption theme)
        {
            _session.Settings.AppTheme = theme.Id;
        }

        var themeId = AppThemeMode.Resolve(_session.Settings.AppTheme);
        if (themeId != AppThemeMode.System
            && IconVariantCombo.SelectedItem is IconVariantInfo info)
        {
            _session.Settings.IconVariant = info.Id;
        }

        _session.SaveSettings();
        AppServices.AppTheme.ApplyFromSettings();
        AppServices.IconTheme.SyncWithAppTheme(coerceUserVariant: true, log: true);
        BindIconVariantCombo(themeId);
        UpdateIconThemeUi(themeId);
        RefreshIconPreview();
        _session.Log("主题设置已保存");
    }

    private sealed record ThemeOption(string Id, string DisplayName);
}
