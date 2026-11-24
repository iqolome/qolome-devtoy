using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Qolome.Nets.Core.Data;
using Windows.UI;

namespace Qolome.Nets.App;

public static class DeviceTypeColorXaml
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Brush BrushFromType(DeviceType? type) =>
        BrushFromGroup(type?.ColorGroup, type?.Code);

    public static Brush BrushFromGroup(string? key, string? code = null) =>
        FromHex(DeviceTypeColorGroups.Hex(key, code));

    public static Brush FromHex(string? hex)
    {
        var value = string.IsNullOrWhiteSpace(hex) ? "#64748B" : hex.Trim();
        if (Cache.TryGetValue(value, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(Parse(value));
        Cache[value] = brush;
        return brush;
    }

    public static FrameworkElement Label(DeviceTypeColorGroup group)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new Rectangle
        {
            Width = 12,
            Height = 12,
            RadiusX = 2,
            RadiusY = 2,
            Fill = FromHex(group.Hex),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = group.Name,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static Color Parse(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 8
            && byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            && byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r8)
            && byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g8)
            && byte.TryParse(s[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b8))
        {
            return Color.FromArgb(a, r8, g8, b8);
        }

        if (s.Length == 6
            && byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return Color.FromArgb(255, r, g, b);
        }

        return Color.FromArgb(255, 100, 116, 139);
    }
}

public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        DeviceTypeColorXaml.FromHex(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
