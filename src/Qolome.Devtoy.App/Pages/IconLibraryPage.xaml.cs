using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome_Devtoy_App.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class IconLibraryPage : Page
{
    private readonly List<IconLibraryItem> _all;
    private DispatcherTimer? _copyHintTimer;

    public IconLibraryPage()
    {
        InitializeComponent();
        _all = SegoeFluentIconCatalog.All.Select(e => new IconLibraryItem(e)).ToList();
        ApplyFilter();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnScopeChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private bool SymbolOnly =>
        ScopeCombo.SelectedItem is ComboBoxItem { Tag: string tag }
        && tag.Equals("symbol", StringComparison.OrdinalIgnoreCase);

    private void ApplyFilter()
    {
        if (IconGrid is null || CountText is null)
        {
            return;
        }

        var q = (SearchBox?.Text ?? string.Empty).Trim();
        IEnumerable<IconLibraryItem> source = _all;
        if (SymbolOnly)
        {
            source = source.Where(i => i.HasSymbol);
        }

        if (q.Length > 0)
        {
            var hex = q.TrimStart('U', 'u', '+').Replace("\\u", "", StringComparison.OrdinalIgnoreCase);
            source = source.Where(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || i.Unicode.Contains(q, StringComparison.OrdinalIgnoreCase)
                || i.CsharpEscape.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (i.SymbolName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || i.Entry.CodePoint.ToString("X4").Contains(hex, StringComparison.OrdinalIgnoreCase));
        }

        var list = source.ToList();
        IconGrid.ItemsSource = list;

        var scopeLabel = SymbolOnly ? $"Symbol {SymbolEnumMap.SymbolCount}" : $"{_all.Count}";
        CountText.Text = string.IsNullOrEmpty(q)
            ? $"共 {list.Count} 个（范围 {scopeLabel}）"
            : $"匹配 {list.Count} / {scopeLabel}";
    }

    private void OnIconClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not IconLibraryItem item)
        {
            return;
        }

        var text = item.PreferredCopyText;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        CopiedHint.Text = item.HasSymbol
            ? $"已复制 {text}（XAML: Symbol=\"{item.SymbolName}\"）"
            : $"已复制 {text}";
        _copyHintTimer?.Stop();
        _copyHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _copyHintTimer.Tick += OnCopyHintTick;
        _copyHintTimer.Start();
    }

    private void OnCopyHintTick(object? sender, object e)
    {
        if (_copyHintTimer is null)
        {
            return;
        }

        _copyHintTimer.Tick -= OnCopyHintTick;
        _copyHintTimer.Stop();
        CopiedHint.Text = string.Empty;
    }
}
