using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 类似 Ant Design Message 的顶部居中轻提示：不打断操作、自动消失。
/// </summary>
public static class AppMessage
{
    private static Panel? _host;
    private static readonly Queue<(string Text, MessageKind Kind)> Pending = new();

    public static void AttachHost(Panel host)
    {
        _host = host;
        while (Pending.Count > 0)
        {
            var (text, kind) = Pending.Dequeue();
            ShowCore(text, kind);
        }
    }

    public static void Success(string text) => Show(text, MessageKind.Success);

    public static void Error(string text) => Show(text, MessageKind.Error);

    public static void Info(string text) => Show(text, MessageKind.Info);

    public static void Warning(string text) => Show(text, MessageKind.Warning);

    private static void Show(string text, MessageKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_host is null)
        {
            Pending.Enqueue((text.Trim(), kind));
            return;
        }

        if (_host.DispatcherQueue.HasThreadAccess)
        {
            ShowCore(text.Trim(), kind);
        }
        else
        {
            _host.DispatcherQueue.TryEnqueue(() => ShowCore(text.Trim(), kind));
        }
    }

    private static void ShowCore(string text, MessageKind kind)
    {
        if (_host is null)
        {
            return;
        }

        var (glyph, color) = kind switch
        {
            MessageKind.Success => ("\uE73E", Color.FromArgb(255, 82, 196, 26)),
            MessageKind.Error => ("\uE711", Color.FromArgb(255, 255, 77, 79)),
            MessageKind.Warning => ("\uE7BA", Color.FromArgb(255, 250, 173, 20)),
            _ => ("\uE946", Color.FromArgb(255, 22, 119, 255)),
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = glyph,
            FontSize = 16,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
        });

        var card = new Border
        {
            Background = ResolveCardBackground(),
            BorderBrush = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 18, 10),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 520,
            Opacity = 1,
            IsHitTestVisible = false,
            Child = content,
        };

        _host.Children.Add(card);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var hide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(hide, card);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(hide, "Opacity");
            var hideBoard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            hideBoard.Children.Add(hide);
            hideBoard.Completed += (_, _) => _host.Children.Remove(card);
            hideBoard.Begin();
        };
        timer.Start();
    }

    /// <summary>必须用不透明实色，半透明卡片会透出下层表格文字。</summary>
    private static Brush ResolveCardBackground()
    {
        var dark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        // 浅色纯白 / 深色实底，Alpha=FF
        return new SolidColorBrush(dark
            ? Color.FromArgb(255, 43, 43, 43)
            : Colors.White);
    }

    private enum MessageKind
    {
        Success,
        Error,
        Info,
        Warning,
    }
}
