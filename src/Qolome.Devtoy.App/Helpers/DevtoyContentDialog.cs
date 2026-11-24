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
        if (dialog.PrimaryButtonText == "删除"
            && Application.Current.Resources.TryGetValue("DevtoyDangerButtonStyle", out var danger)
            && danger is Style dangerStyle)
        {
            dialog.PrimaryButtonStyle = dangerStyle;
        }

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
        // PrimaryButtonStyle 着色后，图标栈内文字也跟白色前景
        if (dialog.PrimaryButtonText == "删除")
        {
            ApplyDangerForeground(dialog, "删除");
        }
    }

    private static void ApplyDangerForeground(ContentDialog dialog, string caption)
    {
        if (!Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var fgObj)
            || fgObj is not Brush fg)
        {
            return;
        }

        foreach (var button in EnumerateButtons(dialog))
        {
            if (!ButtonMatchesCaption(button, caption))
            {
                continue;
            }

            if (button.Content is StackPanel stack)
            {
                ApplyForegroundToStackChildren(stack, fg);
            }

            return;
        }
    }

    private static void ApplyForegroundToStackChildren(StackPanel stack, Brush fg)
    {
        foreach (var child in stack.Children)
        {
            switch (child)
            {
                case TextBlock tb:
                    tb.Foreground = fg;
                    break;
                case Viewbox { Child: SymbolIcon symbol }:
                    symbol.Foreground = fg;
                    break;
                case SymbolIcon sym:
                    sym.Foreground = fg;
                    break;
            }
        }
    }

    private static bool ButtonMatchesCaption(Button button, string caption) =>
        button.Content is string s && s == caption
        || button.Content is StackPanel stack
            && stack.Children.OfType<TextBlock>().Any(t => t.Text == caption);

    private static bool HasStyledButtons(ContentDialog dialog) =>
        EnumerateButtons(dialog).Any(b => b.Content is StackPanel);

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
            "清空" => Symbol.Delete,
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