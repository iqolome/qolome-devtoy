using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Scanning;
using Windows.UI;

namespace Qolome.Nets.App.ViewModels;

public enum TopologyTool
{
    Move,
    Ethernet,
    Fiber,
    Wireless,
    Bluetooth,
    Iptv,
    Custom
}

public partial class TopologyViewModel : ObservableObject
{
    private const int MaxUndo = 50;
    private readonly InventoryService _inventory = AppServices.Inventory;
    private readonly TopologyStore _store = new();
    private readonly List<TopologyDocument> _undo = [];
    private bool _suppressGridDirty;

    public ObservableCollection<NetworkEnvironment> Environments { get; } = [];
    public ObservableCollection<TopologyDeviceItem> Devices { get; } = [];

    public TopologyDocument Document { get; private set; } = new();

    public IReadOnlyList<Device> SourceDevices { get; private set; } = [];

    [ObservableProperty]
    public partial NetworkEnvironment? SelectedEnvironment { get; set; }

    [ObservableProperty]
    public partial TopologyDeviceItem? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial string DeviceQuery { get; set; } = "";

    [ObservableProperty]
    public partial bool SortIpDescending { get; set; }

    [ObservableProperty]
    public partial TopologyTool Tool { get; set; } = TopologyTool.Move;

    [ObservableProperty]
    public partial int ToolIndex { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "从左侧拖拽设备放到画布。Ctrl+Z 撤销；空格拖动画布；拖空白处框选后可批量移动。";

    [ObservableProperty]
    public partial string CustomColorHex { get; set; } = TopologyLinkKinds.CustomColor;

    [ObservableProperty]
    public partial bool CustomDashed { get; set; }

    [ObservableProperty]
    public partial string CustomLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowCustomStyle { get; set; }

    [ObservableProperty]
    public partial bool ShowGrid { get; set; } = true;

    [ObservableProperty]
    public partial bool SnapToGrid { get; set; } = true;

    [ObservableProperty]
    public partial bool CanUndo { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string CloudSyncText { get; set; } = "未同步";

    [ObservableProperty]
    public partial string CloudSyncHint { get; set; } = "尚未同步到 MySQL。";

    [ObservableProperty]
    public partial string CloudSyncGlyph { get; set; } = "\uE753";

    [ObservableProperty]
    public partial Brush CloudSyncBrush { get; set; } = UnsyncedBrush;

    [ObservableProperty]
    public partial bool CloudSyncBusy { get; set; }

    [ObservableProperty]
    public partial bool CloudSyncEnabled { get; set; } = true;

    [ObservableProperty]
    public partial Visibility CloudIconVisibility { get; set; } = Visibility.Visible;

    [ObservableProperty]
    public partial Visibility CloudBusyVisibility { get; set; } = Visibility.Collapsed;

    private static readonly SolidColorBrush SyncedBrush = new(Color.FromArgb(255, 22, 163, 74));
    private static readonly SolidColorBrush UnsyncedBrush = new(Color.FromArgb(255, 217, 119, 6));
    private static readonly SolidColorBrush SyncingBrush = new(Color.FromArgb(255, 37, 99, 235));

    private bool _cloudSynced;
    private bool _cloudBusy;

    public string IpSortHeader => SortIpDescending ? "IP ↓" : "IP ↑";

    public event EventHandler? TopologyReloaded;
    public event EventHandler? TopologyLayoutChanged;

    private bool _suppressReload;

    public string CurrentLinkKind => Tool switch
    {
        TopologyTool.Fiber => TopologyLinkKinds.Fiber,
        TopologyTool.Wireless => TopologyLinkKinds.Wireless,
        TopologyTool.Bluetooth => TopologyLinkKinds.Bluetooth,
        TopologyTool.Iptv => TopologyLinkKinds.Iptv,
        TopologyTool.Custom => TopologyLinkKinds.Custom,
        _ => TopologyLinkKinds.Ethernet
    };

    public bool IsLinkTool => Tool is not TopologyTool.Move;

    partial void OnToolIndexChanged(int value)
    {
        Tool = value switch
        {
            1 => TopologyTool.Ethernet,
            2 => TopologyTool.Fiber,
            3 => TopologyTool.Wireless,
            4 => TopologyTool.Bluetooth,
            5 => TopologyTool.Iptv,
            6 => TopologyTool.Custom,
            _ => TopologyTool.Move
        };
        ShowCustomStyle = Tool == TopologyTool.Custom;
        Status = Tool switch
        {
            TopologyTool.Move => "拖动节点调整位置；拖空白处框选；空格拖动画布。Ctrl 点选可多选，Delete 删除。",
            TopologyTool.Ethernet => "灰实线表示网线。依次点击两个节点。",
            TopologyTool.Fiber => "黄实线表示光纤。依次点击两个节点。",
            TopologyTool.Wireless => "黑虚线表示无线。依次点击两个节点。",
            TopologyTool.Bluetooth => "蓝虚线表示蓝牙。依次点击两个节点。",
            TopologyTool.Iptv => "红实线表示 IPTV。依次点击两个节点。",
            TopologyTool.Custom => "自定义连线：先设颜色、实线/虚线和批注，再依次点击两个节点。",
            _ => Status
        };
    }

    partial void OnShowGridChanged(bool value)
    {
        Document.ShowGrid = value;
        if (!_suppressGridDirty)
        {
            IsDirty = true;
        }
    }

    partial void OnSnapToGridChanged(bool value)
    {
        Document.SnapToGrid = value;
        if (!_suppressGridDirty)
        {
            IsDirty = true;
        }
    }

    public async Task LoadAsync()
    {
        var environments = await _inventory.GetEnvironmentsAsync();
        Environments.Clear();
        foreach (var env in environments)
        {
            Environments.Add(env);
        }

        _suppressReload = true;
        try
        {
            SelectedEnvironment = Environments.FirstOrDefault(e => e.Id == AppServices.SelectedEnvironmentId)
                ?? Environments.FirstOrDefault();
        }
        finally
        {
            _suppressReload = false;
        }

        AppServices.SelectedEnvironmentId = SelectedEnvironment?.Id;
        await ReloadDocumentAsync();
    }

    partial void OnSelectedEnvironmentChanged(NetworkEnvironment? oldValue, NetworkEnvironment? newValue)
    {
        if (_suppressReload)
        {
            return;
        }

        if (newValue is null)
        {
            if (oldValue is not null)
            {
                _suppressReload = true;
                try
                {
                    SelectedEnvironment = oldValue;
                }
                finally
                {
                    _suppressReload = false;
                }
            }

            return;
        }

        AppServices.SelectedEnvironmentId = newValue.Id;
        _ = SwitchEnvironmentAsync(oldValue, newValue);
    }

    private async Task SwitchEnvironmentAsync(NetworkEnvironment? oldValue, NetworkEnvironment? newValue)
    {
        if (IsDirty && oldValue is not null && Document.EnvironmentId == oldValue.Id)
        {
            try
            {
                await _store.SaveAsync(Document);
                IsDirty = false;
            }
            catch (Exception ex)
            {
                Status = $"自动保存上一环境失败：{ex.Message}";
            }
        }

        await ReloadDocumentAsync();
    }

    public async Task ReloadDocumentAsync()
    {
        Devices.Clear();
        SourceDevices = [];
        Document = new TopologyDocument { EnvironmentId = SelectedEnvironment?.Id ?? 0 };
        ClearUndo();
        if (SelectedEnvironment is null)
        {
            TopologyMetrics.Apply(0, 0);
            SyncGridFlags();
            _cloudSynced = false;
            IsDirty = false;
            Status = "请先选择网络环境。";
            RefreshCloudSync();
            TopologyReloaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        SourceDevices = await _inventory.GetDevicesAsync(SelectedEnvironment.Id, null);
        var ids = SourceDevices.Select(d => d.Id).ToHashSet();
        Document = await _store.LoadAsync(SelectedEnvironment.Id, ids);
        TopologyMetrics.Apply(Document.CanvasWidth, Document.CanvasHeight);
        TopologyMetrics.GrowToFitContent(Document.Nodes);
        TopologyMetrics.CopyTo(Document);
        SyncGridFlags();
        RefreshDeviceList();
        _cloudSynced = await _store.IsRemoteCurrentAsync(SelectedEnvironment.Id, Document.UpdatedAtUtc);
        IsDirty = false;
        RefreshCloudSync();
        var devices = Document.Nodes.Count(n => n.IsDevice);
        var isps = Document.Nodes.Count(n => n.IsIsp);
        var containers = Document.Nodes.Count(n => n.IsContainer);
        Status = containers > 0
            ? $"已加载「{SelectedEnvironment.Name}」拓扑：设备 {devices}，互联网 {isps}，容器 {containers}，连线 {Document.Links.Count}。"
            : $"已加载「{SelectedEnvironment.Name}」拓扑：设备 {devices}，互联网 {isps}，连线 {Document.Links.Count}。";
        TopologyReloaded?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshDeviceList()
    {
        var placed = Document.Nodes.Where(n => n.IsDevice).Select(n => n.DeviceId).ToHashSet();
        var selectedId = SelectedDevice?.Device.Id;
        Devices.Clear();

        IEnumerable<Device> source = SourceDevices;
        if (!string.IsNullOrWhiteSpace(DeviceQuery))
        {
            var keyword = DeviceQuery.Trim();
            source = source.Where(d =>
                d.Hostname?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || d.Ip?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || d.NicIps().Any(ip => ip.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                || d.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || d.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || d.DeviceType?.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || d.DeviceType?.Code?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || InventoryService.MatchesMacQuery(d.Mac, keyword)
                || d.OrderedNics().Any(n => InventoryService.MatchesMacQuery(n.Mac, keyword)));
        }

        source = SortIpDescending
            ? source.OrderBy(d => placed.Contains(d.Id))
                .ThenByDescending(d => d.Ip, IpAddressComparer.TextComparer)
                .ThenByDescending(d => d.Code, StringComparer.OrdinalIgnoreCase)
            : source.OrderBy(d => placed.Contains(d.Id))
                .ThenBy(d => d.Ip, IpAddressComparer.TextComparer)
                .ThenBy(d => d.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var device in source)
        {
            Devices.Add(new TopologyDeviceItem(device, placed.Contains(device.Id)));
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Device.Id == selectedId);
    }

    public void ToggleIpSort() => SortIpDescending = !SortIpDescending;

    partial void OnDeviceQueryChanged(string value) => RefreshDeviceList();

    partial void OnSortIpDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(IpSortHeader));
        RefreshDeviceList();
    }

    public void PushUndo()
    {
        _undo.Add(Document.CloneGraph());
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveAt(0);
        }

        RefreshCanUndo();
    }

    public void DiscardUndoIfUnchanged()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        if (GraphEquals(_undo[^1], Document))
        {
            _undo.RemoveAt(_undo.Count - 1);
            RefreshCanUndo();
        }
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Document.RestoreGraph(snapshot);
        IsDirty = true;
        RefreshDeviceList();
        RefreshCanUndo();
        Status = "已撤销上一步。";
        TopologyReloaded?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public TopologyNodeDto? PlaceDevice(int deviceId)
    {
        var index = Document.Nodes.Count;
        return PlaceDeviceAt(deviceId, DefaultSlotX(index), DefaultSlotY(index));
    }

    public TopologyNodeDto? PlaceDeviceAt(int deviceId, double x, double y)
    {
        var existing = Document.Nodes.FirstOrDefault(n => n.IsDevice && n.DeviceId == deviceId);
        if (existing is not null)
        {
            Status = "该设备已在画布上。";
            return existing;
        }

        if (SourceDevices.All(d => d.Id != deviceId))
        {
            Status = "设备不属于当前环境。";
            return null;
        }

        PushUndo();
        var (sx, sy) = SnapClamp(x, y);
        var node = new TopologyNodeDto
        {
            Id = deviceId,
            DeviceId = deviceId,
            Kind = TopologyNodeKinds.Device,
            X = sx,
            Y = sy
        };
        Document.Nodes.Add(node);
        IsDirty = true;
        RefreshDeviceList();
        Status = "已放到画布，可拖动位置。";
        return node;
    }

    public TopologyNodeDto AddIsp(string? label, string? ispKey = null)
    {
        PushUndo();
        var key = IspPresets.Normalize(ispKey, label);
        var text = IspPresets.IsCarrier(key)
            ? IspPresets.DefaultLabel(key)
            : (string.IsNullOrWhiteSpace(label) ? IspPresets.DefaultLabel(IspPresets.Custom) : label.Trim());
        var index = Document.Nodes.Count;
        var (sx, sy) = SnapClamp(DefaultSlotX(index), DefaultSlotY(index));
        var node = new TopologyNodeDto
        {
            Id = NextNegativeId(),
            DeviceId = 0,
            Kind = TopologyNodeKinds.Isp,
            IspKey = key,
            Label = text,
            X = sx,
            Y = sy
        };
        Document.Nodes.Add(node);
        IsDirty = true;
        Status = $"已添加互联网节点「{text}」，可拖动并连线。";
        return node;
    }

    public TopologyNodeDto AddContainer(string? name, IReadOnlyCollection<int>? memberIds = null)
    {
        PushUndo();
        var label = string.IsNullOrWhiteSpace(name) ? "容器" : name.Trim();
        var members = (memberIds ?? [])
            .Select(FindNode)
            .Where(n => n is { IsContainer: false })
            .Cast<TopologyNodeDto>()
            .DistinctBy(n => n.Id)
            .ToList();

        double x;
        double y;
        double width;
        double height;
        if (members.Count > 0)
        {
            var minX = members.Min(n => n.X);
            var minY = members.Min(n => n.Y);
            var maxX = members.Max(n => n.X + TopologyMetrics.WidthOf(n));
            var maxY = members.Max(n => n.Y + TopologyMetrics.HeightOf(n));
            x = minX - TopologyMetrics.ContainerPad;
            y = minY - TopologyMetrics.ContainerPad;
            width = maxX - minX + TopologyMetrics.ContainerPad * 2;
            height = maxY - minY + TopologyMetrics.ContainerPad * 2;
        }
        else
        {
            var index = Document.Nodes.Count;
            x = DefaultSlotX(index);
            y = DefaultSlotY(index);
            width = TopologyMetrics.ContainerMinWidth;
            height = TopologyMetrics.ContainerMinHeight;
        }

        var (sx, sy) = SnapClamp(x, y);
        var node = new TopologyNodeDto
        {
            Id = NextNegativeId(),
            DeviceId = 0,
            Kind = TopologyNodeKinds.Container,
            Label = label,
            X = sx,
            Y = sy,
            Width = Math.Max(TopologyMetrics.ContainerMinWidth, width),
            Height = Math.Max(TopologyMetrics.ContainerMinHeight, height)
        };
        Document.Nodes.Add(node);
        foreach (var member in members)
        {
            member.ContainerId = node.Id;
        }

        FitContainer(node.Id);
        IsDirty = true;
        Status = members.Count > 0
            ? $"已添加容器「{label}」，收纳 {members.Count} 个节点。"
            : $"已添加容器「{label}」。可将节点拖入其中。";
        return node;
    }

    public void SetNodeLabel(int nodeId, string? label, string? ispKey = null)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return;
        }

        PushUndo();
        if (node.IsIsp)
        {
            node.IspKey = IspPresets.Normalize(ispKey, label);
            node.Label = IspPresets.IsCarrier(node.IspKey)
                ? IspPresets.DefaultLabel(node.IspKey)
                : (string.IsNullOrWhiteSpace(label) ? IspPresets.DefaultLabel(IspPresets.Custom) : label.Trim());
        }
        else
        {
            node.Label = string.IsNullOrWhiteSpace(label)
                ? (node.IsContainer ? "容器" : null)
                : label.Trim();
        }

        IsDirty = true;
        Status = node.IsContainer ? "已更新容器名称。" : "已更新节点批注。";
    }

