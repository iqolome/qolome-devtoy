using ClosedXML.Excel;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Excel;

public static class ExcelInventory
{
    public static readonly string[] ScanHeaders =
    [
        "主机名", "IP", "MAC", "厂商", "系统类型", "设备信息", "开放端口"
    ];

    public static readonly string[] DeviceHeaders =
    [
        "设备编码", "设备类型", "主机名", "厂商", "品牌", "设备型号", "设备说明", "域名", "IP", "MAC", "系统类型", "设备信息", "开放端口", "备注"
    ];

    public static readonly string[] DeviceTypeHeaders = ["编码", "名称", "说明", "颜色分组"];

    private static readonly string[] DeviceHeaderHints =
    [
        "可空，空则自动生成 DEV-xxxx",
        "从下拉选择软件中的设备类型；也可填新名称或编码，导入时不存在会自动创建",
        "选填",
        "选填，OUI 或制造商",
        "选填，如华为、Cisco",
        "选填",
        "选填，设备用途或补充说明",
        "多个域名用 ; 或 , 分隔",
        "可多块网卡，用 ; 或换行分隔；缺 IP 填 _，与 MAC 按顺序对应",
        "可多块网卡，用 ; 或换行分隔；缺 MAC 填 _，与 IP 按顺序对应",
        "选填，如 Windows / Linux/Unix",
        "选填",
        "选填，如 22/tcp, 53/udp",
        "选填"
    ];

    private static readonly string[] DeviceTypeHeaderHints =
    [
        "必填，英文或拼音，如 server",
        "必填，如 服务器",
        "选填",
        "选填，从下拉选择分组名称，也可填英文键 network/security 等",
    ];

    private static readonly (string Column, string Required, string Hint, string Example)[] DeviceGuideRows =
    [
        ("设备编码", "建议", "同一环境内用于匹配已有设备。留空则自动生成 DEV-0001 形式编码。", "DEV-0001"),
        ("设备类型", "选填", "请从下拉列表选择软件中已有的类型名称。也可填写新名称或编码，导入时不存在会自动创建。", "蓝牙设备"),
        ("主机名", "选填", "设备主机名。", "core-sw-01"),
        ("厂商", "选填", "制造商或 OUI 厂商，与品牌相互独立。扫描入库时自动填写。", "Huawei Technologies Co.,Ltd"),
        ("品牌", "选填", "设备品牌。", "华为"),
        ("设备型号", "选填", "硬件型号。", "Dell PowerEdge R740"),
        ("设备说明", "选填", "设备用途、位置或补充说明，与备注、设备信息相互独立。", "机房A 核心交换机"),
        ("域名", "选填", "多个域名用英文分号、逗号或中文逗号分隔。", "app.local; intranet.example.com"),
        ("IP", "建议", "IPv4。多块网卡用分号或换行分隔，与 MAC 按顺序对应。某块没有 IP 时该位置填 _。普通设备至少填一个真实 IP；蓝牙可留空。", "_; 192.168.1.1"),
        ("MAC", "选填", "多块网卡用分号或换行分隔，与 IP 按顺序对应。某块没有 MAC 时该位置填 _。导入时会规范为冒号分隔。", "AA:BB:CC:DD:EE:FF; _"),
        ("系统类型", "选填", "如 Windows、Linux/Unix、网络设备。", "Linux/Unix"),
        ("设备信息", "选填", "发现方式、TTL 等扫描补充说明，不含厂商。", "发现=ICMP；TTL=64"),
        ("开放端口", "选填", "开放端口，建议带协议，如 22/tcp, 53/udp。", "22/tcp, 80/tcp, 53/udp"),
        ("备注", "选填", "任意说明。", "核心交换机")
    ];

    private static readonly (string Column, string Required, string Hint, string Example)[] DeviceTypeGuideRows =
    [
        ("编码", "必填", "唯一编码，建议小写英文。", "server"),
        ("名称", "必填", "界面显示名称。编码或名称与已有项相同则更新。", "服务器"),
        ("说明", "选填", "类型用途说明。", "物理或虚拟服务器"),
        ("颜色分组", "选填", "请从下拉选择软件中的分组名称，或填英文键（network/security 等）。空则按编码归入默认分组。", "计算终端")
    ];

