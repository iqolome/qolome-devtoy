using System.Text.Json.Serialization;

namespace Qolome.Nets.Core.Data;

public static class TopologyMetrics
{
    public const double NodeWidth = 140;
    public const double NodeHeight = 108;
    public const double GridSize = 24;
    public const double MinCanvasWidth = 2400;
    public const double MinCanvasHeight = 1600;
    public const double CanvasMaxWidth = 9600;
    public const double CanvasMaxHeight = 6400;
    public const double CanvasGrowStep = 480;
    public const double CanvasEdgeReserve = 96;
    public const double ContainerMinWidth = 320;
    public const double ContainerMinHeight = 200;
    public const double ContainerPad = 16;
    public const double ContainerCaptionGap = 6;
    public const double ContainerCaptionHeight = 22;

    public static double CanvasWidth { get; private set; } = MinCanvasWidth;
    public static double CanvasHeight { get; private set; } = MinCanvasHeight;
    public static double MaxX => Math.Max(0, CanvasWidth - NodeWidth);
    public static double MaxY => Math.Max(0, CanvasHeight - NodeHeight);

    public static double WidthOf(TopologyNodeDto node) =>
        node.IsContainer ? ClampContainer(node.Width, ContainerMinWidth, CanvasMaxWidth) : NodeWidth;

    public static double HeightOf(TopologyNodeDto node) =>
        node.IsContainer ? ClampContainer(node.Height, ContainerMinHeight, CanvasMaxHeight) : NodeHeight;

    private static double ClampContainer(double value, double minimum, double maximum)
    {
        var size = value > 0 ? value : minimum;
        return Math.Clamp(size, minimum, maximum);
    }

    public static double LayoutHeightOf(TopologyNodeDto node) =>
        HeightOf(node) + (node.IsContainer ? ContainerCaptionGap + ContainerCaptionHeight : 0);

    public static void Apply(double width, double height)
    {
        CanvasWidth = NormalizeSize(width, MinCanvasWidth, CanvasMaxWidth);
        CanvasHeight = NormalizeSize(height, MinCanvasHeight, CanvasMaxHeight);
    }

    public static void CopyTo(TopologyDocument document)
    {
        document.CanvasWidth = CanvasWidth;
        document.CanvasHeight = CanvasHeight;
    }

    public static bool GrowToFit(double right, double bottom)
    {
        var width = Math.Min(CanvasMaxWidth, Math.Max(CanvasWidth, AlignGrow(right)));
        var height = Math.Min(CanvasMaxHeight, Math.Max(CanvasHeight, AlignGrow(bottom)));
        if (width <= CanvasWidth && height <= CanvasHeight)
        {
            return false;
        }

        CanvasWidth = width;
        CanvasHeight = height;
        return true;
    }

    public static bool GrowToFitContent(IEnumerable<TopologyNodeDto> nodes)
    {
        var changed = false;
        foreach (var node in nodes)
        {
            changed |= GrowToFit(
                node.X + WidthOf(node) + CanvasEdgeReserve,
                node.Y + LayoutHeightOf(node) + CanvasEdgeReserve);
        }

        return changed;
    }

    public static bool EnsureFits(TopologyNodeDto node, double x, double y) =>
        GrowToFit(
            x + WidthOf(node) + CanvasEdgeReserve,
            y + LayoutHeightOf(node) + CanvasEdgeReserve);

    public static (double X, double Y) ClampPosition(TopologyNodeDto node, double x, double y)
    {
        EnsureFits(node, x, y);
        x = Clamp(x, Math.Max(0, CanvasWidth - WidthOf(node)));
        y = Clamp(y, Math.Max(0, CanvasHeight - LayoutHeightOf(node)));
        return (x, y);
    }

    public static double Snap(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Round(value / GridSize) * GridSize;
    }

    public static double ClampX(double x) => Clamp(x, MaxX);

    public static double ClampY(double y) => Clamp(y, MaxY);

    public static double ClampTo(double value, double max) => Clamp(value, max);

