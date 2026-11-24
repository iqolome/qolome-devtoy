using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class DataSourcePage : Page
{
    private const int ModeUrl = 0;

    private const int ModeFields = 1;

    private readonly DevtoySession _session = AppServices.Session;

    private bool _binding;

    public DataSourcePage()
    {
        InitializeComponent();
        DbKindBox.ItemsSource = Enum.GetNames(typeof(DatabaseKind));
        DbKindBox.SelectionChanged += OnDbKindChanged;
        Loaded += (_, _) => Bind();
    }

    private void OnDbKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding || DbKindBox.SelectedItem is not string kind)
        {
            return;
        }

        if (!Enum.TryParse<DatabaseKind>(kind, out var parsed))
        {
            return;
        }

        PortBox.Text = parsed switch
        {
            DatabaseKind.PostgreSql => "5432",
            DatabaseKind.Oracle => "1521",
            DatabaseKind.Sqlite => "",
            DatabaseKind.Odbc => "",
            _ => "3306",
        };

        if (parsed == DatabaseKind.Odbc)
        {
            ParamsLabel.Text = "ODBC Driver";
            ParamsBox.PlaceholderText = "Driver={PostgreSQL Unicode} 或 DSN=MyDsn";
            DatabaseLabel.Text = "Database / DSN";
            JdbcUrlBox.PlaceholderText = "完整 ODBC 连接串（URL 模式）";
            JdbcLabel.Text = "ODBC 连接串";
        }
        else
        {
            ParamsLabel.Text = "连接参数";
            ParamsBox.PlaceholderText = "useSSL=false&serverTimezone=Asia/Shanghai";
            DatabaseLabel.Text = "数据库名称";
            JdbcUrlBox.PlaceholderText = "jdbc:mysql://127.0.0.1:3306/qolome";
            JdbcLabel.Text = "JDBC 连接";
        }
    }

    private void Bind()
    {
        _binding = true;
        var ds = _session.Settings.DataSource;
        DbKindBox.SelectedItem = ds.DatabaseKind.ToString();
        HostBox.Text = ds.Host;
        PortBox.Text = ds.Port;
        DatabaseBox.Text = ds.Database;
        ParamsBox.Text = ds.ConnectionParams;
        JdbcUrlBox.Text = ds.JdbcUrl;
        UserBox.Text = ds.Username;
        PasswordBox.Password = ds.Password;
        var byUrl = ds.InputMode == JdbcInputMode.Url;
        ModeUrlRadio.IsChecked = byUrl;
        ModeFieldsRadio.IsChecked = !byUrl;
        ApplyPanelVisibility(byUrl ? ModeUrl : ModeFields);
        _binding = false;
    }

    private void OnInputModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_binding)
        {
            return;
        }

        ApplyPanelVisibility(GetInputModeIndex());
    }

    private int GetInputModeIndex() =>
        ModeUrlRadio.IsChecked == true ? ModeUrl : ModeFields;

    private void ApplyPanelVisibility(int selectedIndex)
    {
        var byUrl = selectedIndex == ModeUrl;
        var urlVis = byUrl ? Visibility.Visible : Visibility.Collapsed;
        var fieldVis = byUrl ? Visibility.Collapsed : Visibility.Visible;

        JdbcLabel.Visibility = urlVis;
        JdbcUrlBox.Visibility = urlVis;

        DbKindLabel.Visibility = fieldVis;
        DbKindBox.Visibility = fieldVis;
        HostLabel.Visibility = fieldVis;
        HostBox.Visibility = fieldVis;
        PortLabel.Visibility = fieldVis;
        PortBox.Visibility = fieldVis;
        DatabaseLabel.Visibility = fieldVis;
        DatabaseBox.Visibility = fieldVis;
        ParamsLabel.Visibility = fieldVis;
        ParamsBox.Visibility = fieldVis;
    }

    private void ApplyToModel()
    {
        var ds = _session.Settings.DataSource;
        ds.InputMode = GetInputModeIndex() == ModeUrl
            ? JdbcInputMode.Url
            : JdbcInputMode.Fields;
        if (DbKindBox.SelectedItem is string kind
            && Enum.TryParse<DatabaseKind>(kind, out var parsed))
        {
            ds.DatabaseKind = parsed;
        }

        ds.Host = HostBox.Text.Trim();
        ds.Port = PortBox.Text.Trim();
        ds.Database = DatabaseBox.Text.Trim();
        ds.ConnectionParams = ParamsBox.Text.Trim();
        ds.JdbcUrl = JdbcUrlBox.Text.Trim();
        ds.Username = UserBox.Text.Trim();
        ds.Password = PasswordBox.Password;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ApplyToModel();
        _session.SaveSettings();
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        ApplyToModel();
        var message = await UiBusy.RunAsync("正在测试数据库连接…", () => _session.TestDatabaseAsync())
            .ConfigureAwait(true);
        await ShowMessageAsync("测试连接", message).ConfigureAwait(true);
    }

    private async void OnGenerateInitSql(object sender, RoutedEventArgs e)
    {
        ApplyToModel();
        try
        {
            var sql = await UiBusy.RunAsync("正在生成初始化 SQL…", () => _session.GenerateInitSqlAsync())
                .ConfigureAwait(true);
            var box = new TextBox
            {
                Text = sql,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                MinWidth = 560,
                MinHeight = 280,
                IsReadOnly = true,
            };
            var dialog = new ContentDialog
            {
                Title = "初始化 SQL",
                Content = box,
                PrimaryButtonText = "保存到文件",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowWithIconsAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var path = await SaveSqlFileAsync(sql).ConfigureAwait(true);
            if (path is not null)
            {
                _session.Log("已保存初始化 SQL: " + path);
            }
        }
        catch (Exception ex)
        {
            _session.LogError("生成初始化 SQL 失败: " + ex.Message);
            await ShowErrorAsync("生成初始化 SQL", ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnInitializeSchema(object sender, RoutedEventArgs e)
    {
        ApplyToModel();
        try
        {
            await UiBusy.RunAsync("正在初始化数据库…", () => _session.InitializeSchemaAsync())
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("初始化", ex.Message).ConfigureAwait(true);
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowWithIconsAsync();
    }

    private Task ShowErrorAsync(string title, string message) =>
        ShowMessageAsync(title, "失败：\n" + message);

    private async Task<string?> SaveSqlFileAsync(string sql)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"qolome_devtoy_mysql_init_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        picker.FileTypeChoices.Add("SQL", [".sql"]);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await FileIO.WriteTextAsync(file, sql);
        return file.Path;
    }
}
