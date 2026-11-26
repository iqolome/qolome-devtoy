using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Qolome_Devtoy_App.Helpers;

namespace Qolome.Nets.App.ViewModels;

public partial class DeviceListLayout : ObservableObject
{
    public static DeviceListLayout Shared { get; } = new();

    public const double MinWidth = 56;

    /// <summary>拖拽列宽上限（数据列可宽于表头 8 字）。</summary>
    public const double MaxWidth = 480;

    public static double CheckWidth => TableColumnMetrics.CheckColumnWidth;
    public static double IndexWidth => TableColumnMetrics.IndexColumnWidth;
    public static double ActionsWidth => TableColumnMetrics.ActionColumnMaxWidth;

    [ObservableProperty]
    public partial double Code { get; set; } = 100;

    [ObservableProperty]
    public partial double Type { get; set; } = 120;

    [ObservableProperty]
    public partial double Hostname { get; set; } = 120;

    [ObservableProperty]
    public partial double Vendor { get; set; } = 120;

    [ObservableProperty]
    public partial double Brand { get; set; } = 100;

    [ObservableProperty]
    public partial double Model { get; set; } = 90;

    [ObservableProperty]
    public partial double Description { get; set; } = 160;

    [ObservableProperty]
    public partial double Ip { get; set; } = 120;

    [ObservableProperty]
    public partial double Mac { get; set; } = 130;

    [ObservableProperty]
    public partial double Domains { get; set; } = 120;

    [ObservableProperty]
    public partial double Os { get; set; } = 80;

    [ObservableProperty]
    public partial double Group { get; set; } = 110;

    [ObservableProperty]
    public partial double Created { get; set; } = TableColumnMetrics.DateTimeColumnWidth;

    [ObservableProperty]
    public partial double Updated { get; set; } = TableColumnMetrics.DateTimeColumnWidth;

    public GridLength CheckLength => Pixel(CheckWidth);
    public GridLength IndexLength => Pixel(IndexWidth);
    public GridLength ActionsLength => Pixel(ActionsWidth);

    public GridLength CodeLength => Pixel(Code);
    public GridLength TypeLength => Pixel(Type);
    public GridLength HostnameLength => Pixel(Hostname);
    public GridLength VendorLength => Pixel(Vendor);
    public GridLength BrandLength => Pixel(Brand);
    public GridLength ModelLength => Pixel(Model);
    public GridLength DescriptionLength => Pixel(Description);
    public GridLength IpLength => Pixel(Ip);
    public GridLength MacLength => Pixel(Mac);
    public GridLength DomainsLength => Pixel(Domains);
    public GridLength OsLength => Pixel(Os);
    public GridLength GroupLength => Pixel(Group);
    public GridLength CreatedLength => Pixel(Created);
    public GridLength UpdatedLength => Pixel(Updated);

    public double TotalWidth =>
        CheckWidth + IndexWidth + Code + Type + Hostname + Vendor + Brand + Model + Description + Ip + Mac + Domains + Os + Group + Created + Updated + ActionsWidth + 40;

    public double Get(string key) => key switch
    {
        "Code" => Code,
        "Type" => Type,
        "Hostname" => Hostname,
        "Vendor" => Vendor,
        "Brand" => Brand,
        "Model" => Model,
        "Description" => Description,
        "Ip" => Ip,
        "Mac" => Mac,
        "Domains" => Domains,
        "Os" => Os,
        "Group" => Group,
        "Created" => Created,
        "Updated" => Updated,
        _ => MinWidth
    };

    public void Resize(string key, double width)
    {
        width = Math.Clamp(width, MinWidth, MaxWidth);
        switch (key)
        {
            case "Code": Code = width; break;
            case "Type": Type = width; break;
            case "Hostname": Hostname = width; break;
            case "Vendor": Vendor = width; break;
            case "Brand": Brand = width; break;
            case "Model": Model = width; break;
            case "Description": Description = width; break;
            case "Ip": Ip = width; break;
            case "Mac": Mac = width; break;
            case "Domains": Domains = width; break;
            case "Os": Os = width; break;
            case "Group": Group = width; break;
            case "Created": Created = width; break;
            case "Updated": Updated = width; break;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null or nameof(TotalWidth) || e.PropertyName.EndsWith("Length", StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(e.PropertyName + "Length");
        OnPropertyChanged(nameof(TotalWidth));
    }

    private static GridLength Pixel(double value) => new(value, GridUnitType.Pixel);
}
