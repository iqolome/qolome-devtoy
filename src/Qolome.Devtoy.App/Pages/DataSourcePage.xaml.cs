using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.Core.Data;
using Qolome_Devtoy_App.Helpers;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class DataSourcePage : Page
{
    private const int ModeUrl = 0;
    private const int ModeFields = 1;

    private const string GlyphReveal = "\uE7B3";
    private const string GlyphHide = "\uE9A9";

    private readonly DevtoySession _session = AppServices.Session;
    private bool _binding;
    private bool _passwordRevealed;
    private bool _syncingPassword;

    public DataSourcePage()
    {
        InitializeComponent();
        DbKindBox.ItemsSource = Enum.GetNames<DatabaseKind>();
        DbKindBox.SelectionChanged += OnDbKindChanged;
        Loaded += (_, _) => Bind();
        Unloaded += (_, _) => HidePassword();
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

        if (parsed == DatabaseKind.Sqlite)
        {
            EnsureSqliteDefaults();
        }
        else
        {
            ApplyRemoteDefaultPort(parsed);
        }

        ApplyKindFieldLabels(parsed);
        ApplyKindVisibility(parsed);
    }

    private void EnsureSqliteDefaults()
    {
        if (string.IsNullOrWhiteSpace(SqlitePathBox.Text))
        {
            SqlitePathBox.Text = DevtoyPaths.EnsureMigratedDefaultSqlitePath();
        }

        if (string.IsNullOrWhiteSpace(UserBox.Text))
        {
            UserBox.Text = "devtoy";
        }

        if (string.IsNullOrWhiteSpace(GetPasswordValue()))
        {
            SetPasswordValue(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8))
                .ToLowerInvariant());
        }

        ModeFieldsRadio.IsChecked = true;
    }

    private void ApplyRemoteDefaultPort(DatabaseKind parsed)
    {
        PortBox.Text = parsed switch
        {
            DatabaseKind.PostgreSql => "5432",
            DatabaseKind.Oracle => "1521",
            DatabaseKind.Odbc => "",
            _ => string.IsNullOrWhiteSpace(PortBox.Text) ? "3306" : PortBox.Text,
        };
    }

    private void ApplyKindFieldLabels(DatabaseKind parsed)
    {
        if (parsed == DatabaseKind.Odbc)
        {
            ParamsLabel.Text = "ODBC Driver";
            ParamsBox.PlaceholderText = "Driver={PostgreSQL Unicode} 或 DSN=MyDsn";
            DatabaseLabel.Text = "Database / DSN";
            JdbcUrlBox.PlaceholderText = "完整 ODBC 连接串（URL 模式）";
            JdbcLabel.Text = "ODBC 连接串";
            return;
        }

        ParamsLabel.Text = "连接参数";
        ParamsBox.PlaceholderText = "useSSL=false&serverTimezone=Asia/Shanghai";
        DatabaseLabel.Text = "数据库名称";
        JdbcUrlBox.PlaceholderText = "jdbc:mysql://127.0.0.1:3306/qolome";
        JdbcLabel.Text = "JDBC 连接";
    }

    private void Bind()
    {
        _binding = true;
        HidePassword();
        var ds = _session.Settings.DataSource;
        DataSourceRuntime.Normalize(ds);
        DbKindBox.SelectedItem = ds.DatabaseKind.ToString();
        HostBox.Text = ds.Host;
        PortBox.Text = ds.Port;
        DatabaseBox.Text = ds.Database;
        SqlitePathBox.Text = ds.IsSqlite
            ? DataSourceRuntime.ResolveSqlitePath(ds)
            : DevtoyPaths.EnsureMigratedDefaultSqlitePath();
        ParamsBox.Text = ds.ConnectionParams;
        JdbcUrlBox.Text = ds.JdbcUrl;
        UserBox.Text = ds.Username;
        SetPasswordValue(ds.Password);
        var byUrl = ds.InputMode == JdbcInputMode.Url && !ds.IsSqlite;
        ModeUrlRadio.IsChecked = byUrl;
        ModeFieldsRadio.IsChecked = !byUrl;
        ApplyKindVisibility(ds.DatabaseKind);
        if (!ds.IsSqlite)
        {
            ApplyPanelVisibility(byUrl ? ModeUrl : ModeFields);
        }

        _binding = false;
    }

    private void OnInputModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_binding || IsSqliteSelected())
        {
            return;
        }

        ApplyPanelVisibility(GetInputModeIndex());
    }

    private bool IsSqliteSelected() =>
        DbKindBox.SelectedItem is string kind
        && Enum.TryParse<DatabaseKind>(kind, out var parsed)
        && parsed == DatabaseKind.Sqlite;

    private int GetInputModeIndex() =>
        ModeUrlRadio.IsChecked == true ? ModeUrl : ModeFields;

    private void ApplyKindVisibility(DatabaseKind kind)
    {
        var sqlite = kind == DatabaseKind.Sqlite;
        var sqliteVis = sqlite ? Visibility.Visible : Visibility.Collapsed;
        var remoteVis = sqlite ? Visibility.Collapsed : Visibility.Visible;

        SqlitePathLabel.Visibility = sqliteVis;
        SqlitePathPanel.Visibility = sqliteVis;

        InputModeLabel.Visibility = remoteVis;
        InputModePanel.Visibility = remoteVis;
        GenerateInitSqlButton.Visibility = remoteVis;

        if (sqlite)
        {
            JdbcLabel.Visibility = Visibility.Collapsed;
            JdbcUrlBox.Visibility = Visibility.Collapsed;
            HostLabel.Visibility = Visibility.Collapsed;
            HostBox.Visibility = Visibility.Collapsed;
            PortLabel.Visibility = Visibility.Collapsed;
            PortBox.Visibility = Visibility.Collapsed;
            DatabaseLabel.Visibility = Visibility.Collapsed;
            DatabaseBox.Visibility = Visibility.Collapsed;
            ParamsLabel.Visibility = Visibility.Collapsed;
            ParamsBox.Visibility = Visibility.Collapsed;
            UserBox.IsReadOnly = true;
            PasswordBox.IsEnabled = false;
            PasswordPlainBox.IsReadOnly = true;
        }
        else
        {
            UserBox.IsReadOnly = false;
            PasswordBox.IsEnabled = true;
            PasswordPlainBox.IsReadOnly = false;
            ApplyPanelVisibility(GetInputModeIndex());
        }

        HidePassword();
    }

    private void ApplyPanelVisibility(int selectedIndex)
    {
        if (IsSqliteSelected())
        {
            return;
        }

        var byUrl = selectedIndex == ModeUrl;
        var urlVis = byUrl ? Visibility.Visible : Visibility.Collapsed;
        var fieldVis = byUrl ? Visibility.Collapsed : Visibility.Visible;

        JdbcLabel.Visibility = urlVis;
        JdbcUrlBox.Visibility = urlVis;

        DbKindLabel.Visibility = Visibility.Visible;
        DbKindBox.Visibility = Visibility.Visible;
        HostLabel.Visibility = fieldVis;
        HostBox.Visibility = fieldVis;
        PortLabel.Visibility = fieldVis;
        PortBox.Visibility = fieldVis;
        DatabaseLabel.Visibility = fieldVis;
        DatabaseBox.Visibility = fieldVis;
        ParamsLabel.Visibility = fieldVis;
        ParamsBox.Visibility = fieldVis;
    }

    private async void OnBrowseSqlite(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".db");
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            SqlitePathBox.Text = file.Path;
            return;
        }

        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        folderPicker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SqlitePathBox.Text = Path.Combine(folder.Path, DevtoyPaths.DefaultSqliteFileName);
        }
    }

    private void ApplyToModel()
    {
        var ds = _session.Settings.DataSource;
        if (DbKindBox.SelectedItem is string kind
            && Enum.TryParse<DatabaseKind>(kind, out var parsed))
        {
            ds.DatabaseKind = parsed;
        }

        if (ds.IsSqlite)
        {
            ds.InputMode = JdbcInputMode.Fields;
            ds.SqlitePath = string.IsNullOrWhiteSpace(SqlitePathBox.Text)
                ? DevtoyPaths.EnsureMigratedDefaultSqlitePath()
                : SqlitePathBox.Text.Trim();
            ds.Username = UserBox.Text.Trim();
            ds.Password = GetPasswordValue();
            DataSourceRuntime.Normalize(ds);
            SqlitePathBox.Text = ds.SqlitePath;
            UserBox.Text = ds.Username;
            SetPasswordValue(ds.Password);
            return;
        }

        ds.InputMode = GetInputModeIndex() == ModeUrl
            ? JdbcInputMode.Url
            : JdbcInputMode.Fields;
        ds.Host = HostBox.Text.Trim();
        ds.Port = PortBox.Text.Trim();
        ds.Database = DatabaseBox.Text.Trim();
        ds.ConnectionParams = ParamsBox.Text.Trim();
        ds.JdbcUrl = JdbcUrlBox.Text.Trim();
        ds.Username = UserBox.Text.Trim();
        ds.Password = GetPasswordValue();
        ds.SqlitePath = "";
    }

    private async void OnRevealPassword(object sender, RoutedEventArgs e)
    {
        if (_passwordRevealed)
        {
            HidePassword();
            return;
        }

        var (ok, error) = await WindowsHelloGate.VerifyAsync("查看数据库连接密码需要验证你的身份。")
            .ConfigureAwait(true);
        if (!ok)
        {
            await ShowMessageAsync("查看密码", error ?? "验证未通过。").ConfigureAwait(true);
            return;
        }

        ShowPassword();
    }

    private void OnPasswordPlainChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPassword || !_passwordRevealed || PasswordPlainBox.IsReadOnly)
        {
            return;
        }

        _syncingPassword = true;
        PasswordBox.Password = PasswordPlainBox.Text;
        _syncingPassword = false;
    }

    private string GetPasswordValue() =>
        _passwordRevealed ? PasswordPlainBox.Text : PasswordBox.Password;

    private void SetPasswordValue(string password)
    {
        _syncingPassword = true;
        PasswordBox.Password = password ?? "";
        PasswordPlainBox.Text = password ?? "";
        _syncingPassword = false;
    }

    private void ShowPassword()
    {
        _syncingPassword = true;
        PasswordPlainBox.Text = PasswordBox.Password;
        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordPlainBox.Visibility = Visibility.Visible;
        RevealPasswordIcon.Glyph = GlyphHide;
        ToolTipService.SetToolTip(RevealPasswordButton, "隐藏密码");
        _passwordRevealed = true;
        _syncingPassword = false;
    }

    private void HidePassword()
    {
        if (!_passwordRevealed && PasswordPlainBox.Visibility == Visibility.Collapsed)
        {
            RevealPasswordIcon.Glyph = GlyphReveal;
            return;
        }

        _syncingPassword = true;
        if (_passwordRevealed && !PasswordPlainBox.IsReadOnly)
        {
            PasswordBox.Password = PasswordPlainBox.Text;
        }

        PasswordPlainBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        RevealPasswordIcon.Glyph = GlyphReveal;
        ToolTipService.SetToolTip(RevealPasswordButton, "查看密码（需 Windows Hello 验证）");
        _passwordRevealed = false;
        _syncingPassword = false;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ApplyToModel();
        DataSourceRuntime.PersistCurrent(_session.Settings.DataSource);
        _session.SaveSettings();
        try
        {
            await UiBusy.RunAsync(
                    "正在准备业务库 Schema…",
                    async () =>
                    {
                        await AppDatabase.PrepareRuntimeAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(AppDatabase.StartupWarning))
                        {
                            await AppDatabase.LoadStartupDataAsync().ConfigureAwait(false);
                        }
                    })
                .ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(AppDatabase.StartupWarning))
            {
                _session.LogWarn(AppDatabase.StartupWarning);
            }
            else
            {
                _session.Log("业务库已就绪：" + AppDatabase.DescribeConnection());
            }
        }
        catch (Exception ex)
        {
            _session.LogWarn("业务库准备失败：" + ex.Message);
            await ShowErrorAsync("保存数据源", ex.Message).ConfigureAwait(true);
        }
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
        DataSourceRuntime.PersistCurrent(_session.Settings.DataSource);
        try
        {
            await UiBusy.RunAsync(
                    "正在初始化数据库…",
                    async () =>
                    {
                        await _session.InitializeSchemaAsync().ConfigureAwait(false);
                        await AppDatabase.PrepareRuntimeAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(AppDatabase.StartupWarning))
                        {
                            await AppDatabase.LoadStartupDataAsync().ConfigureAwait(false);
                        }
                    })
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

    private static async Task<string?> SaveSqlFileAsync(string sql)
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
