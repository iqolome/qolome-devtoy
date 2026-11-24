using Windows.Storage.Pickers;
using WinRT.Interop;
using Qolome.Nets.Core.Data;

namespace Qolome.Nets.App;

public static class FileDialogs
{
    public static async Task<string?> PickSaveExcelAsync(string suggestedName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("Excel 工作簿", [".xlsx"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickOpenExcelAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xlsm");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSaveBackupAsync(string suggestedName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("配置备份 (ZIP)", [".zip"]);
        picker.FileTypeChoices.Add("JSON 配置备份", [".json"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSaveZipAsync(string suggestedName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("ZIP 压缩包", [".zip"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickOpenBackupAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickOpenIconAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        foreach (var ext in DeviceTypeIconStore.Extensions)
        {
            picker.FileTypeFilter.Add(ext);
        }

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSavePngAsync(string suggestedName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("PNG 图片", [".png"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSaveSqlAsync(string suggestedName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("SQL 脚本", [".sql"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickOpenSqlAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".sql");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSaveJsonAsync(string suggestedName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedName;
        picker.FileTypeChoices.Add("JSON 配置备份", [".json"]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickOpenJsonAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
