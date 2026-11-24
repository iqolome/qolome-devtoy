using System.Globalization;
using System.Text.RegularExpressions;

namespace Qolome.Nets.Core.Data;

public static class SvgColorTint
{
    private static readonly Regex ColorAttribute = new(
        """(?<name>fill|stroke)\s*=\s*(?<q>['"])(?<value>[^'"]+)\k<q>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Apply(string svg, string? groupHex)
    {
        if (string.IsNullOrWhiteSpace(svg) || string.IsNullOrWhiteSpace(groupHex))
        {
            return svg;
        }

        var stroke = NormalizeHex(groupHex);
        if (stroke is null)
        {
            return svg;
        }

        var fillLight = MixWhite(stroke, 0.85);
        return ColorAttribute.Replace(svg, match =>
        {
            var value = match.Groups["value"].Value.Trim();
            if (!IsPaintableColor(value))
            {
                return match.Value;
            }

            var name = match.Groups["name"].Value;
            var quote = match.Groups["q"].Value;
            var painted = name.Equals("stroke", StringComparison.OrdinalIgnoreCase) || !IsLightColor(value)
                ? stroke
                : fillLight;
            return $"{name}={quote}{painted}{quote}";
        });
    }

    public static string ApplyOutline(string svg, string strokeHex = OutlineStrokeHex, string fillHex = OutlineFillHex)
    {
        if (string.IsNullOrWhiteSpace(svg))
        {
            return svg;
        }

        var stroke = NormalizeHex(strokeHex) ?? OutlineStrokeHex;
        var fill = NormalizeHex(fillHex) ?? OutlineFillHex;
        return ColorAttribute.Replace(svg, match =>
        {
            var value = match.Groups["value"].Value.Trim();
            if (!IsPaintableColor(value))
            {
                return match.Value;
            }

            var name = match.Groups["name"].Value;
            var quote = match.Groups["q"].Value;
            var painted = name.Equals("stroke", StringComparison.OrdinalIgnoreCase) || !IsLightColor(value)
                ? stroke
                : fill;
            return $"{name}={quote}{painted}{quote}";
        });
    }

    public const string OutlineStrokeHex = "#4B5563";
    public const string OutlineFillHex = "#FFFFFF";

    public static string? NormalizeHex(string? hex)
    {
        var raw = hex?.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Length == 3
            && TryNibble(raw[0], out var r3)
            && TryNibble(raw[1], out var g3)
            && TryNibble(raw[2], out var b3))
        {
            return $"#{r3:X2}{g3:X2}{b3:X2}";
        }

        if (raw.Length is 6 or 8
            && int.TryParse(raw[^6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return $"#{rgb:X6}";
        }

        return null;
    }

    private static bool IsPaintableColor(string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)
            || value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.StartsWith('#') || value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLightColor(string value)
    {
        if (!TryParseRgb(value, out var r, out var g, out var b))
        {
            return true;
        }

        return 0.299 * r + 0.587 * g + 0.114 * b > 168;
    }

    private static string MixWhite(string hex, double amount)
    {
        if (!TryParseRgb(hex, out var r, out var g, out var b))
        {
            return hex;
        }

        byte Mix(int channel) => (byte)Math.Clamp(channel + (255 - channel) * amount, 0, 255);
        return $"#{Mix(r):X2}{Mix(g):X2}{Mix(b):X2}";
    }

    private static bool TryParseRgb(string value, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var hex = NormalizeHex(value.StartsWith('#') || value.Length is 3 or 6 or 8 ? value : null);
        if (hex is null || hex.Length != 7)
        {
            return false;
        }

        r = Convert.ToInt32(hex[1..3], 16);
        g = Convert.ToInt32(hex[3..5], 16);
        b = Convert.ToInt32(hex[5..7], 16);
        return true;
    }

    private static bool TryNibble(char c, out int value)
    {
        var hex = c is >= '0' and <= '9' ? c - '0'
            : c is >= 'a' and <= 'f' ? c - 'a' + 10
            : c is >= 'A' and <= 'F' ? c - 'A' + 10
            : -1;
        if (hex < 0)
        {
            value = 0;
            return false;
        }

        value = hex * 17;
        return true;
    }
}
