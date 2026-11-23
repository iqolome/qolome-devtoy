using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Java TsDictGenerator：按字典项生成 TS 字典。</summary>
public sealed class TsDictGenerator
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai");

    private readonly VelocityFiles _velocityFiles;

    public TsDictGenerator(VelocityFiles velocityFiles) => _velocityFiles = velocityFiles;

    public string Generate(
        CodegenSettings settings,
        string projectPath,
        string projectName,
        string auth,
        string version,
        DictItemRecord item,
        IReadOnlyList<DictValueRow> values)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("字典值为空: " + item.ItemCode);
        }

        var referenceCode = item.ReferenceCode;
        var valueToStr = referenceCode is "String" or "Long";
        var tsName = CaseFormats.LowerUnderscoreToLowerHyphen(item.ItemCode);
        var datetime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zone)
            .ToString("yyyy年MM月dd日 HH:mm:ss");

        var model = new
        {
            copyright = settings.Copyright,
            projectName,
            datetime,
            itemId = item.ItemId.ToString(),
            itemCode = item.ItemCode,
            itemEnumName = CaseFormats.DictCodeToUpperCamel(item.ItemCode),
            itemName = item.ItemName,
            version,
            tsName,
            tsComments = item.ItemName,
            vals = new VmList(values.Select(v => new
            {
                valId = v.ValId.ToString(),
                valValue = v.ValValue,
                valEnumName = CaseFormats.UpperUnderscoreToUpperCamel(v.ValEnum ?? "UNKNOWN"),
                valCode = v.ValCode,
                valName = v.ValName,
                valEnum = v.ValEnum,
                valScope = v.ValScope,
                valDesc = v.ValDesc,
                seq = v.Seq,
                remark = v.Remark,
                enabled = v.Enabled ? "true" : "false",
            })),
            auth,
            valueToStrFlag = valueToStr,
            valueTypeStrFlag = string.Equals(referenceCode, "String", StringComparison.Ordinal),
        };

        var output = Path.Combine(projectPath, settings.TsDictPath, tsName + ".ts");
        _velocityFiles.WriteBean(settings.TsDictTemplateName, output, model);
        return output;
    }
}
