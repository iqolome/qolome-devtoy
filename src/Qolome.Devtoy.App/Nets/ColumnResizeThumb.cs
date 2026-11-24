using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Qolome.Nets.App;

public sealed class ColumnResizeThumb : Grid
{
    public ColumnResizeThumb()
    {
        Width = 8;
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