    public void SetLinkAnnotation(int linkId, string? label, string? colorHex, bool? dashed)
    {
        var link = FindLink(linkId);
        if (link is null)
        {
            return;
        }

        PushUndo();
        link.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            link.Color = colorHex.Trim();
        }

        if (dashed is not null)
        {
            link.Dashed = dashed;
        }

        IsDirty = true;
        Status = "已更新连线批注。";
    }

    public void RemoveNode(int nodeId, bool recordUndo = true)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return;
        }

        if (recordUndo)
        {
            PushUndo();
        }

        if (node.IsContainer)
        {
            foreach (var child in ChildrenOf(nodeId).ToList())
            {
                child.ContainerId = null;
            }
        }

        Document.Nodes.RemoveAll(n => n.Id == nodeId);
        Document.Links.RemoveAll(l => l.FromNodeId == nodeId || l.ToNodeId == nodeId);
        IsDirty = true;
        RefreshDeviceList();
        Status = node.IsContainer
            ? "已删除容器，内部节点仍保留。"
            : node.IsIsp ? "已从画布移除互联网节点。" : "已从画布移除设备。";
    }

    public TopologyLinkDto? Connect(int fromId, int toId)
    {
        if (fromId == toId)
        {
            return null;
        }

        var a = Math.Min(fromId, toId);
        var b = Math.Max(fromId, toId);
        var kind = CurrentLinkKind;
        var existing = Document.Links.FirstOrDefault(l =>
            l.FromNodeId == a && l.ToNodeId == b && TopologyLinkKinds.Normalize(l.Kind) == kind);
        PushUndo();
        if (existing is not null)
        {
            ApplyCurrentStyle(existing, kind);
            IsDirty = true;
            Status = "已更新连线类型。";
            return existing;
        }

        var link = new TopologyLinkDto
        {
            Id = NextLinkId(),
            FromNodeId = a,
            ToNodeId = b,
            FromDeviceId = a,
            ToDeviceId = b
        };
        ApplyCurrentStyle(link, kind);
        Document.Links.Add(link);
        IsDirty = true;
        Status = "已添加连线。";
        return link;
    }

    public void RemoveLink(int linkId, bool recordUndo = true)
    {
        if (FindLink(linkId) is null)
        {
            return;
        }

        if (recordUndo)
        {
            PushUndo();
        }

        Document.Links.RemoveAll(l => l.Id == linkId);
        IsDirty = true;
        Status = "已删除连线。";
    }

    public void DeleteSelection(IReadOnlyCollection<int> nodeIds, IReadOnlyCollection<int> linkIds)
    {
        if (nodeIds.Count == 0 && linkIds.Count == 0)
        {
            return;
        }

        PushUndo();
        foreach (var linkId in linkIds)
        {
            RemoveLink(linkId, recordUndo: false);
        }

        foreach (var nodeId in nodeIds)
        {
            RemoveNode(nodeId, recordUndo: false);
        }
    }

    public void MoveNode(int nodeId, double x, double y, bool snap = true)
    {
        var node = FindNode(nodeId);
        if (node is null)
        {
            return;
        }

        if (snap)
        {
            x = TopologyMetrics.Snap(x);
            y = TopologyMetrics.Snap(y);
        }

        (x, y) = TopologyMetrics.ClampPosition(node, x, y);

        var dx = x - node.X;
        var dy = y - node.Y;
        node.X = x;
        node.Y = y;
        if (node.IsContainer && (dx != 0 || dy != 0))
        {
            foreach (var child in ChildrenOf(nodeId).ToList())
            {
                var (cx, cy) = TopologyMetrics.ClampPosition(child, child.X + dx, child.Y + dy);
                child.X = cx;
                child.Y = cy;
            }
        }

        IsDirty = true;
    }

    public IReadOnlyList<TopologyNodeDto> ChildrenOf(int containerId) =>
        Document.Nodes.Where(n => n.ContainerId == containerId).ToList();

    public void AssignContainersByPosition(IEnumerable<int> nodeIds)
    {
        var moved = nodeIds.Select(FindNode).Where(n => n is not null).Cast<TopologyNodeDto>().ToList();
        if (moved.Count == 0)
        {
            return;
        }

        var affected = false;
        foreach (var node in moved)
        {
            if (node.IsContainer)
            {
                continue;
            }

            var parent = FindContainerForNode(node);
            var nextId = parent?.Id;
            if (node.ContainerId == nextId)
            {
                continue;
            }

            node.ContainerId = nextId;
            affected = true;
        }

        if (affected)
        {
            IsDirty = true;
        }
    }

    public TopologyNodeDto? FindContainerForNode(TopologyNodeDto node)
    {
        var nw = TopologyMetrics.WidthOf(node);
        var nh = TopologyMetrics.HeightOf(node);
        var cx = node.X + nw / 2;
        var cy = node.Y + nh / 2;
        TopologyNodeDto? best = null;
        var bestArea = double.MaxValue;
        foreach (var container in Document.Nodes.Where(n => n.IsContainer && n.Id != node.Id))
        {
            var x = container.X;
            var y = container.Y;
            var w = TopologyMetrics.WidthOf(container);
            var h = TopologyMetrics.HeightOf(container);
            var centerInside = cx >= x && cy >= y && cx <= x + w && cy <= y + h;
            if (!centerInside)
            {
                continue;
            }

            var area = w * h;
            if (area < bestArea)
            {
                best = container;
                bestArea = area;
            }
        }

        return best;
    }

    public TopologyNodeDto? FindContainerAt(double x, double y, int? excludeId = null)
    {
        TopologyNodeDto? best = null;
        var bestArea = double.MaxValue;
        foreach (var node in Document.Nodes.Where(n => n.IsContainer && n.Id != excludeId))
        {
            var w = TopologyMetrics.WidthOf(node);
            var h = TopologyMetrics.HeightOf(node);
            if (x < node.X || y < node.Y || x > node.X + w || y > node.Y + h)
            {
                continue;
            }

            var area = w * h;
            if (area < bestArea)
            {
                best = node;
                bestArea = area;
            }
        }

        return best;
    }

    public void ExpandContainerToFit(int containerId, bool growOnly = true)
    {
        var container = FindNode(containerId);
        if (container is not { IsContainer: true })
        {
            return;
        }

        if (!TryChildrenBox(containerId, out var left, out var top, out var right, out var bottom))
        {
            container.Width = Math.Max(container.Width, TopologyMetrics.ContainerMinWidth);
            container.Height = Math.Max(container.Height, TopologyMetrics.ContainerMinHeight);
            return;
        }

        if (growOnly)
        {
            var x = Math.Min(container.X, left);
            var y = Math.Min(container.Y, top);
            var r = Math.Max(container.X + TopologyMetrics.WidthOf(container), right);
            var b = Math.Max(container.Y + TopologyMetrics.HeightOf(container), bottom);
            SetContainerBounds(containerId, x, y, r - x, b - y, containChildren: false);
            return;
        }

        SetContainerBounds(containerId, left, top, right - left, bottom - top, containChildren: false);
    }

    public void SetContainerBounds(int containerId, double x, double y, double width, double height, bool containChildren = true)
    {
        var container = FindNode(containerId);
        if (container is not { IsContainer: true })
        {
            return;
        }

        if (containChildren && TryChildrenBox(containerId, out var left, out var top, out var right, out var bottom))
        {
            x = Math.Min(x, left);
            y = Math.Min(y, top);
            width = Math.Max(width, right - x);
            height = Math.Max(height, bottom - y);
        }

        width = Math.Max(width, TopologyMetrics.ContainerMinWidth);
        height = Math.Max(height, TopologyMetrics.ContainerMinHeight);
        TopologyMetrics.GrowToFit(
            x + width + TopologyMetrics.CanvasEdgeReserve,
            y + height + TopologyMetrics.ContainerCaptionGap + TopologyMetrics.ContainerCaptionHeight
                + TopologyMetrics.CanvasEdgeReserve);
        x = TopologyMetrics.ClampTo(x, Math.Max(0, TopologyMetrics.CanvasWidth - width));
        y = TopologyMetrics.ClampTo(y, Math.Max(0, TopologyMetrics.CanvasHeight - height
            - TopologyMetrics.ContainerCaptionGap - TopologyMetrics.ContainerCaptionHeight));

        container.X = x;
        container.Y = y;
        container.Width = width;
        container.Height = height;
        IsDirty = true;
    }

    public void FitContainer(int containerId, bool keepOrigin = false) =>
        ExpandContainerToFit(containerId, growOnly: keepOrigin);

    private bool TryChildrenBox(int containerId, out double left, out double top, out double right, out double bottom)
    {
        var children = ChildrenOf(containerId);
        if (children.Count == 0)
        {
            left = top = right = bottom = 0;
            return false;
        }

        left = children.Min(n => n.X) - TopologyMetrics.ContainerPad;
        top = children.Min(n => n.Y) - TopologyMetrics.ContainerPad;
        right = children.Max(n => n.X + TopologyMetrics.WidthOf(n)) + TopologyMetrics.ContainerPad;
        bottom = children.Max(n => n.Y + TopologyMetrics.HeightOf(n)) + TopologyMetrics.ContainerPad;
        return true;
    }

    public void SetLinkCurve(int linkId, double curve)
    {
        var link = FindLink(linkId);
        if (link is null)
        {
            return;
        }

        if (double.IsNaN(curve) || double.IsInfinity(curve))
        {
            curve = 0;
        }

        link.Curve = Math.Clamp(curve, -480, 480);
        IsDirty = true;
    }

    public void ArrangeLayers(bool leftToRight)
    {
        if (Document.Nodes.Count == 0)
        {
            Status = "画布上没有节点。";
            return;
        }

        PushUndo();
        var topLevel = Document.Nodes.Where(n => n.ContainerId is null).ToList();
        var topLinks = LinksForLayout(topLevel.Select(n => n.Id).ToHashSet());
        var layers = TopologyLayerLayout.AssignLayers(topLevel, topLinks);
        var groups = TopologyLayerLayout.OrderLayers(topLevel, topLinks, layers, orderAlongX: !leftToRight);
        PlaceLayerGroups(groups, leftToRight);
        foreach (var container in Document.Nodes.Where(n => n.IsContainer).ToList())
        {
            PackContainerChildren(container);
        }

        TopologyMetrics.GrowToFitContent(Document.Nodes);
        TopologyMetrics.CopyTo(Document);

        IsDirty = true;
        Status = leftToRight ? "已按左右分级排列，并减少连线交叉。" : "已按上下分级排列，并减少连线交叉。";
        TopologyLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<TopologyLinkDto> LinksForLayout(HashSet<int> nodeIds)
    {
        int Map(int id)
        {
            var node = FindNode(id);
            if (node is null)
            {
                return id;
            }

            if (node.ContainerId is int parent && nodeIds.Contains(parent))
            {
                return parent;
            }

            return id;
        }

        var links = new List<TopologyLinkDto>();
        var seen = new HashSet<(int, int)>();
        foreach (var link in Document.Links)
        {
            var from = Map(link.FromNodeId);
            var to = Map(link.ToNodeId);
            if (from == to || !nodeIds.Contains(from) || !nodeIds.Contains(to) || !seen.Add((Math.Min(from, to), Math.Max(from, to))))
            {
                continue;
            }

            links.Add(new TopologyLinkDto
            {
                Id = link.Id,
                FromNodeId = from,
                ToNodeId = to
            });
        }

        return links;
    }

    private static void PlaceLayerGroups(List<List<TopologyNodeDto>> groups, bool leftToRight)
    {
        const double pad = 48;
        const double gap = 24;
        var layerGap = 36.0;
        if (leftToRight)
        {
            var x = pad;
            foreach (var ordered in groups)
            {
                var y = pad;
                var colW = ordered.Count == 0 ? TopologyMetrics.NodeWidth : ordered.Max(TopologyMetrics.WidthOf);
                foreach (var node in ordered)
                {
                    var h = TopologyMetrics.LayoutHeightOf(node);
                    if (y + h > TopologyMetrics.MinCanvasHeight - pad && y > pad)
                    {
                        y = pad;
                        x += colW + gap;
                        colW = TopologyMetrics.WidthOf(node);
                    }

                    colW = Math.Max(colW, TopologyMetrics.WidthOf(node));
                    ApplyLayoutPosition(node, x, y);
                    y += h + gap;
                }

                x += colW + layerGap;
            }

            return;
        }

        var yCursor = pad;
        foreach (var ordered in groups)
        {
            var x = pad;
            var rowH = ordered.Count == 0 ? TopologyMetrics.NodeHeight : ordered.Max(TopologyMetrics.LayoutHeightOf);
            foreach (var node in ordered)
            {
                var w = TopologyMetrics.WidthOf(node);
                if (x + w > TopologyMetrics.MinCanvasWidth - pad && x > pad)
                {
                    x = pad;
                    yCursor += rowH + gap;
                    rowH = TopologyMetrics.LayoutHeightOf(node);
                }

                rowH = Math.Max(rowH, TopologyMetrics.LayoutHeightOf(node));
                ApplyLayoutPosition(node, x, yCursor);
                x += w + gap;
            }

            yCursor += rowH + layerGap;
        }
    }

    private void PackContainerChildren(TopologyNodeDto container)
    {
        var children = ChildrenOf(container.Id);
        if (children.Count == 0)
        {
            container.Width = Math.Max(container.Width, TopologyMetrics.ContainerMinWidth);
            container.Height = Math.Max(container.Height, TopologyMetrics.ContainerMinHeight);
            return;
        }

        var childIds = children.Select(n => n.Id).ToHashSet();
        var innerLinks = Document.Links
            .Where(l => childIds.Contains(l.FromNodeId) && childIds.Contains(l.ToNodeId))
            .ToList();
        var layers = TopologyLayerLayout.AssignLayers(children, innerLinks);
        var groups = TopologyLayerLayout.OrderLayers(children, innerLinks, layers, orderAlongX: true);
        var x0 = container.X + TopologyMetrics.ContainerPad;
        var y0 = container.Y + TopologyMetrics.ContainerPad;
        var gap = 16.0;
        var y = y0;
        foreach (var row in groups)
        {
            var x = x0;
            var rowH = TopologyMetrics.NodeHeight;
            foreach (var child in row)
            {
                ApplyLayoutPosition(child, x, y);
                x += TopologyMetrics.WidthOf(child) + gap;
                rowH = Math.Max(rowH, TopologyMetrics.HeightOf(child));
            }

            y += rowH + gap;
        }

        FitContainer(container.Id, keepOrigin: true);
    }

    private static void ApplyLayoutPosition(TopologyNodeDto node, double x, double y)
    {
        (node.X, node.Y) = TopologyMetrics.ClampPosition(node, x, y);
    }

    public TopologyNodeDto? FindNode(int nodeId) => Document.Nodes.FirstOrDefault(n => n.Id == nodeId);

    public TopologyLinkDto? FindLink(int linkId) => Document.Links.FirstOrDefault(l => l.Id == linkId);

    private int NextLinkId()
    {
        var max = Document.Links.Select(l => l.Id).DefaultIfEmpty(0).Max();
        return max + 1;
    }

    public Device? FindDevice(int deviceId) => SourceDevices.FirstOrDefault(d => d.Id == deviceId);

    public async Task SaveAsync()
    {
        if (SelectedEnvironment is null)
        {
            throw new InvalidOperationException("请先选择网络环境。");
        }

        Document.EnvironmentId = SelectedEnvironment.Id;
        Document.ShowGrid = ShowGrid;
        Document.SnapToGrid = SnapToGrid;
        TopologyMetrics.CopyTo(Document);
        try
        {
            await _store.SaveAsync(Document);
            // 仅落本地库；与远程是否一致需再点右上角云同步。
            _cloudSynced = false;
            IsDirty = false;
            Status = $"已保存到本地：{Document.Nodes.Count} 个节点，{Document.Links.Count} 条连线。点击右上角可同步到云端。";
            RefreshCloudSync();
        }
        catch
        {
            _cloudSynced = false;
            RefreshCloudSync();
            throw;
        }
    }

    public async Task SyncCloudAsync()
    {
        if (_cloudBusy)
        {
            return;
        }

        if (SelectedEnvironment is null)
        {
            Status = "请先选择网络环境。";
            return;
        }

        _cloudBusy = true;
        RefreshCloudSync();
        try
        {
            Document.EnvironmentId = SelectedEnvironment.Id;
            Document.ShowGrid = ShowGrid;
            Document.SnapToGrid = SnapToGrid;
            TopologyMetrics.CopyTo(Document);
            await _store.PushToRemoteAsync(Document);
            _cloudSynced = true;
            IsDirty = false;
            Status = $"云端同步成功：{Document.Nodes.Count} 个节点，{Document.Links.Count} 条连线。";
        }
        catch (Exception ex)
        {
            _cloudSynced = false;
            Status = ex.Message;
        }
        finally
        {
            _cloudBusy = false;
            RefreshCloudSync();
        }
    }

    partial void OnIsDirtyChanged(bool value) => RefreshCloudSync();

    private void RefreshCloudSync()
    {
        if (_cloudBusy)
        {
            CloudSyncText = "同步中";
            CloudSyncGlyph = "\uE895";
            CloudSyncBrush = SyncingBrush;
            CloudSyncHint = "正在将本地拓扑同步到远程数据源…";
            CloudSyncBusy = true;
            CloudSyncEnabled = false;
            CloudIconVisibility = Visibility.Collapsed;
            CloudBusyVisibility = Visibility.Visible;
            return;
        }

        CloudSyncBusy = false;
        CloudSyncEnabled = true;
        CloudIconVisibility = Visibility.Visible;
        CloudBusyVisibility = Visibility.Collapsed;

        if (!IsDirty && _cloudSynced)
        {
            CloudSyncText = "云端已同步";
            CloudSyncGlyph = "\uE753";
            CloudSyncBrush = SyncedBrush;
            CloudSyncHint = "本地与远程拓扑已一致。修改并保存后可再次点击同步。";
            return;
        }

        CloudSyncText = IsDirty ? "有未保存更改" : "未同步到云端";
        CloudSyncGlyph = "\uE753";
        CloudSyncBrush = UnsyncedBrush;
        CloudSyncHint = IsDirty
            ? "先点「保存」写入本地，再点此按钮同步到远程；或直接点此按钮将自动先保存再同步。"
            : "本地已保存，点击将 SQLite 中的拓扑推送到「数据源设置」远程库。";
    }

    private void ApplyCurrentStyle(TopologyLinkDto link, string kind)
    {
        link.Kind = kind;
        if (kind == TopologyLinkKinds.Custom)
        {
            link.Color = string.IsNullOrWhiteSpace(CustomColorHex) ? TopologyLinkKinds.CustomColor : CustomColorHex.Trim();
            link.Dashed = CustomDashed;
            if (!string.IsNullOrWhiteSpace(CustomLabel))
            {
                link.Label = CustomLabel.Trim();
            }
        }
        else
        {
            link.Color = null;
            link.Dashed = null;
        }
    }

    private int NextNegativeId()
    {
        var min = Document.Nodes.Where(n => n.Id < 0).Select(n => n.Id).DefaultIfEmpty(0).Min();
        return min >= 0 ? -1 : min - 1;
    }

    private static double DefaultSlotX(int index) =>
        48 + (index % 8) * (TopologyMetrics.NodeWidth + 16);

    private static double DefaultSlotY(int index) =>
        48 + (index / 8) * (TopologyMetrics.NodeHeight + 16);

    private (double X, double Y) SnapClamp(double x, double y)
    {
        if (SnapToGrid)
        {
            x = TopologyMetrics.Snap(x);
            y = TopologyMetrics.Snap(y);
        }

        return (TopologyMetrics.ClampX(x), TopologyMetrics.ClampY(y));
    }

    private void SyncGridFlags()
    {
        _suppressGridDirty = true;
        try
        {
            ShowGrid = Document.ShowGrid;
            SnapToGrid = Document.SnapToGrid;
        }
        finally
        {
            _suppressGridDirty = false;
        }
    }

    private void ClearUndo()
    {
        _undo.Clear();
        RefreshCanUndo();
    }

    private void RefreshCanUndo() => CanUndo = _undo.Count > 0;

    private static bool GraphEquals(TopologyDocument a, TopologyDocument b)
    {
        if (a.Nodes.Count != b.Nodes.Count || a.Links.Count != b.Links.Count)
        {
            return false;
        }

        foreach (var node in a.Nodes)
        {
            var other = b.Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (other is null
                || other.X != node.X
                || other.Y != node.Y
                || other.Kind != node.Kind
                || other.DeviceId != node.DeviceId
                || other.IspKey != node.IspKey
                || other.Label != node.Label
                || other.Width != node.Width
                || other.Height != node.Height
                || other.ContainerId != node.ContainerId)
            {
                return false;
            }
        }

        foreach (var link in a.Links)
        {
            var other = b.Links.FirstOrDefault(l => l.Id == link.Id);
            if (other is null
                || other.FromNodeId != link.FromNodeId
                || other.ToNodeId != link.ToNodeId
                || other.Curve != link.Curve
                || other.Kind != link.Kind
                || other.Label != link.Label
                || other.Color != link.Color
                || other.Dashed != link.Dashed)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class TopologyDeviceItem
{
    public TopologyDeviceItem(Device device, bool isPlaced)
    {
        Device = device;
        IsPlaced = isPlaced;
    }

    public Device Device { get; }
    public bool IsPlaced { get; }
    public ImageSource? Icon => DeviceTypeIconXaml.FromType(Device.DeviceType);
    public string Title => string.IsNullOrWhiteSpace(Device.Hostname) ? Device.Code : Device.Hostname!;
    public string Subtitle => string.IsNullOrWhiteSpace(Device.Ip) ? Device.Code : Device.Ip!;
    public string TypeName => Device.TypeName;
    public string DescriptionText =>
        string.IsNullOrWhiteSpace(Device.Description) ? "—" : Device.Description.Trim();
    public string TypeLine => $"类型：{TypeName}";
    public string DescriptionLine => $"说明：{DescriptionText}";
    public string PlaceHint => IsPlaced ? "已在画布" : "拖到画布";
    public string DetailTip => $"{Title}\n{Subtitle}\n{TypeLine}\n{DescriptionLine}";
}
