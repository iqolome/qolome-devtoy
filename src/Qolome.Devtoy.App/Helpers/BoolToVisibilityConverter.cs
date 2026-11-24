using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>bool → Visibility；ConverterParameter=invert 时取反。</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;
        if (IsInvert(parameter))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility.Visible;
        if (IsInvert(parameter))
        {
            visible = !visible;
        }

        return visible;
    }

    private static bool IsInvert(object? parameter) =>
        parameter is string s
        && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
}
