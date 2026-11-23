using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class BasicSettingsPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;

    public BasicSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind();
    }

    private void Bind()
    {
        var s = _session.Settings;
        AuthorBox.Text = s.AuthorName;
        EmailBox.Text = s.AuthorEmail;
        BasePackageBox.Text = s.BasePackage;
        EnumPackageBox.Text = s.EnumPackagePath;
        LogicDeleteBox.Text = s.LogicDeleteColumnName;
        TenantIdBox.Text = s.TenantIdColumnName;
        CreateTimeBox.Text = s.CreateTimeColumnName;
        CreaterIdBox.Text = s.CreaterIdColumnName;
        ModifyTimeBox.Text = s.ModifyTimeColumnName;
        ModifierIdBox.Text = s.ModifierIdColumnName;

        IdTypeCombo.ItemsSource = MybatisIdType.All
            .Select(x => new IdTypeOption(x.Id, x.DisplayName))
            .ToList();
        IdTypeCombo.DisplayMemberPath = nameof(IdTypeOption.DisplayName);
        var idType = MybatisIdType.Resolve(s.IdType);
        IdTypeCombo.SelectedItem = IdTypeCombo.Items
            .OfType<IdTypeOption>()
            .FirstOrDefault(x => x.Id == idType);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = _session.Settings;
        s.AuthorName = AuthorBox.Text.Trim();
        s.AuthorEmail = EmailBox.Text.Trim();
        s.BasePackage = string.IsNullOrWhiteSpace(BasePackageBox.Text)
            ? "com.qolome.starter"
            : BasePackageBox.Text.Trim();
        s.EnumPackagePath = string.IsNullOrWhiteSpace(EnumPackageBox.Text)
            ? "com.qolome.common.dict.biz.enums"
            : EnumPackageBox.Text.Trim();
        s.LogicDeleteColumnName = NormalizeColumn(LogicDeleteBox.Text, "deleted");
        s.TenantIdColumnName = NormalizeColumn(TenantIdBox.Text, "tenant_id");
        s.CreateTimeColumnName = NormalizeColumn(CreateTimeBox.Text, "create_time");
        s.CreaterIdColumnName = NormalizeColumn(CreaterIdBox.Text, "creater_id");
        s.ModifyTimeColumnName = NormalizeColumn(ModifyTimeBox.Text, "modify_time");
        s.ModifierIdColumnName = NormalizeColumn(ModifierIdBox.Text, "modifier_id");
        s.IdType = IdTypeCombo.SelectedItem is IdTypeOption option
            ? MybatisIdType.Resolve(option.Id)
            : MybatisIdType.AssignId;

        _session.SaveSettings();
        _session.Log($"代码生成配置已保存（author={s.FormatAuthorHtml()}）");
    }

    private static string NormalizeColumn(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record IdTypeOption(string Id, string DisplayName);
}