    private const string TypeSheetName = "设备类型";
    private const string ListSheetName = "下拉列表";
    private const int DeviceTypeColumn = 2;
    private const int ColorGroupColumn = 4;
    private const int TemplateDropdownRows = 100;

    public static void WriteScan(string path, IEnumerable<HostScanResult> hosts)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("扫描结果");
        WriteHeader(sheet, ScanHeaders);
        var row = 2;
        foreach (var host in hosts)
        {
            sheet.Cell(row, 1).Value = host.Hostname ?? "";
            sheet.Cell(row, 2).Value = host.Ip;
            sheet.Cell(row, 3).Value = host.Mac ?? "";
            sheet.Cell(row, 4).Value = host.Vendor ?? "";
            sheet.Cell(row, 5).Value = host.OsType ?? "";
            sheet.Cell(row, 6).Value = host.DeviceInfo ?? "";
            sheet.Cell(row, 7).Value = host.OpenPorts.Count == 0 ? "" : string.Join(", ", host.OpenPorts.Select(p => p.ToString()));
            row++;
        }

        sheet.Columns().AdjustToContents();
        ApplyAutoFilter(sheet, ScanHeaders.Length);
        workbook.SaveAs(path);
    }

    public static void WriteDevices(string path, IEnumerable<Device> devices, IReadOnlyList<DeviceType>? types = null)
    {
        var list = devices as IReadOnlyList<Device> ?? devices.ToList();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("设备");
        WriteHeader(sheet, DeviceHeaders);
        var row = 2;
        foreach (var device in list)
        {
            sheet.Cell(row, 1).Value = device.Code;
            sheet.Cell(row, 2).Value = device.DeviceType?.Name ?? "";
            sheet.Cell(row, 3).Value = device.Hostname ?? "";
            sheet.Cell(row, 4).Value = device.Vendor ?? "";
            sheet.Cell(row, 5).Value = device.Brand ?? "";
            sheet.Cell(row, 6).Value = device.Model ?? "";
            sheet.Cell(row, 7).Value = device.Description ?? "";
            sheet.Cell(row, 8).Value = string.Join("; ", device.Domains.Select(d => d.Domain));
            sheet.Cell(row, 9).Value = FormatNicColumn(device, n => n.Ip);
            sheet.Cell(row, 10).Value = FormatNicColumn(device, n => n.Mac);
            sheet.Cell(row, 11).Value = device.OsType ?? "";
            sheet.Cell(row, 12).Value = device.DeviceInfo ?? "";
            sheet.Cell(row, 13).Value = device.OpenPorts ?? "";
            sheet.Cell(row, 14).Value = device.Remarks ?? "";
            row++;
        }

        sheet.Columns().AdjustToContents();
        ApplyAutoFilter(sheet, DeviceHeaders.Length);
        ApplyDeviceTypeDropdown(
            workbook,
            sheet,
            types,
            list.Select(d => d.DeviceType?.Name));
        workbook.SaveAs(path);
    }

    public static void WriteDeviceImport(string path, IEnumerable<DeviceImportRow> rows, IReadOnlyList<DeviceType>? types = null)
    {
        var list = rows as IReadOnlyList<DeviceImportRow> ?? rows.ToList();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("设备");
        WriteHeader(sheet, DeviceHeaders, DeviceHeaderHints);
        var row = 2;
        foreach (var item in list)
        {
            if (IsBlankDeviceRow(item))
            {
                continue;
            }

            sheet.Cell(row, 1).Value = InventoryService.TrimInput(item.Code);
            sheet.Cell(row, 2).Value = InventoryService.TrimInput(item.DeviceType);
            sheet.Cell(row, 3).Value = InventoryService.TrimInput(item.Hostname);
            sheet.Cell(row, 4).Value = InventoryService.TrimInput(item.Vendor);
            sheet.Cell(row, 5).Value = InventoryService.TrimInput(item.Brand);
            sheet.Cell(row, 6).Value = InventoryService.TrimInput(item.Model);
            sheet.Cell(row, 7).Value = InventoryService.TrimInput(item.Description, keepLineBreaks: true);
            sheet.Cell(row, 8).Value = FormatDomainText(item.Domains);
            sheet.Cell(row, 9).Value = FormatNicText(item.Ip);
            sheet.Cell(row, 10).Value = FormatNicText(item.Mac);
            sheet.Cell(row, 11).Value = InventoryService.TrimInput(item.OsType);
            sheet.Cell(row, 12).Value = InventoryService.TrimInput(item.DeviceInfo, keepLineBreaks: true);
            sheet.Cell(row, 13).Value = InventoryService.TrimInput(item.OpenPorts);
            sheet.Cell(row, 14).Value = InventoryService.TrimInput(item.Remarks, keepLineBreaks: true);
            row++;
        }

        sheet.Columns().AdjustToContents();
        ApplyAutoFilter(sheet, DeviceHeaders.Length);
        WriteGuideSheet(workbook, "填写说明", DeviceGuideRows);
        ApplyDeviceTypeDropdown(
            workbook,
            sheet,
            types,
            list.Select(r => r.DeviceType));
        workbook.SaveAs(path);
    }

    private static string FormatNicColumn(Device device, Func<DeviceNic, string?> selector)
    {
        var nics = device.OrderedNics();
        return nics.Count == 0
            ? ""
            : string.Join("; ", nics.Select(n => InventoryService.NicSlotDisplay(selector(n))));
    }

    private static string FormatNicText(string? text)
    {
        var parts = InventoryService.SplitNicValues(text);
        return parts.Count == 0
            ? ""
            : string.Join("; ", parts.Select(InventoryService.NicSlotDisplay));
    }

    private static string FormatDomainText(string? text)
    {
        var parts = InventoryService.ParseDomains(text);
        return parts.Count == 0 ? "" : string.Join("; ", parts);
    }

    private static bool IsBlankDeviceRow(DeviceImportRow item) =>
        string.IsNullOrWhiteSpace(item.Code)
        && string.IsNullOrWhiteSpace(item.DeviceType)
        && string.IsNullOrWhiteSpace(item.Hostname)
        && string.IsNullOrWhiteSpace(item.Vendor)
        && string.IsNullOrWhiteSpace(item.Brand)
        && string.IsNullOrWhiteSpace(item.Model)
        && string.IsNullOrWhiteSpace(item.Description)
        && string.IsNullOrWhiteSpace(item.Domains)
        && string.IsNullOrWhiteSpace(item.Ip)
        && string.IsNullOrWhiteSpace(item.Mac)
        && string.IsNullOrWhiteSpace(item.OsType)
        && string.IsNullOrWhiteSpace(item.DeviceInfo)
        && string.IsNullOrWhiteSpace(item.OpenPorts)
        && string.IsNullOrWhiteSpace(item.Remarks);

    public static void WriteDeviceTypes(string path, IEnumerable<DeviceType> types)
    {
        using var workbook = new XLWorkbook();
        var list = types.ToList();
        var sheet = WriteDeviceTypeCatalog(workbook, list);
        ApplyAutoFilter(sheet, DeviceTypeHeaders.Length);
        ApplyColorGroupDropdown(sheet);
        workbook.SaveAs(path);
    }

    public static void WriteDeviceTemplate(string path, IReadOnlyList<DeviceType>? types = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("设备");
        WriteHeader(sheet, DeviceHeaders, DeviceHeaderHints);
        ApplyColumnWidths(sheet, DeviceHeaders.Length);
        ApplyAutoFilter(sheet, DeviceHeaders.Length);
        WriteGuideSheet(workbook, "填写说明", DeviceGuideRows);
        ApplyDeviceTypeDropdown(workbook, sheet, types, template: true);
        workbook.SaveAs(path);
    }

    public static void WriteDeviceTypeTemplate(string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(TypeSheetName);
        WriteHeader(sheet, DeviceTypeHeaders, DeviceTypeHeaderHints);
        ApplyColumnWidths(sheet, DeviceTypeHeaders.Length);
        ApplyAutoFilter(sheet, DeviceTypeHeaders.Length);
        WriteGuideSheet(workbook, "填写说明", DeviceTypeGuideRows);
        ApplyColorGroupDropdown(sheet, template: true);
        workbook.SaveAs(path);
    }

    public static IReadOnlyList<DeviceImportRow> ReadDevices(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.FirstOrDefault(s => s.Name == "设备")
            ?? workbook.Worksheets.FirstOrDefault(s => s.Name != TypeSheetName && s.Name != ListSheetName && s.Name != "填写说明")
            ?? workbook.Worksheets.First();
        var map = HeaderMap(sheet);
        Require(map, "设备编码", "主机名", "IP");
        var rows = new List<DeviceImportRow>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = new DeviceImportRow
            {
                Code = Cell(sheet, r, map, "设备编码"),
                DeviceType = Cell(sheet, r, map, "设备类型"),
                Hostname = Cell(sheet, r, map, "主机名"),
                Vendor = Cell(sheet, r, map, "厂商"),
                Brand = Cell(sheet, r, map, "品牌"),
                Model = Cell(sheet, r, map, "设备型号", "型号"),
                Description = Cell(sheet, r, map, "设备说明", "说明"),
                Domains = Cell(sheet, r, map, "域名"),
                Ip = Cell(sheet, r, map, "IP", "ip地址"),
                Mac = Cell(sheet, r, map, "MAC", "mac地址"),
                OsType = Cell(sheet, r, map, "系统类型"),
                DeviceInfo = Cell(sheet, r, map, "设备信息"),
                OpenPorts = Cell(sheet, r, map, "开放端口"),
                Remarks = Cell(sheet, r, map, "备注")
            };
            if (!IsBlankDeviceRow(row))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    public static IReadOnlyList<DeviceTypeImportRow> ReadDeviceTypes(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.FirstOrDefault(s => s.Name == TypeSheetName)
            ?? workbook.Worksheets.FirstOrDefault(s => s.Name != "设备" && s.Name != ListSheetName && s.Name != "填写说明")
            ?? workbook.Worksheets.First();
        var map = HeaderMap(sheet);
        Require(map, "编码", "名称");
        var rows = new List<DeviceTypeImportRow>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            var row = new DeviceTypeImportRow
            {
                Code = Cell(sheet, r, map, "编码", "设备类型编码"),
                Name = Cell(sheet, r, map, "名称", "设备类型", "设备类型名称"),
                Description = Cell(sheet, r, map, "说明", "描述"),
                ColorGroup = Cell(sheet, r, map, "颜色分组", "分组", "颜色")
            };
            if (string.IsNullOrWhiteSpace(row.Code) && string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers, IReadOnlyList<string>? hints = null)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D6EAF8");
            if (hints is not null && i < hints.Count && !string.IsNullOrWhiteSpace(hints[i]))
            {
                cell.GetComment().AddText(hints[i]);
            }
        }

        sheet.SheetView.FreezeRows(1);
    }

    private static void ApplyColumnWidths(IXLWorksheet sheet, int columns)
    {
        for (var i = 1; i <= columns; i++)
        {
            sheet.Column(i).Width = 16;
        }
    }

    private static void ApplyAutoFilter(IXLWorksheet sheet, int columns)
    {
        var last = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? 1, 1);
        sheet.Range(1, 1, last, columns).SetAutoFilter();
    }

    private static void WriteGuideSheet(
        XLWorkbook workbook,
        string name,
        IReadOnlyList<(string Column, string Required, string Hint, string Example)> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        WriteHeader(sheet, ["列名", "是否必填", "填写说明", "示例"]);
        var row = 2;
        foreach (var item in rows)
        {
            sheet.Cell(row, 1).Value = item.Column;
            sheet.Cell(row, 2).Value = item.Required;
            sheet.Cell(row, 3).Value = item.Hint;
            sheet.Cell(row, 4).Value = item.Example;
            row++;
        }

        sheet.Column(1).Width = 14;
        sheet.Column(2).Width = 12;
        sheet.Column(3).Width = 56;
        sheet.Column(4).Width = 36;
        ApplyAutoFilter(sheet, 4);
    }

    private static IXLWorksheet WriteDeviceTypeCatalog(
        XLWorkbook workbook,
        IReadOnlyList<DeviceType> types,
        IEnumerable<string>? extraNames = null)
    {
        var sheet = workbook.Worksheets.Add(TypeSheetName);
        WriteHeader(sheet, DeviceTypeHeaders);
        var row = 2;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types.OrderBy(t => t.SortOrder).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = type.Name?.Trim() ?? "";
            var code = type.Code?.Trim() ?? "";
            if (name.Length == 0 && code.Length == 0)
            {
                continue;
            }

            if (name.Length > 0)
            {
                seen.Add(name);
            }

            sheet.Cell(row, 1).Value = code;
            sheet.Cell(row, 2).Value = name;
            sheet.Cell(row, 3).Value = type.Description ?? "";
            sheet.Cell(row, 4).Value = DeviceTypeColorGroups.DisplayName(type.ColorGroup, type.Code);
            row++;
        }

        if (extraNames is not null)
        {
            foreach (var extra in extraNames)
            {
                var name = extra?.Trim() ?? "";
                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                sheet.Cell(row, 1).Value = name;
                sheet.Cell(row, 2).Value = name;
                row++;
            }
        }

        sheet.Columns().AdjustToContents();
        ApplyAutoFilter(sheet, DeviceTypeHeaders.Length);
        return sheet;
    }

    private static void ApplyDeviceTypeDropdown(
        XLWorkbook workbook,
        IXLWorksheet deviceSheet,
        IReadOnlyList<DeviceType>? types,
        IEnumerable<string?>? extraNames = null,
        bool template = false)
    {
        var catalogTypes = types ?? [];
        var extras = extraNames?
            .Select(name => name?.Trim() ?? "")
            .Where(name => name.Length > 0)
            .ToList() ?? [];
        if (catalogTypes.Count == 0 && extras.Count == 0)
        {
            return;
        }

        var catalog = WriteDeviceTypeCatalog(workbook, catalogTypes, extras);
        var lastNameRow = catalog.LastRowUsed()?.RowNumber() ?? 1;
        if (lastNameRow < 2)
        {
            return;
        }

        ApplyListValidation(
            deviceSheet,
            DeviceTypeColumn,
            catalog.Range(2, 2, lastNameRow, 2),
            "DeviceTypeNames",
            "设备类型",
            "请从下拉列表选择软件中已有的设备类型。也可以填写新名称，导入时会自动创建。",
            template);
    }

    private static void ApplyColorGroupDropdown(IXLWorksheet typeSheet, bool template = false)
    {
        var names = DeviceTypeColorGroups.All
            .OrderBy(g => g.Order)
            .Select(g => g.Name)
            .ToList();
        if (names.Count == 0)
        {
            return;
        }

        var quoted = "\"" + string.Join(",", names) + "\"";
        var endRow = ValidationEndRow(typeSheet, template);
        var validation = typeSheet.Range(2, ColorGroupColumn, endRow, ColorGroupColumn).CreateDataValidation();
        validation.IgnoreBlanks = true;
        validation.List(quoted, true);
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Warning;
        validation.ErrorTitle = "颜色分组";
        validation.ErrorMessage = "请从下拉列表选择软件中的颜色分组。也可以填写英文键，空则按编码归入默认分组。";
        validation.ShowInputMessage = false;
    }

    private static void ApplyListValidation(
        IXLWorksheet sheet,
        int column,
        IXLRange listRange,
        string definedName,
        string title,
        string message,
        bool template = false)
    {
        listRange.AddToNamed(definedName);
        var endRow = ValidationEndRow(sheet, template);
        var validation = sheet.Range(2, column, endRow, column).CreateDataValidation();
        validation.IgnoreBlanks = true;
        validation.List(listRange, true);
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Warning;
        validation.ErrorTitle = title;
        validation.ErrorMessage = message;
        validation.ShowInputMessage = false;
    }

    private static int ValidationEndRow(IXLWorksheet sheet, bool template)
    {
        var lastUsed = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (lastUsed < 2)
        {
            return 1 + TemplateDropdownRows;
        }

        return template ? Math.Max(lastUsed, 1 + TemplateDropdownRows) : lastUsed;
    }

    private static Dictionary<string, int> HeaderMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var last = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 1; c <= last; c++)
        {
            var title = sheet.Cell(1, c).GetString().Trim();
            if (title.Length > 0)
            {
                map[title] = c;
            }
        }

        return map;
    }

    private static void Require(Dictionary<string, int> map, params string[] requiredHeaders)
    {
        if (map.Count == 0)
        {
            throw new ScanException("Excel 没有表头。请先下载导入模板。");
        }

        var missing = requiredHeaders.Where(name => !map.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            throw new ScanException($"Excel 缺少列：{string.Join("、", missing)}。请使用「下载模板」获取标准表头。");
        }
    }

    private static string? Cell(IXLWorksheet sheet, int row, Dictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out var column))
            {
                var value = sheet.Cell(row, column).GetFormattedString().Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }
}
