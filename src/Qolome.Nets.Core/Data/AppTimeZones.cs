namespace Qolome.Nets.Core.Data;

public sealed record AppTimeZoneOption(string Id, string Label);

public static class AppTimeZones
{
    public const string System = "system";
    public const string Utc = "UTC";

    public static IReadOnlyList<AppTimeZoneOption> All { get; } =
    [
        new(System, "跟随系统时区"),
        new(Utc, "UTC"),
        new("Asia/Shanghai", "中国（上海）UTC+8"),
        new("Asia/Hong_Kong", "中国香港 UTC+8"),
        new("Asia/Taipei", "中国台北 UTC+8"),
        new("Asia/Tokyo", "日本（东京）UTC+9"),
        new("Asia/Seoul", "韩国（首尔）UTC+9"),
        new("Asia/Singapore", "新加坡 UTC+8"),
        new("Asia/Kolkata", "印度（加尔各答）UTC+5:30"),
        new("Australia/Sydney", "澳大利亚（悉尼）"),
        new("Europe/London", "英国（伦敦）"),
        new("Europe/Paris", "欧洲中部（巴黎）"),
        new("Europe/Berlin", "德国（柏林）"),
        new("America/New_York", "美国东部（纽约）"),
        new("America/Chicago", "美国中部（芝加哥）"),
        new("America/Denver", "美国山地（丹佛）"),
        new("America/Los_Angeles", "美国太平洋（洛杉矶）")
    ];

    private static readonly Dictionary<string, string> WindowsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Shanghai"] = "China Standard Time",
        ["Asia/Hong_Kong"] = "China Standard Time",
        ["Asia/Taipei"] = "Taipei Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Asia/Seoul"] = "Korea Standard Time",
        ["Asia/Singapore"] = "Singapore Standard Time",
        ["Asia/Kolkata"] = "India Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Paris"] = "Romance Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time"
    };

    public static string Normalize(string? value)
    {
        var id = value?.Trim();
        if (string.IsNullOrWhiteSpace(id) || string.Equals(id, System, StringComparison.OrdinalIgnoreCase))
        {
            return System;
        }

        if (All.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            return All.First(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Id;
        }

        return System;
    }

    public static int ToIndex(string? value)
    {
        var id = Normalize(value);
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == id)
            {
                return i;
            }
        }

        return 0;
    }

    public static string FromIndex(int index) =>
        index >= 0 && index < All.Count ? All[index].Id : System;

    public static TimeZoneInfo Resolve(string? value)
    {
        var id = Normalize(value);
        if (id == System)
        {
            return TimeZoneInfo.Local;
        }

        if (id == Utc)
        {
            return TimeZoneInfo.Utc;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var iana))
        {
            return iana;
        }

        if (WindowsIds.TryGetValue(id, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out var windows))
        {
            return windows;
        }

        return TimeZoneInfo.Local;
    }

    public static string ToMysqlOffset(string? value)
    {
        var span = Resolve(value).GetUtcOffset(DateTime.UtcNow);
        var sign = span < TimeSpan.Zero ? "-" : "+";
        var abs = span.Duration();
        return $"{sign}{(int)abs.TotalHours:D2}:{abs.Minutes:D2}";
    }

    public static string Describe(string? value)
    {
        var id = Normalize(value);
        var offset = ToMysqlOffset(id);
        if (id == System)
        {
            var name = TimeZoneInfo.Local.DisplayName;
            return $"当前跟随系统：{name}（MySQL 会话 {offset}）。";
        }

        var label = All.First(x => x.Id == id).Label;
        return $"已指定 {label}（MySQL 会话 {offset}）。点「保存并连接」后对当前数据库连接生效。";
    }
}
