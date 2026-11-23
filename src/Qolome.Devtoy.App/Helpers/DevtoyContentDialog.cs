using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>为 <see cref="ContentDialog"/> 底部按钮补充图标 + 文字。</summary>
public static class DevtoyContentDialog
{
    public static IAsyncOperation<ContentDialogResult> ShowWithIconsAsync(this ContentDialog dialog)
    {
        EnableButtonIcons(dialog);
        return dialog.ShowAsync();
    }

    public static void EnableButtonIcons(ContentDialog dialog)
    {
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            dialog.Loaded -= handler!;
            ApplyButtonIcons(dialog);
            if (!HasStyledButtons(dialog))
            {
                dialog.DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => ApplyButtonIcons(dialog));
            }
        };
        dialog.Loaded += handler;
    }

    private static void ApplyButtonIcons(ContentDialog dialog)
    {
        StyleByCaption(dialog, dialog.PrimaryButtonText, PrimarySymbol(dialog.PrimaryButtonText));
        StyleByCaption(dialog, dialog.SecondaryButtonText, PrimarySymbol(dialog.SecondaryButtonText));
        StyleByCaption(dialog, dialog.CloseButtonText, CloseSymbol(dialog.CloseButtonText));
    }

    private static bool HasStyledButtons(ContentDialog dialog)
    {
        foreach (var button in EnumerateButtons(dialog))
        {
            if (button.Content is StackPanel)
            {
                return true;
            }
        }

        return false;
    }

    private static void StyleByCaption(ContentDialog dialog, string? text, Symbol symbol)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var button in EnumerateButtons(dialog))
        {
            if (button.Content is string caption && caption == text)
            {
                DevtoyButtons.SetGlyph(button, text, symbol);
                return;
            }
        }
    }

    private static IEnumerable<Button> EnumerateButtons(ContentDialog dialog)
    {
        var count = VisualTreeHelper.GetChildrenCount(dialog);
        for (var i = 0; i < count; i++)
        {
            foreach (var button in EnumerateButtonsRecursive(VisualTreeHelper.GetChild(dialog, i)))
            {
                yield return button;
            }
        }
    }

    private static IEnumerable<Button> EnumerateButtonsRecursive(DependencyObject node)
    {
        if (node is Button button)
        {
            yield return button;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in EnumerateButtonsRecursive(VisualTreeHelper.GetChild(node, i)))
            {
                yield return child;
            }
        }
    }

    private static Symbol PrimarySymbol(string? text) =>
        text switch
        {
            "继续" => Symbol.Forward,
            "删除" => Symbol.Delete,
            "保存" => Symbol.Save,
            "添加" => Symbol.Add,
            "生成" => Symbol.Play,
            _ => Symbol.Accept,
        };

    private static Symbol CloseSymbol(string? text) =>
        text switch
        {
            "确定" => Symbol.Accept,
            "关闭" => Symbol.Clear,
            _ => Symbol.Cancel,
        };
}