    public static bool IsValid(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static double AlignGrow(double needed)
    {
        if (!IsValid(needed) || needed <= 0)
        {
            return 0;
        }

        return Math.Ceiling(needed / CanvasGrowStep) * CanvasGrowStep;
    }

    private static double NormalizeSize(double value, double min, double max)
    {
        if (!IsValid(value) || value < min)
        {
            return min;
        }

        return Math.Min(value, max);
    }

    private static double Clamp(double value, double max)
    {
        if (!IsValid(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, max);
    }
}

public sealed class TopologyDocument
{
    public int EnvironmentId { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public List<TopologyNodeDto> Nodes { get; set; } = [];
    public List<TopologyLinkDto> Links { get; set; } = [];
    public DateTime? UpdatedAtUtc { get; set; }
    public double CanvasWidth { get; set; }
    public double CanvasHeight { get; set; }

    public TopologyDocument CloneGraph() => new()
    {
        EnvironmentId = EnvironmentId,
        ShowGrid = ShowGrid,
        SnapToGrid = SnapToGrid,
        Nodes = Nodes.Select(Clone).ToList(),
        Links = Links.Select(Clone).ToList(),
        UpdatedAtUtc = UpdatedAtUtc,
        CanvasWidth = CanvasWidth,
        CanvasHeight = CanvasHeight
    };

    public void RestoreGraph(TopologyDocument source)
    {
        Nodes = source.Nodes.Select(Clone).ToList();
        Links = source.Links.Select(Clone).ToList();
    }

    public static TopologyNodeDto Clone(TopologyNodeDto n) => new()
    {
        Id = n.Id,
        DeviceId = n.DeviceId,
        Kind = n.Kind,
        IspKey = n.IspKey,
        Label = n.Label,
        X = n.X,
        Y = n.Y,
        Width = n.Width,
        Height = n.Height,
        ContainerId = n.ContainerId
    };

    public static TopologyLinkDto Clone(TopologyLinkDto l) => new()
    {
        Id = l.Id,
        FromNodeId = l.FromNodeId,
        ToNodeId = l.ToNodeId,
        FromDeviceId = l.FromDeviceId,
        ToDeviceId = l.ToDeviceId,
        Kind = l.Kind,
        Color = l.Color,
        Dashed = l.Dashed,
        Label = l.Label,
        Curve = l.Curve
    };
}

public sealed class TopologyNodeDto
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string Kind { get; set; } = TopologyNodeKinds.Device;
    public string? IspKey { get; set; }
    public string? Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int? ContainerId { get; set; }

    [JsonIgnore]
    public bool IsIsp => TopologyNodeKinds.IsIsp(Kind);

    [JsonIgnore]
    public bool IsContainer => TopologyNodeKinds.IsContainer(Kind);

    [JsonIgnore]
    public bool IsDevice => !IsIsp && !IsContainer;
}

public sealed class TopologyLinkDto
{
    public int Id { get; set; }
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public int FromDeviceId { get; set; }
    public int ToDeviceId { get; set; }
    public string Kind { get; set; } = TopologyLinkKinds.Ethernet;
    public string? Color { get; set; }
    public bool? Dashed { get; set; }
    public string? Label { get; set; }
    /// <summary>相对连线中点的垂直偏移，0 为直线，正负表示两侧弯曲。</summary>
    public double Curve { get; set; }
}

public static class TopologyNodeKinds
{
    public const string Device = "device";
    public const string Isp = "isp";
    public const string Container = "container";

    public static string Normalize(string? kind, int deviceId)
    {
        var value = kind?.Trim().ToLowerInvariant();
        if (value == Container)
        {
            return Container;
        }

        if (value == Isp)
        {
            return Isp;
        }

        if (value == Device || deviceId > 0)
        {
            return Device;
        }

        return Isp;
    }

    public static bool IsIsp(string? kind) =>
        string.Equals(kind?.Trim(), Isp, StringComparison.OrdinalIgnoreCase);

    public static bool IsContainer(string? kind) =>
        string.Equals(kind?.Trim(), Container, StringComparison.OrdinalIgnoreCase);
}

public static class TopologyLinkKinds
{
    public const string Ethernet = "ethernet";
    public const string Fiber = "fiber";
    public const string Wireless = "wireless";
    public const string Bluetooth = "bluetooth";
    public const string Iptv = "iptv";
    public const string Custom = "custom";

    public const string EthernetColor = "#6B7280";
    public const string FiberColor = "#EAB308";
    public const string WirelessColor = "#000000";
    public const string BluetoothColor = "#2563EB";
    public const string IptvColor = "#DC2626";
    public const string CustomColor = "#7C3AED";

    public static string Normalize(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        Fiber => Fiber,
        Wireless => Wireless,
        Bluetooth => Bluetooth,
        Iptv => Iptv,
        Custom => Custom,
        _ => Ethernet
    };

    public static string DefaultColor(string? kind) => Normalize(kind) switch
    {
        Fiber => FiberColor,
        Wireless => WirelessColor,
        Bluetooth => BluetoothColor,
        Iptv => IptvColor,
        Custom => CustomColor,
        _ => EthernetColor
    };

    public static bool DefaultDashed(string? kind)
    {
        var value = Normalize(kind);
        return value is Wireless or Bluetooth;
    }

    public static string ResolveColor(TopologyLinkDto link)
    {
        var hex = link.Color?.Trim();
        return string.IsNullOrWhiteSpace(hex) ? DefaultColor(link.Kind) : hex;
    }

    public static bool ResolveDashed(TopologyLinkDto link) =>
        link.Dashed ?? DefaultDashed(link.Kind);
}
