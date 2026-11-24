using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.App.ViewModels;

public partial class HostItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Ip { get; set; } = "";

    [ObservableProperty]
    public partial string Hostname { get; set; } = "—";

    [ObservableProperty]
    public partial string Mac { get; set; } = "—";

    [ObservableProperty]
    public partial string Vendor { get; set; } = "—";

    [ObservableProperty]
    public partial string OsType { get; set; } = "—";

    [ObservableProperty]
    public partial string DeviceInfo { get; set; } = "—";

    [ObservableProperty]
    public partial string Latency { get; set; } = "—";

    [ObservableProperty]
    public partial string Ttl { get; set; } = "—";

    [ObservableProperty]
    public partial string Size { get; set; } = "—";

    [ObservableProperty]
    public partial string Ports { get; set; } = "—";

    [ObservableProperty]
    public partial string PortDetails { get; set; } = "未发现开放端口";

    public ObservableCollection<OpenPortItemViewModel> PortItems { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string DiscoveryMethod { get; set; } = "";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public HostScanResult Model { get; private set; }

    private readonly Action? _checkedChanged;
    private bool _suppressCheckedNotify;

    public HostItemViewModel(HostScanResult model, Action? checkedChanged = null)
    {
        _checkedChanged = checkedChanged;
        Model = model;
        Apply(model);
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_suppressCheckedNotify)
        {
            _checkedChanged?.Invoke();
        }
    }

    public void SetChecked(bool value)
    {
        if (IsChecked == value)
        {
            return;
        }

        _suppressCheckedNotify = true;
        IsChecked = value;
        _suppressCheckedNotify = false;
    }

    public void Apply(HostScanResult model)
    {
        Model = model;
        Ip = model.Ip;
        Hostname = string.IsNullOrWhiteSpace(model.Hostname) ? "—" : model.Hostname;
        Mac = string.IsNullOrWhiteSpace(model.Mac) ? "—" : model.Mac;
        Vendor = string.IsNullOrWhiteSpace(model.Vendor) ? "—" : model.Vendor;
        OsType = string.IsNullOrWhiteSpace(model.OsType) ? "—" : model.OsType;
        DeviceInfo = string.IsNullOrWhiteSpace(model.DeviceInfo) ? "—" : model.DeviceInfo;
        Latency = model.Ping.LatencyDisplay;
        Ttl = model.Ping.TtlDisplay;
        Size = model.Ping.SizeDisplay;
        Ports = model.PortsDisplay;
        PortItems.Clear();
        foreach (var port in model.OpenPorts)
        {
            PortItems.Add(new OpenPortItemViewModel(port));
        }

        PortDetails = model.OpenPorts.Count == 0
            ? "未发现开放端口"
            : string.Join(Environment.NewLine, model.OpenPorts.Select(p => p.ToString()));
        Status = model.Status;
        DiscoveryMethod = model.DiscoveryMethod;
        OnPropertyChanged(nameof(OpenPorts));
        OnPropertyChanged(nameof(PingStatistics));
    }

    public IReadOnlyList<OpenPort> OpenPorts => Model.OpenPorts;

    public string PingStatistics => Model.Ping.StatisticsText;
}

public sealed class OpenPortItemViewModel
{
    public OpenPortItemViewModel(OpenPort port)
    {
        Port = port.Port;
        Protocol = port.ProtocolKey;
        Service = port.Service;
        Description = port.Description ?? "";
        State = port.State;
        Display = port.ToString();
    }

    public int Port { get; }
    public string Protocol { get; }
    public string Service { get; }
    public string Description { get; }
    public string State { get; }
    public string Display { get; }
}

public partial class PingLineViewModel : ObservableObject
{
    public PingLineViewModel(PingReplyInfo reply)
    {
        Text = reply.LineText;
        Size = reply.SizeDisplay;
        Latency = reply.RttDisplay;
        Ttl = reply.TtlDisplay;
        Status = reply.StatusText;
    }

    public string Text { get; }
    public string Size { get; }
    public string Latency { get; }
    public string Ttl { get; }
    public string Status { get; }
}
