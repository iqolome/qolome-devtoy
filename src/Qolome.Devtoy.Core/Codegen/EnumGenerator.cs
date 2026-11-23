using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Java EnumGenerator：按字典项生成 Java 枚举。</summary>
public sealed class EnumGenerator
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai");

    private readonly VelocityFiles _velocityFiles;

    public EnumGenerator(VelocityFiles velocityFiles) => _velocityFiles = velocityFiles;

    public string Generate(
        CodegenSettings settings,
        EnumProjectContext project,
        string? enumPackagePath,
        DictItemRecord item,
        IReadOnlyList<DictValueRow> values)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("字典值为空: " + item.ItemCode);
        }

        var enumPackage = string.IsNullOrWhiteSpace(enumPackagePath)
            ? settings.EnumPackage
            : enumPackagePath.Trim();
        var enumClassName = CaseFormats.DictCodeToUpperCamel(item.ItemCode) + settings.EnumNameSuffix;
        var referenceCode = item.ReferenceCode;
        var interfaceName = SimpleName(settings.EnumInterface);
        var datetime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zone)
            .ToString("yyyy年MM月dd日 HH:mm:ss");

        var model = new
        {
            copyright = settings.Copyright,
            projectName = project.ProjectName,
            datetime,
            version = project.Version,
            enumClassName,
            itemId = item.ItemId.ToString(),
            itemCode = item.ItemCode,
            itemName = item.ItemName,
            importList = new VmList { settings.EnumInterface },
            enumValues = new VmList(values.Select(v => new
            {
                fieldName = v.ValEnum ?? "",
                code = v.ValCode,
                name = v.ValName,
                value = v.ValValue,
                valDesc = v.ValDesc,
                remark = v.Remark,
            })),
            auth = project.Auth,
            enumPackagePath = enumPackage,
            enumInftInfo = new { inftName = interfaceName, packageName = settings.EnumInterface },
            valueDataType = referenceCode,
            valuetypeStrFlag = string.Equals(referenceCode, "String", StringComparison.Ordinal),
        };

        var output = Path.Combine(
            project.ProjectPath,
            "src",
            "main",
            "java",
            enumPackage.Replace('.', Path.DirectorySeparatorChar),
            enumClassName + ".java");
        _velocityFiles.WriteBean(settings.EnumTemplateName, output, model);
        return output;
    }

    private static string SimpleName(string className)
    {
        var dot = className.LastIndexOf('.');
        return dot < 0 ? className : className[(dot + 1)..];
    }
}
