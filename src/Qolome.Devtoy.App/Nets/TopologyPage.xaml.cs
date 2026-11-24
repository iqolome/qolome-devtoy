using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Qolome.Nets.App.ViewModels;
using Qolome.Nets.Core.Data;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace Qolome.Nets.App;

public sealed partial class TopologyPage : Page
{
    private const double NodeWidth = TopologyMetrics.NodeWidth;
    private const double NodeHeight = TopologyMetrics.NodeHeight;
    private const double LinkOffset = 12;
    private const string DeviceDragFormat = "qolome.nets.device-id";
    private const string DeviceDragPrefix = "qolome.nets.device-id:";

    private static readonly SolidColorBrush HitBrush = new(Color.FromArgb(1, 0, 0, 0));
    private static readonly SolidColorBrush SelectedNodeBorder = new(Color.FromArgb(255, 37, 99, 235));
    private static readonly SolidColorBrush PendingNodeBorder = new(Color.FromArgb(255, 217, 119, 6));
    private static readonly SolidColorBrush LabelBackground = new(Color.FromArgb(230, 255, 255, 255));
    private static readonly SolidColorBrush GridStroke = new(Color.FromArgb(36, 100, 116, 139));
    private static readonly SolidColorBrush MarqueeFill = new(Color.FromArgb(40, 37, 99, 235));
    private static readonly SolidColorBrush ContainerFill = new(Color.FromArgb(42, 37, 99, 235));
    private static readonly SolidColorBrush ContainerStroke = new(Color.FromArgb(170, 37, 99, 235));

    private readonly Dictionary<int, Border> _nodeVisuals = [];
    private readonly Dictionary<int, TextBlock> _containerCaptions = [];
    private readonly Dictionary<int, LinkVisual> _linkVisuals = [];
    private readonly Dictionary<int, Point> _dragStartPositions = [];

    private int? _pendingFromId;
    private int? _selectedNodeId;
    private int? _selectedLinkId;
    private readonly HashSet<int> _selectedNodeIds = [];
    private readonly HashSet<int> _selectedLinkIds = [];

    private bool _dragging;
    private int _dragNodeId;
    private Point _dragGrab;
    private bool _dragUndoPushed;

    private bool _spaceDown;
    private bool _panning;
    private Point _panPointerStart;
    private double _panScrollX;
    private double _panScrollY;
    private UIElement? _panCapture;

    private bool _marqueeing;
    private Point _marqueeStart;
    private bool _marqueeAdditive;
    private Rectangle? _marquee;

    private bool _curvePending;
    private bool _curveDragging;
    private int _curveLinkId;
    private Point _curvePress;
    private Point _curveA;
    private Point _curveB;

    private bool _resizing;
    private int _resizeNodeId;
    private ResizeEdges _resizeEdges;
    private Rect _resizeStart;
    private Point _resizePointerStart;
    private DispatcherTimer? _edgePanTimer;
    private Point _lastPointerHost;
    private bool _hasPointerHost;

    private UIElement? _gridVisual;
    private bool _syncingTools;

    public TopologyViewModel ViewModel { get; } = new();

    public TopologyPage()
    {
        InitializeComponent();
        DeviceList.DragItemsStarting += DeviceList_DragItemsStarting;
        DeviceList.ContainerContentChanging += DeviceList_ContainerContentChanging;
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Board.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        BoardHost.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        SuppressSystemDrag(Board);
        EnableDeviceDrop(Board);
        EnableDeviceDrop(BoardHost);
        SetIspMenuIcon(AddIspCmccItem, IspPresets.Cmcc);
        SetIspMenuIcon(AddIspUnicomItem, IspPresets.Unicom);
        SetIspMenuIcon(AddIspTelecomItem, IspPresets.Telecom);
        SetIspMenuIcon(AddIspCustomItem, IspPresets.Custom);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && int.TryParse(element.Tag?.ToString(), out var index))
        {
            ViewModel.ToolIndex = index;
        }

        SyncToolButtons();
    }

    private void SyncToolButtons()
    {
        if (_syncingTools)
        {
            return;
        }

        _syncingTools = true;
        try
        {
            var index = ViewModel.ToolIndex;
            ToolMove.IsChecked = index == 0;
            ToolEthernet.IsChecked = index == 1;
            ToolFiber.IsChecked = index == 2;
            ToolWireless.IsChecked = index == 3;
            ToolBluetooth.IsChecked = index == 4;
            ToolIptv.IsChecked = index == 5;
            ToolCustom.IsChecked = index == 6;
        }
        finally
        {
            _syncingTools = false;
        }
    }

    private void SyncGridToggles()
    {
        ToggleGrid.IsChecked = ViewModel.ShowGrid;
        ToggleSnap.IsChecked = ViewModel.SnapToGrid;
    }

    private static void SetIspMenuIcon(MenuFlyoutItem item, string key)
    {
        var source = DeviceTypeIconXaml.Isp(key);
        if (source is not null)
        {
            item.Icon = new ImageIcon { Source = source };
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.TopologyReloaded += OnTopologyReloaded;
        ViewModel.TopologyLayoutChanged += OnTopologyLayoutChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        await ViewModel.LoadAsync();
        SyncToolButtons();
        SyncGridToggles();
        SyncCustomStylePanel();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.TopologyReloaded -= OnTopologyReloaded;
        ViewModel.TopologyLayoutChanged -= OnTopologyLayoutChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        StopEdgePan();
        if (!ViewModel.IsDirty)
        {
            return;
        }

        try
        {
            await ViewModel.SaveAsync();
        }
        catch
        {
            // 离开页面时尽力保存，失败不打断导航。
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.ShowCustomStyle) or null)
        {
            SyncCustomStylePanel();
        }

        if (e.PropertyName is nameof(ViewModel.ToolIndex) or null)
        {
            SyncToolButtons();
        }

        if (e.PropertyName is nameof(ViewModel.ShowGrid) or nameof(ViewModel.SnapToGrid) or null)
        {
            SyncGridToggles();
        }
    }

    private void SyncCustomStylePanel()
    {
        CustomStylePanel.Visibility = ViewModel.ShowCustomStyle ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTopologyReloaded(object? sender, EventArgs e)
    {
        StopEdgePan();
        _pendingFromId = null;
        _dragging = false;
        _panning = false;
        _marqueeing = false;
        _curvePending = false;
        _curveDragging = false;
        _gridVisual = null;
        _marquee = null;
        _dragStartPositions.Clear();
        ClearSelection();
        RebuildCanvas();
    }

    private void OnTopologyLayoutChanged(object? sender, EventArgs e)
    {
        SyncCanvasSize();
        ApplyExistingNodePositions();
        QueueCenterOnContent(fit: true);
    }

    private void ApplyExistingNodePositions()
    {
        var nodes = ViewModel.Document.Nodes;
        if (nodes.Count == 0)
        {
            return;
        }

        if (_nodeVisuals.Count != nodes.Count
            || nodes.Any(n => !_nodeVisuals.ContainsKey(n.Id)))
        {
            RebuildCanvas();
            return;
        }

        foreach (var node in nodes)
        {
            UpdateNodePosition(node.Id);
        }
    }

    private void SortIp_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleIpSort();

    private static void DeviceList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        container.Tag = args.Item;
        container.CanDrag = true;
        container.DragStarting -= DeviceItem_DragStarting;
        container.DragStarting += DeviceItem_DragStarting;
        ToolTipService.SetToolTip(container, args.Item is TopologyDeviceItem item ? item.DetailTip : null);
    }

    private static void DeviceList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.OfType<TopologyDeviceItem>().FirstOrDefault() is not { } item)
        {
            e.Cancel = true;
            return;
        }

        ApplyDeviceDragData(e.Data, item.Device.Id);
        if (item.IsPlaced)
        {
            e.Data.RequestedOperation = DataPackageOperation.None;
        }
    }

    private void DeviceList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = ResolveDeviceItemFromSource(e.OriginalSource) ?? ViewModel.SelectedDevice;
        if (item is null)
        {
            return;
        }

        e.Handled = true;
        var node = ViewModel.Document.Nodes.FirstOrDefault(n => n.IsDevice && n.DeviceId == item.Device.Id);
        if (node is null)
        {
            ViewModel.Status = "该设备尚未放到画布，可拖到画布上。";
            return;
        }

        LocateCanvasNode(node, $"已定位到「{item.Title}」。");
    }

    private static TopologyDeviceItem? ResolveDeviceItemFromSource(object source)
    {
        for (DependencyObject? current = source as DependencyObject;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: TopologyDeviceItem item })
            {
                return item;
            }

            if (current is ListViewItem listItem)
            {
                return ResolveDeviceItem(listItem);
            }
        }

        return null;
    }

    private void LocateCanvasNode(TopologyNodeDto node, string status)
    {
        if (!_nodeVisuals.ContainsKey(node.Id))
        {
            RebuildCanvas();
        }

        SelectNode(node.Id, additive: false);
        ApplySelection();
        var zoom = BoardHost.ZoomFactor;
        if (zoom <= 0 || !TopologyMetrics.IsValid(zoom))
        {
            zoom = 1;
        }

        var cx = TopologyMetrics.ClampX(node.X) + TopologyMetrics.WidthOf(node) / 2;
        var cy = TopologyMetrics.ClampY(node.Y) + TopologyMetrics.HeightOf(node) / 2;
        BoardHost.ChangeView(
            cx * zoom - BoardHost.ActualWidth / 2,
            cy * zoom - BoardHost.ActualHeight / 2,
            zoom);
        ViewModel.Status = status;
    }

    private static void DeviceItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var item = ResolveDeviceItem(sender);
        if (item is null)
        {
            return;
        }

        args.AllowedOperations = item.IsPlaced ? DataPackageOperation.None : DataPackageOperation.Copy;
        ApplyDeviceDragData(args.Data, item.Device.Id);
    }

    private static TopologyDeviceItem? ResolveDeviceItem(UIElement sender) => sender switch
    {
        ListViewItem { Tag: TopologyDeviceItem tagged } => tagged,
        ListViewItem { Content: TopologyDeviceItem content } => content,
        FrameworkElement { DataContext: TopologyDeviceItem context } => context,
        _ => null
    };

    private static void ApplyDeviceDragData(DataPackage data, int deviceId)
    {
        var token = DeviceDragPrefix + deviceId.ToString(CultureInfo.InvariantCulture);
        data.RequestedOperation = DataPackageOperation.Copy;
        data.Properties.Title = DeviceDragFormat;
        data.Properties.Description = token;
        data.SetText(token);
        try
        {
            data.SetData(DeviceDragFormat, token);
        }
        catch
        {
            // 未打包应用里自定义剪贴板格式可能不可用，Text / Description 足够。
        }
    }

    private static bool IsDeviceDrag(DataPackageView view) =>
        TryParseDeviceToken(view.Properties.Description, out _)
        || TryParseDeviceToken(view.Properties.Title, out _)
        || string.Equals(view.Properties.Title, DeviceDragFormat, StringComparison.Ordinal)
        || view.Contains(DeviceDragFormat);

    private static bool TryParseDeviceToken(string? text, out int deviceId)
    {
        deviceId = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith(DeviceDragPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[DeviceDragPrefix.Length..];
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out deviceId);
    }

    private static async Task<int?> ReadDeviceIdAsync(DataPackageView view)
    {
        if (TryParseDeviceToken(view.Properties.Description, out var fromDescription))
        {
            return fromDescription;
        }

        if (TryParseDeviceToken(view.Properties.Title, out var fromTitle))
        {
            return fromTitle;
        }

        if (view.Contains(DeviceDragFormat))
        {
            try
            {
                var raw = await view.GetDataAsync(DeviceDragFormat);
                if (TryParseDeviceToken(raw as string ?? raw?.ToString(), out var fromFormat))
                {
                    return fromFormat;
                }
            }
            catch
            {
                // 自定义格式读取失败时改走 Text。
            }
        }

        if (view.Contains(StandardDataFormats.Text))
        {
            try
            {
                var text = await view.GetTextAsync();
                if (TryParseDeviceToken(text, out var fromText))
                {
                    return fromText;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private void Board_DragOver(object sender, DragEventArgs e)
    {
        if (!IsDeviceDrag(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = string.Empty;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            e.DragUIOverride.IsContentVisible = false;
            return;
        }

        if (TryGetDraggedDeviceId(e.DataView, out var deviceId)
            && ViewModel.Document.Nodes.Any(n => n.IsDevice && n.DeviceId == deviceId))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = "已在画布";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
            e.DragUIOverride.IsContentVisible = false;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "放到画布";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = true;
    }

    private static bool TryGetDraggedDeviceId(DataPackageView view, out int deviceId) =>
        TryParseDeviceToken(view.Properties.Description, out deviceId)
        || TryParseDeviceToken(view.Properties.Title, out deviceId);

    private static void SuppressSystemDrag(UIElement element)
    {
        element.CanDrag = false;
        element.ManipulationMode = ManipulationModes.None;
        element.DragStarting -= Node_DragStarting;
        element.DragStarting += Node_DragStarting;
    }

    private static void Node_DragStarting(UIElement sender, DragStartingEventArgs args) => args.Cancel = true;

    private void EnableDeviceDrop(UIElement element)
    {
        element.AllowDrop = true;
        element.DragOver -= Board_DragOver;
        element.DragOver += Board_DragOver;
        element.Drop -= Board_Drop;
        element.Drop += Board_Drop;
    }

    private async void Board_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var view = e.DataView;
            if (!IsDeviceDrag(view) && !view.Contains(StandardDataFormats.Text) && !view.Contains(DeviceDragFormat))
            {
                return;
            }

            var deviceId = await ReadDeviceIdAsync(view);
            if (deviceId is not int id)
            {
                return;
            }

            var pos = e.GetPosition(Board);
            if (!TopologyMetrics.IsValid(pos.X) || !TopologyMetrics.IsValid(pos.Y))
            {
                pos = new Point(48, 48);
            }

            var already = ViewModel.Document.Nodes.FirstOrDefault(n => n.IsDevice && n.DeviceId == id);
            if (already is not null)
            {
                LocateCanvasNode(already, "该设备已在画布上。");
                return;
            }

            var node = ViewModel.PlaceDeviceAt(id, pos.X - NodeWidth / 2, pos.Y - NodeHeight / 2);
            if (node is null)
            {
                return;
            }

            ViewModel.AssignContainersByPosition([node.Id]);
            SelectNode(node.Id, additive: false);
            _pendingFromId = null;
            RebuildCanvas();
        }
        catch (Exception ex)
        {
            ViewModel.Status = ex.Message;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void AddIspPreset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEnvironment is null)
        {
            ViewModel.Status = "请先选择网络环境。";
            return;
        }

        var key = (sender as MenuFlyoutItem)?.Tag as string;
        PlaceIsp(IspPresets.DefaultLabel(key), key);
    }

    private async void AddIspCustom_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEnvironment is null)
        {
            ViewModel.Status = "请先选择网络环境。";
            return;
        }

        var label = await PromptDialogs.PromptIspCustomLabelAsync(XamlRoot);
        if (label is null)
        {
            return;
        }

        PlaceIsp(label, IspPresets.Custom);
    }

    private async void AddContainer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEnvironment is null)
        {
            ViewModel.Status = "请先选择网络环境。";
            return;
        }

        var name = await PromptDialogs.PromptContainerNameAsync(XamlRoot);
        if (name is null)
        {
            return;
        }

        var members = _selectedNodeIds
            .Where(id => ViewModel.FindNode(id) is { IsContainer: false })
            .ToList();
        var node = ViewModel.AddContainer(name, members);
        SelectNode(node.Id, additive: false);
        RebuildCanvas();
    }

    private void PlaceIsp(string label, string? ispKey)
    {
        var node = ViewModel.AddIsp(label, ispKey);
        SelectNode(node.Id, additive: false);
        RebuildCanvas();
    }

    private void UndoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTypingTarget())
        {
            return;
        }

        args.Handled = true;
        ViewModel.Undo();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => ViewModel.Undo();

    private void ToggleGrid_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowGrid = ToggleGrid.IsChecked == true;
        RefreshGrid();
    }

    private void ToggleSnap_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SnapToGrid = ToggleSnap.IsChecked == true;

    private void CenterView_Click(object sender, RoutedEventArgs e)
    {
        CenterOnContent(fit: false, updateStatus: true);
    }

    private void ArrangeVertical_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ArrangeLayers(leftToRight: false);

    private void ArrangeHorizontal_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ArrangeLayers(leftToRight: true);

    private void QueueCenterOnContent(bool fit)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            BoardHost.UpdateLayout();
            if (!CenterOnContent(fit, updateStatus: false))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    BoardHost.UpdateLayout();
                    CenterOnContent(fit, updateStatus: false);
                });
            }
        });
    }

    private bool CenterOnContent(bool fit, bool updateStatus)
    {
        var nodes = ViewModel.Document.Nodes;
        double minX;
        double minY;
        double maxX;
        double maxY;
        if (nodes.Count == 0)
        {
            minX = 0;
            minY = 0;
            maxX = Board.Width;
            maxY = Board.Height;
        }
        else
        {
            minX = nodes.Min(n => TopologyMetrics.ClampX(n.X));
            minY = nodes.Min(n => TopologyMetrics.ClampY(n.Y));
            maxX = nodes.Max(n => TopologyMetrics.ClampX(n.X) + TopologyMetrics.WidthOf(n));
            maxY = nodes.Max(n => TopologyMetrics.ClampY(n.Y) + TopologyMetrics.HeightOf(n));
        }

        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        double zoom = BoardHost.ZoomFactor;
        if (zoom <= 0 || !TopologyMetrics.IsValid(zoom))
        {
            zoom = 1;
        }

        var viewW = BoardHost.ActualWidth;
        var viewH = BoardHost.ActualHeight;
        if (viewW <= 0 || viewH <= 0)
        {
            return false;
        }

        if (fit && nodes.Count > 0)
        {
            var boundW = Math.Max(NodeWidth, maxX - minX + 96);
            var boundH = Math.Max(NodeHeight, maxY - minY + 96);
            zoom = Math.Clamp(
                Math.Min(viewW / boundW, viewH / boundH),
                BoardHost.MinZoomFactor,
                BoardHost.MaxZoomFactor);
        }

        var moved = BoardHost.ChangeView(
            cx * zoom - viewW / 2,
            cy * zoom - viewH / 2,
            (float)zoom,
            disableAnimation: true);
        if (updateStatus)
        {
            ViewModel.Status = nodes.Count == 0 ? "已定位到画布中心。" : "已定位到拓扑中心。";
        }

        return moved;
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTypingTarget())
        {
            return;
        }

        if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            DeleteSelection();
            return;
        }

        if (e.Key != VirtualKey.Space)
        {
            return;
        }

        _spaceDown = true;
        UpdateCanvasCursor();
        e.Handled = true;
    }

    private void Page_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space)
        {
            return;
        }

        _spaceDown = false;
        if (!_panning)
        {
            UpdateCanvasCursor();
        }
    }

    private bool IsTypingTarget() =>
        FocusManager.GetFocusedElement(XamlRoot) is TextBox or ComboBox or AutoSuggestBox;

    private void UpdateCanvasCursor()
    {
        ProtectedCursor = InputSystemCursor.Create(
            _spaceDown || _panning ? InputSystemCursorShape.SizeAll : InputSystemCursorShape.Arrow);
    }

    private void DeleteSelection()
    {
        var nodeIds = _selectedNodeIds.ToList();
        var links = _selectedLinkIds.ToList();
        if (nodeIds.Count == 0 && links.Count == 0)
        {
            ViewModel.Status = "请先点选画布上的设备或连线，Ctrl 可多选，再按 Delete 删除。";
            return;
        }

        if (_pendingFromId is int pending && nodeIds.Contains(pending))
        {
            _pendingFromId = null;
        }

        ViewModel.DeleteSelection(nodeIds, links);

        var parts = new List<string>();
        if (nodeIds.Count > 0)
        {
            parts.Add($"{nodeIds.Count} 个节点");
        }

        if (links.Count > 0)
        {
            parts.Add($"{links.Count} 条连线");
        }

        ClearSelection();
        RebuildCanvas();
        ViewModel.Status = $"已删除 {string.Join("、", parts)}。按 Delete 删除选中项，Ctrl 点选可多选。";
    }

    private void ClearSelection()
    {
        _selectedNodeId = null;
        _selectedLinkId = null;
        _selectedNodeIds.Clear();
        _selectedLinkIds.Clear();
    }

    private void SelectNode(int nodeId, bool additive)
    {
        if (!additive)
        {
            _selectedNodeIds.Clear();
            _selectedLinkIds.Clear();
            _selectedNodeIds.Add(nodeId);
            _selectedNodeId = nodeId;
            _selectedLinkId = null;
            return;
        }

        if (!_selectedNodeIds.Add(nodeId))
        {
            _selectedNodeIds.Remove(nodeId);
            _selectedNodeId = _selectedNodeIds.Count == 0 ? null : _selectedNodeIds.First();
            return;
        }

        _selectedNodeId = nodeId;
        _selectedLinkId = null;
    }

    private void SelectLink(int linkId, bool additive)
    {
        if (!additive)
        {
            _selectedNodeIds.Clear();
            _selectedLinkIds.Clear();
            _selectedLinkIds.Add(linkId);
            _selectedLinkId = linkId;
            _selectedNodeId = null;
            return;
        }

        if (!_selectedLinkIds.Add(linkId))
        {
            _selectedLinkIds.Remove(linkId);
            _selectedLinkId = _selectedLinkIds.Count == 0 ? null : _selectedLinkIds.First();
            return;
        }

        _selectedLinkId = linkId;
        _selectedNodeId = null;
    }

    private static bool IsCtrlDown(PointerRoutedEventArgs e) =>
        e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);

    private async void PickColor_Click(object sender, RoutedEventArgs e)
    {
        var current = ParseColor(ViewModel.CustomColorHex, Color.FromArgb(255, 124, 58, 237));
        var picked = await PromptDialogs.PromptColorAsync(XamlRoot, current);
        if (picked is null)
        {
            return;
        }

        ViewModel.CustomColorHex = $"#{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveAsync();
        }
        catch (Exception ex)
        {
            ViewModel.Status = ex.Message;
        }
    }

    private async void CloudSync_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncCloudAsync();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var name = ViewModel.SelectedEnvironment?.Name ?? "topology";
        var path = await FileDialogs.PickSavePngAsync($"拓扑-{name}-{DateTime.Now:yyyyMMdd-HHmm}");
        if (path is null)
        {
            return;
        }

        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(Board);
            var pixels = await rtb.GetPixelsAsync();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            stream.Size = 0;
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth,
                (uint)rtb.PixelHeight,
                96,
                96,
                pixels.ToArray());
            await encoder.FlushAsync();
            ViewModel.Status = "已导出拓扑图片。";
        }
        catch (Exception ex)
        {
            ViewModel.Status = ex.Message;
        }
    }

    private void Board_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Board))
        {
            return;
        }

        Focus(FocusState.Programmatic);
        _pendingFromId = null;
        if (_spaceDown)
        {
            StartPan(Board, e);
            e.Handled = true;
            return;
        }

        if (ViewModel.IsLinkTool)
        {
            if (!IsCtrlDown(e))
            {
                ClearSelection();
            }

            ApplySelection();
            return;
        }

        var pos = e.GetCurrentPoint(Board).Position;
        _marqueeing = true;
        _marqueeStart = pos;
        _marqueeAdditive = IsCtrlDown(e);
        Board.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Board_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panning)
        {
            ContinuePan(e);
            return;
        }

        if (!e.Pointer.IsInContact)
        {
            return;
        }

        var pos = e.GetCurrentPoint(Board).Position;
        if (_dragging)
        {
            ContinueNodeDrag(e);
            e.Handled = true;
            return;
        }

        if (_curvePending)
        {
            ContinueCurveDrag(pos);
            e.Handled = true;
            return;
        }

        if (!_marqueeing)
        {
            return;
        }

        EnsureMarquee();
        UpdateMarqueeRect(_marqueeStart, pos);
        e.Handled = true;
    }

    private void Board_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panning && ReferenceEquals(_panCapture, Board))
        {
            EndPan(e);
            return;
        }

        if (_dragging)
        {
            EndNodeDrag(e);
            return;
        }

        if (_curvePending)
        {
            EndCurveDrag(e);
            return;
        }

        FinishMarquee(e);
    }

    private void StartPan(UIElement capture, PointerRoutedEventArgs e)
    {
        CancelMarquee();
        _panning = true;
        _panCapture = capture;
        _panPointerStart = e.GetCurrentPoint(BoardHost).Position;
        _panScrollX = BoardHost.HorizontalOffset;
        _panScrollY = BoardHost.VerticalOffset;
        capture.CapturePointer(e.Pointer);
        UpdateCanvasCursor();
    }

    private void ContinuePan(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(BoardHost).Position;
        BoardHost.ChangeView(
            _panScrollX - (point.X - _panPointerStart.X),
            _panScrollY - (point.Y - _panPointerStart.Y),
            BoardHost.ZoomFactor);
    }

    private void EndPan(PointerRoutedEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        try
        {
            _panCapture?.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // 指针已释放时忽略。
        }

        _panCapture = null;
        UpdateCanvasCursor();
    }

    private void FinishMarquee(PointerRoutedEventArgs e)
    {
        if (!_marqueeing)
        {
            return;
        }

        _marqueeing = false;
        try
        {
            Board.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // ignore
        }

        var pos = e.GetCurrentPoint(Board).Position;
        var rect = NormalizeRect(_marqueeStart, pos);
        RemoveMarquee();
        if (rect.Width < 4 && rect.Height < 4)
        {
            if (!_marqueeAdditive)
            {
                ClearSelection();
                ApplySelection();
            }

            return;
        }

        SelectNodesInRect(rect, _marqueeAdditive);
        ApplySelection();
        ViewModel.Status = _selectedNodeIds.Count == 0
            ? "框选范围内没有节点。"
            : $"已框选 {_selectedNodeIds.Count} 个节点，拖动其中任意节点可批量移动。";
    }

    private void SelectNodesInRect(Rect rect, bool additive)
    {
        if (!additive)
        {
            _selectedNodeIds.Clear();
            _selectedLinkIds.Clear();
            _selectedLinkId = null;
        }

        foreach (var (id, border) in _nodeVisuals)
        {
            var nodeRect = new Rect(
                Canvas.GetLeft(border),
                Canvas.GetTop(border),
                border.Width > 0 ? border.Width : NodeWidth,
                border.Height > 0 ? border.Height : NodeHeight);
            if (RectsIntersect(rect, nodeRect))
            {
                _selectedNodeIds.Add(id);
            }
        }

        _selectedNodeId = _selectedNodeIds.Count == 0 ? null : _selectedNodeIds.First();
    }

    private void EnsureMarquee()
    {
        if (_marquee is not null)
        {
            return;
        }

        _marquee = new Rectangle
        {
            Stroke = SelectedNodeBorder,
            StrokeThickness = 1,
            StrokeDashArray = [4, 2],
            Fill = MarqueeFill,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_marquee, 20);
        Board.Children.Add(_marquee);
    }

    private void UpdateMarqueeRect(Point a, Point b)
    {
        if (_marquee is null)
        {
            return;
        }

        var rect = NormalizeRect(a, b);
        Canvas.SetLeft(_marquee, rect.X);
        Canvas.SetTop(_marquee, rect.Y);
        _marquee.Width = Math.Max(rect.Width, 1);
        _marquee.Height = Math.Max(rect.Height, 1);
    }

    private void RemoveMarquee()
    {
        if (_marquee is null)
        {
            return;
        }

        Board.Children.Remove(_marquee);
        _marquee = null;
    }

    private void CancelMarquee()
    {
        _marqueeing = false;
        RemoveMarquee();
    }

    private static Rect NormalizeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private static bool RectsIntersect(Rect a, Rect b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private void RebuildCanvas()
    {
        Board.Children.Clear();
        _nodeVisuals.Clear();
        _containerCaptions.Clear();
        _linkVisuals.Clear();
        _gridVisual = null;
        _marquee = null;

        SyncCanvasSize();
        RefreshGrid();

        foreach (var link in ViewModel.Document.Links)
        {
            try
            {
                AddLinkVisual(link);
            }
            catch (Exception ex)
            {
                ViewModel.Status = $"连线 #{link.Id} 绘制失败：{ex.Message}";
            }
        }

        foreach (var node in ViewModel.Document.Nodes.OrderBy(n => n.IsContainer ? 0 : 1))
        {
            try
            {
                AddNodeVisual(node);
            }
            catch (Exception ex)
            {
                AddFallbackNodeVisual(node);
                ViewModel.Status = $"节点 #{node.Id} 绘制失败：{ex.Message}";
            }
        }

        foreach (var link in _linkVisuals.Values)
        {
            try
            {
                UpdateLinkGeometry(link);
            }
            catch
            {
                // 单条连线几何失败不应让整张画布空白。
            }
        }

        ApplySelection();
    }

    private void RefreshGrid()
    {
        if (_gridVisual is not null)
        {
            Board.Children.Remove(_gridVisual);
            _gridVisual = null;
        }

        if (!ViewModel.ShowGrid)
        {
            return;
        }

        _gridVisual = CreateGridVisual();
        Canvas.SetZIndex(_gridVisual, 0);
        Board.Children.Insert(0, _gridVisual);
    }

    private UIElement CreateGridVisual()
    {
        var layer = new Canvas
        {
            Width = TopologyMetrics.CanvasWidth,
            Height = TopologyMetrics.CanvasHeight,
            IsHitTestVisible = false
        };
        var width = TopologyMetrics.CanvasWidth;
        var height = TopologyMetrics.CanvasHeight;
        var step = TopologyMetrics.GridSize;
        for (double x = 0; x <= width; x += step)
        {
            layer.Children.Add(new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height,
                Stroke = GridStroke,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }

        for (double y = 0; y <= height; y += step)
        {
            layer.Children.Add(new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = width,
                Y2 = y,
                Stroke = GridStroke,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }

        return layer;
    }

    private void SyncCanvasSize()
    {
        TopologyMetrics.CopyTo(ViewModel.Document);
        var width = TopologyMetrics.CanvasWidth;
        var height = TopologyMetrics.CanvasHeight;
        if (Math.Abs(Board.Width - width) < 0.5 && Math.Abs(Board.Height - height) < 0.5)
        {
            return;
        }

        Board.Width = width;
        Board.Height = height;
        RefreshGrid();
    }

    private void StartEdgePan()
    {
        _edgePanTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _edgePanTimer.Tick -= EdgePan_Tick;
        _edgePanTimer.Tick += EdgePan_Tick;
        _edgePanTimer.Start();
    }

    private void StopEdgePan()
    {
        _edgePanTimer?.Stop();
        _hasPointerHost = false;
    }

    private void EdgePan_Tick(object? sender, object e)
    {
        if (!_dragging && !_resizing)
        {
            StopEdgePan();
            return;
        }

        if (_dragging)
        {
            ContinueNodeDrag(null);
            return;
        }

        ContinueContainerResize(null, snap: false);
    }

    private void RememberPointer(PointerRoutedEventArgs e)
    {
        _lastPointerHost = e.GetCurrentPoint(BoardHost).Position;
        _hasPointerHost = true;
    }

    private Point PointerOnBoard(PointerRoutedEventArgs? e)
    {
        if (e is not null)
        {
            return e.GetCurrentPoint(Board).Position;
        }

        if (!_hasPointerHost)
        {
            return new Point();
        }

        try
        {
            return BoardHost.TransformToVisual(Board).TransformPoint(_lastPointerHost);
        }
        catch
        {
            var zoom = BoardHost.ZoomFactor;
            if (zoom <= 0 || !TopologyMetrics.IsValid(zoom))
            {
                zoom = 1;
            }

            return new Point(
                (BoardHost.HorizontalOffset + _lastPointerHost.X) / zoom,
                (BoardHost.VerticalOffset + _lastPointerHost.Y) / zoom);
        }
    }

    private void TryEdgePan()
    {
        if (!_hasPointerHost)
        {
            return;
        }

        var viewW = BoardHost.ActualWidth;
        var viewH = BoardHost.ActualHeight;
        if (viewW <= 0 || viewH <= 0)
        {
            return;
        }

        const double band = 64;
        var p = _lastPointerHost;
        var dx = 0.0;
        var dy = 0.0;
        if (p.X < band)
        {
            dx = -(band - p.X) * 0.28;
        }
        else if (p.X > viewW - band)
        {
            dx = (p.X - (viewW - band)) * 0.28;
        }

        if (p.Y < band)
        {
            dy = -(band - p.Y) * 0.28;
        }
        else if (p.Y > viewH - band)
        {
            dy = (p.Y - (viewH - band)) * 0.28;
        }

        if (dx == 0 && dy == 0)
        {
            return;
        }

        var zoom = BoardHost.ZoomFactor;
        if (zoom <= 0 || !TopologyMetrics.IsValid(zoom))
        {
            zoom = 1;
        }

        TopologyMetrics.GrowToFit(
            (BoardHost.HorizontalOffset + viewW) / zoom + 8,
            (BoardHost.VerticalOffset + viewH) / zoom + 8);
        SyncCanvasSize();
        BoardHost.ChangeView(
            Math.Max(0, BoardHost.HorizontalOffset + dx),
            Math.Max(0, BoardHost.VerticalOffset + dy),
            zoom,
            disableAnimation: true);
    }

    private static UIElement CreateNodeIcon(ImageSource? source, string fallbackGlyph)
    {
        if (source is null)
        {
            return new FontIcon
            {
                Glyph = fallbackGlyph,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false,
                CanDrag = false
            };
        }

        return new Border
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            CanDrag = false,
            Background = new ImageBrush
            {
                ImageSource = source,
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            }
        };
    }

    private static TextBlock CreateNodeText(string text, double fontSize, double opacity, bool bold = false) => new()
    {
        Text = text,
        FontSize = fontSize,
        Opacity = opacity,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
        CanDrag = false,
        IsTextSelectionEnabled = false
    };

    private readonly record struct NodeVisualContent(
        string Title,
        string Subtitle,
        string? TypeName,
        string? Description,
        UIElement Icon);

    private NodeVisualContent ResolveNodeVisualContent(TopologyNodeDto node)
    {
        if (node.IsIsp)
        {
            var title = string.IsNullOrWhiteSpace(node.Label) ? IspPresets.DefaultLabel(node.IspKey) : node.Label;
            var icon = CreateNodeIcon(DeviceTypeIconXaml.Isp(node.IspKey, node.Label), "\uE128");
            return new NodeVisualContent(title, "ISP", "互联网", null, icon);
        }

        var device = ViewModel.FindDevice(node.DeviceId);
        if (device is null)
        {
            var missingTitle = string.IsNullOrWhiteSpace(node.Label) ? $"设备 #{node.DeviceId}" : node.Label;
            return new NodeVisualContent(
                missingTitle,
                "未在当前环境找到",
                "—",
                "—",
                CreateNodeIcon(null, "\uE770"));
        }

        var displayTitle = string.IsNullOrWhiteSpace(device.Hostname) ? device.Code : device.Hostname;
        var ips = string.Join(" / ", device.NicIps());
        var subtitle = string.IsNullOrWhiteSpace(ips) ? device.Code : ips;
        var description = string.IsNullOrWhiteSpace(device.Description) ? "—" : device.Description.Trim();
        return new NodeVisualContent(
            displayTitle,
            subtitle,
            device.TypeName,
            description,
            CreateNodeIcon(DeviceTypeIconXaml.FromType(device.DeviceType), "\uE770"));
    }

    private void AddNodeVisual(TopologyNodeDto node)
    {
        if (node.IsContainer)
        {
            AddContainerVisual(node);
            return;
        }

        var content = ResolveNodeVisualContent(node);
        var title = content.Title;
        var subtitle = content.Subtitle;
        var typeName = content.TypeName;
        var description = content.Description;
        var icon = content.Icon;

        var stack = new StackPanel { Spacing = 1, IsHitTestVisible = false, CanDrag = false };
        stack.Children.Add(icon);
        stack.Children.Add(CreateNodeText(title, 12, 1, bold: true));
        stack.Children.Add(CreateNodeText(subtitle, 11, 0.7));
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            stack.Children.Add(CreateNodeText(typeName, 10, 0.75));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            stack.Children.Add(CreateNodeText(description, 10, 0.6));
        }

        var tip = typeName is null
            ? $"{title}\n{subtitle}"
            : $"{title}\n{subtitle}\n类型：{typeName}\n说明：{description ?? "—"}";

        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Tag = node.Id,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1.5),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Child = stack
        };
        ToolTipService.SetToolTip(border, tip);
        border.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Canvas.SetLeft(border, TopologyMetrics.ClampX(node.X));
        Canvas.SetTop(border, TopologyMetrics.ClampY(node.Y));
        Canvas.SetZIndex(border, 10);
        border.PointerPressed += Node_PointerPressed;
        border.PointerMoved += Node_PointerMoved;
        border.PointerReleased += Node_PointerReleased;
        border.PointerCanceled += Node_PointerReleased;
        border.DoubleTapped += Node_DoubleTapped;
        SuppressSystemDrag(border);
        EnableDeviceDrop(border);

        Board.Children.Add(border);
        _nodeVisuals[node.Id] = border;
    }

    private void AddContainerVisual(TopologyNodeDto node)
    {
        var title = string.IsNullOrWhiteSpace(node.Label) ? "容器" : node.Label.Trim();
        var root = new Grid { IsHitTestVisible = true, CanDrag = false };
        AddResizeGrip(root, node.Id, ResizeEdges.Left | ResizeEdges.Top, HorizontalAlignment.Left, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthwestSoutheast);
        AddResizeGrip(root, node.Id, ResizeEdges.Right | ResizeEdges.Top, HorizontalAlignment.Right, VerticalAlignment.Top, InputSystemCursorShape.SizeNortheastSouthwest);
        AddResizeGrip(root, node.Id, ResizeEdges.Left | ResizeEdges.Bottom, HorizontalAlignment.Left, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNortheastSouthwest);
        AddResizeGrip(root, node.Id, ResizeEdges.Right | ResizeEdges.Bottom, HorizontalAlignment.Right, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthwestSoutheast);

        var border = new Border
        {
            Width = TopologyMetrics.WidthOf(node),
            Height = TopologyMetrics.HeightOf(node),
            Tag = node.Id,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Background = ContainerFill,
            BorderBrush = ContainerStroke,
            Child = root
        };
        ToolTipService.SetToolTip(border, $"{title}\n拖动边角或边缘可调整大小；节点靠近边缘会自动撑开。点空白处可为容器连线。");
        border.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Canvas.SetLeft(border, TopologyMetrics.ClampX(node.X));
        Canvas.SetTop(border, TopologyMetrics.ClampY(node.Y));
        Canvas.SetZIndex(border, 5);
        border.PointerPressed += Node_PointerPressed;
        border.PointerMoved += Node_PointerMoved;
        border.PointerReleased += Node_PointerReleased;
        border.PointerCanceled += Node_PointerReleased;
        border.DoubleTapped += Node_DoubleTapped;
        SuppressSystemDrag(border);
        EnableDeviceDrop(border);
        Board.Children.Add(border);
        _nodeVisuals[node.Id] = border;

        var caption = new TextBlock
        {
            Text = title,
            Tag = node.Id,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Width = TopologyMetrics.WidthOf(node),
            Height = TopologyMetrics.ContainerCaptionHeight,
            IsTextSelectionEnabled = false,
            CanDrag = false
        };
        ToolTipService.SetToolTip(caption, title);
        caption.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Canvas.SetLeft(caption, TopologyMetrics.ClampX(node.X));
        Canvas.SetTop(caption, TopologyMetrics.ClampY(node.Y) + TopologyMetrics.HeightOf(node) + TopologyMetrics.ContainerCaptionGap);
        Canvas.SetZIndex(caption, 6);
        caption.PointerPressed += Node_PointerPressed;
        caption.PointerMoved += Node_PointerMoved;
        caption.PointerReleased += Node_PointerReleased;
        caption.PointerCanceled += Node_PointerReleased;
        caption.DoubleTapped += Node_DoubleTapped;
        SuppressSystemDrag(caption);
        Board.Children.Add(caption);
        _containerCaptions[node.Id] = caption;
    }

    private void AddResizeGrip(
        Grid root,
        int nodeId,
        ResizeEdges edges,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        InputSystemCursorShape cursor)
    {
        var grip = new Border
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(-5),
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Colors.White),
            BorderBrush = ContainerStroke,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Tag = new ContainerResizeTag(nodeId, edges, cursor),
            CanDrag = false
        };
        Grid.SetRowSpan(grip, 1);
        WireResizeHandle(grip, cursor);
        root.Children.Add(grip);
    }

    private void WireResizeHandle(Border handle, InputSystemCursorShape cursor)
    {
        handle.PointerEntered += (_, _) =>
        {
            if (!_dragging && !_panning)
            {
                ProtectedCursor = InputSystemCursor.Create(cursor);
            }
        };
        handle.PointerExited += (_, _) =>
        {
            if (!_resizing)
            {
                UpdateCanvasCursor();
            }
        };
        handle.PointerPressed += ContainerResize_PointerPressed;
        handle.PointerMoved += ContainerResize_PointerMoved;
        handle.PointerReleased += ContainerResize_PointerReleased;
        handle.PointerCanceled += ContainerResize_PointerReleased;
    }

    private void ContainerResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsLinkTool
            || _spaceDown
            || sender is not Border handle
            || handle.Tag is not ContainerResizeTag tag)
        {
            return;
        }

        var node = ViewModel.FindNode(tag.NodeId);
        if (node is not { IsContainer: true })
        {
            return;
        }

        e.Handled = true;
        Focus(FocusState.Programmatic);
        SelectNode(tag.NodeId, additive: false);
        ApplySelection();
        ViewModel.PushUndo();
        _dragUndoPushed = true;
        _resizing = true;
        _resizeNodeId = tag.NodeId;
        _resizeEdges = tag.Edges;
        _resizeStart = new Rect(node.X, node.Y, TopologyMetrics.WidthOf(node), TopologyMetrics.HeightOf(node));
        _resizePointerStart = e.GetCurrentPoint(Board).Position;
        handle.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(tag.Cursor);
        RememberPointer(e);
        StartEdgePan();
    }

    private void ContainerResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing || !e.Pointer.IsInContact)
        {
            return;
        }

        e.Handled = true;
        ContinueContainerResize(e, snap: false);
    }

    private void ContainerResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        e.Handled = true;
        StopEdgePan();
        ContinueContainerResize(e, snap: ViewModel.SnapToGrid);
        _resizing = false;
        if (_dragUndoPushed)
        {
            ViewModel.DiscardUndoIfUnchanged();
            _dragUndoPushed = false;
        }

        try
        {
            if (sender is UIElement element)
            {
                element.ReleasePointerCapture(e.Pointer);
            }
        }
        catch
        {
            // ignore
        }

        UpdateCanvasCursor();
    }

    private void ContinueContainerResize(PointerRoutedEventArgs? e, bool snap)
    {
        if (e is not null)
        {
            RememberPointer(e);
        }

        TryEdgePan();
        ApplyContainerResize(PointerOnBoard(e), snap);
        SyncCanvasSize();
    }

    private void ApplyContainerResize(Point pos, bool snap)
    {
        var x = _resizeStart.X;
        var y = _resizeStart.Y;
        var width = _resizeStart.Width;
        var height = _resizeStart.Height;
        var dx = pos.X - _resizePointerStart.X;
        var dy = pos.Y - _resizePointerStart.Y;
        if ((_resizeEdges & ResizeEdges.Left) != 0)
        {
            x += dx;
            width -= dx;
        }

        if ((_resizeEdges & ResizeEdges.Right) != 0)
        {
            width += dx;
        }

        if ((_resizeEdges & ResizeEdges.Top) != 0)
        {
            y += dy;
            height -= dy;
        }

        if ((_resizeEdges & ResizeEdges.Bottom) != 0)
        {
            height += dy;
        }

        if (snap)
        {
            x = TopologyMetrics.Snap(x);
            y = TopologyMetrics.Snap(y);
            width = TopologyMetrics.Snap(width);
            height = TopologyMetrics.Snap(height);
        }

        ViewModel.SetContainerBounds(_resizeNodeId, x, y, width, height, containChildren: true);
        UpdateNodePosition(_resizeNodeId);
    }

    private void AddFallbackNodeVisual(TopologyNodeDto node)
    {
        if (_nodeVisuals.ContainsKey(node.Id))
        {
            return;
        }

        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Tag = node.Id,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(1.5),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(node.Label) ? $"#{node.Id}" : node.Label,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
        Canvas.SetLeft(border, TopologyMetrics.ClampX(node.X));
        Canvas.SetTop(border, TopologyMetrics.ClampY(node.Y));
        Canvas.SetZIndex(border, 10);
        border.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        border.PointerPressed += Node_PointerPressed;
        border.PointerMoved += Node_PointerMoved;
        border.PointerReleased += Node_PointerReleased;
        border.PointerCanceled += Node_PointerReleased;
        SuppressSystemDrag(border);
        EnableDeviceDrop(border);
        Board.Children.Add(border);
        _nodeVisuals[node.Id] = border;
    }

    private void AddLinkVisual(TopologyLinkDto link)
    {
        var hex = TopologyLinkKinds.ResolveColor(link);
        var color = ParseColor(hex, Colors.Black);
        var dashed = TopologyLinkKinds.ResolveDashed(link);
        var stroke = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2.5,
            IsHitTestVisible = false,
            StrokeDashArray = dashed ? new DoubleCollection { 6, 4 } : null
        };
        var hit = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = HitBrush,
            StrokeThickness = 16,
            Tag = link.Id
        };
        var handle = new Ellipse
        {
            Width = 10,
            Height = 10,
            Tag = link.Id,
            Fill = new SolidColorBrush(Colors.White),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            Opacity = 0.45,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(hit, 8);
        Canvas.SetZIndex(stroke, 7);
        Canvas.SetZIndex(handle, 15);
        WireLinkInput(hit);
        WireLinkInput(handle);
        EnableDeviceDrop(hit);

        Board.Children.Add(hit);
        Board.Children.Add(stroke);
        Board.Children.Add(handle);
        Border? caption = null;
        if (!string.IsNullOrWhiteSpace(link.Label))
        {
            caption = new Border
            {
                Background = LabelBackground,
                Padding = new Thickness(6, 1, 6, 1),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = link.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Black),
                    IsTextSelectionEnabled = false
                }
            };
            Canvas.SetZIndex(caption, 12);
            Board.Children.Add(caption);
        }

        _linkVisuals[link.Id] = new LinkVisual(link.Id, link.FromNodeId, link.ToNodeId, stroke, hit, handle, caption);
    }

    private void WireLinkInput(UIElement element)
    {
        element.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        element.PointerPressed += Link_PointerPressed;
        element.PointerMoved += Link_PointerMoved;
        element.PointerReleased += Link_PointerReleased;
        element.PointerCanceled += Link_PointerReleased;
        element.DoubleTapped += Link_DoubleTapped;
    }

    private void Node_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not int nodeId || _resizing)
        {
            return;
        }

        if (!_nodeVisuals.TryGetValue(nodeId, out var box))
        {
            return;
        }

        var node = ViewModel.FindNode(nodeId);
        if (node is { IsContainer: true }
            && !ViewModel.IsLinkTool
            && !_spaceDown
            && FindLinkUnderPointer(e) is int linkId)
        {
            HandleLinkPointerPressed(linkId, e, Board);
            return;
        }

        e.Handled = true;
        Focus(FocusState.Programmatic);
        if (_spaceDown)
        {
            StartPan(box, e);
            return;
        }

        var additive = !ViewModel.IsLinkTool && IsCtrlDown(e);
        if (node is { IsDevice: true })
        {
            ViewModel.SelectedDevice = ViewModel.Devices.FirstOrDefault(d => d.Device.Id == node.DeviceId);
        }

        if (ViewModel.IsLinkTool)
        {
            SelectNode(nodeId, additive: false);
            HandleConnectClick(nodeId);
            ApplySelection();
            return;
        }

        if (additive)
        {
            SelectNode(nodeId, true);
            ApplySelection();
            ViewModel.Status = $"已选中 {_selectedNodeIds.Count} 个节点、{_selectedLinkIds.Count} 条连线。按 Delete 删除。";
            return;
        }

        if (!_selectedNodeIds.Contains(nodeId))
        {
            SelectNode(nodeId, additive: false);
        }
        else
        {
            _selectedNodeId = nodeId;
            _selectedLinkId = null;
        }

        ApplySelection();
        var pos = e.GetCurrentPoint(Board).Position;
        ViewModel.PushUndo();
        _dragUndoPushed = true;
        _dragging = true;
        _dragNodeId = nodeId;
        _dragGrab = new Point(pos.X - Canvas.GetLeft(box), pos.Y - Canvas.GetTop(box));
        _dragStartPositions.Clear();
        foreach (var id in _selectedNodeIds)
        {
            var selected = ViewModel.FindNode(id);
            if (selected is not null)
            {
                _dragStartPositions[id] = new Point(selected.X, selected.Y);
            }
        }

        RaiseDragZIndex();
        Board.CapturePointer(e.Pointer);
        RememberPointer(e);
        StartEdgePan();
    }

    private async void Node_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.Tool != TopologyTool.Move || sender is not FrameworkElement { Tag: int nodeId })
        {
            return;
        }

        e.Handled = true;
        SelectNode(nodeId, additive: false);
        ApplySelection();
        await EditSelectionAsync();
    }

    private void HandleConnectClick(int nodeId)
    {
        if (_pendingFromId is null)
        {
            _pendingFromId = nodeId;
            ViewModel.Status = "已选择起点，再点另一个节点完成连线。";
            return;
        }

        if (_pendingFromId == nodeId)
        {
            _pendingFromId = null;
            ViewModel.Status = "已取消起点，请重新选择。";
            return;
        }

        ViewModel.Connect(_pendingFromId.Value, nodeId);
        _pendingFromId = null;
        RebuildCanvas();
    }

    private void Node_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panning)
        {
            ContinuePan(e);
            return;
        }

        if (!_dragging || !e.Pointer.IsInContact)
        {
            return;
        }

        ContinueNodeDrag(e);
    }

    private void Node_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panning && ReferenceEquals(_panCapture, sender))
        {
            EndPan(e);
            return;
        }

        EndNodeDrag(e);
    }

    private void ContinueNodeDrag(PointerRoutedEventArgs? e)
    {
        if (e is not null)
        {
            RememberPointer(e);
        }

        TryEdgePan();
        MoveDraggedNodes(PointerOnBoard(e));
        SyncCanvasSize();
    }

    private void MoveDraggedNodes(Point pos)
    {
        var rawX = pos.X - _dragGrab.X;
        var rawY = pos.Y - _dragGrab.Y;
        ViewModel.MoveNode(_dragNodeId, rawX, rawY, snap: false);
        var leader = ViewModel.FindNode(_dragNodeId);
        if (leader is null || !_dragStartPositions.TryGetValue(_dragNodeId, out var origin))
        {
            UpdateNodePosition(_dragNodeId);
            return;
        }

        var dx = leader.X - origin.X;
        var dy = leader.Y - origin.Y;
        var movedContainers = _dragStartPositions.Keys
            .Where(id => ViewModel.FindNode(id)?.IsContainer == true)
            .ToHashSet();
        foreach (var (id, start) in _dragStartPositions)
        {
            if (id == _dragNodeId)
            {
                continue;
            }

            var other = ViewModel.FindNode(id);
            if (other?.ContainerId is int parent && movedContainers.Contains(parent))
            {
                continue;
            }

            ViewModel.MoveNode(id, start.X + dx, start.Y + dy, snap: false);
            UpdateNodePosition(id);
        }

        UpdateNodePosition(_dragNodeId);
    }

    private void EndNodeDrag(PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        StopEdgePan();
        _dragging = false;
        SnapDraggedNodes();
        var moved = _dragStartPositions.Keys.ToList();
        RestoreDragZIndex();
        if (moved.Count > 0)
        {
            ViewModel.AssignContainersByPosition(moved);
            foreach (var node in ViewModel.Document.Nodes)
            {
                UpdateNodePosition(node.Id);
            }
        }
        if (_dragUndoPushed)
        {
            ViewModel.DiscardUndoIfUnchanged();
            _dragUndoPushed = false;
        }

        _dragStartPositions.Clear();
        try
        {
            Board.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // ignore
        }
    }

    private void RaiseDragZIndex()
    {
        foreach (var id in _dragStartPositions.Keys)
        {
            if (_nodeVisuals.TryGetValue(id, out var border))
            {
                Canvas.SetZIndex(border, 30);
            }

            if (_containerCaptions.TryGetValue(id, out var caption))
            {
                Canvas.SetZIndex(caption, 32);
            }

            foreach (var child in ViewModel.ChildrenOf(id))
            {
                if (_nodeVisuals.TryGetValue(child.Id, out var childBorder))
                {
                    Canvas.SetZIndex(childBorder, 31);
                }
            }
        }
    }

    private void RestoreDragZIndex()
    {
        foreach (var (id, border) in _nodeVisuals)
        {
            Canvas.SetZIndex(border, ViewModel.FindNode(id)?.IsContainer == true ? 5 : 10);
        }

        foreach (var caption in _containerCaptions.Values)
        {
            Canvas.SetZIndex(caption, 6);
        }
    }

    private void SnapDraggedNodes()
    {
        if (!ViewModel.SnapToGrid || _dragStartPositions.Count == 0)
        {
            return;
        }

        var leader = ViewModel.FindNode(_dragNodeId);
        if (leader is null)
        {
            return;
        }

        var beforeX = leader.X;
        var beforeY = leader.Y;
        ViewModel.MoveNode(_dragNodeId, leader.X, leader.Y, snap: true);
        var sdx = leader.X - beforeX;
        var sdy = leader.Y - beforeY;
        UpdateNodePosition(_dragNodeId);
        if (sdx == 0 && sdy == 0)
        {
            return;
        }

        var movedContainers = _dragStartPositions.Keys
            .Where(id => ViewModel.FindNode(id)?.IsContainer == true)
            .ToHashSet();
        foreach (var id in _dragStartPositions.Keys)
        {
            if (id == _dragNodeId)
            {
                continue;
            }

            var node = ViewModel.FindNode(id);
            if (node is null || (node.ContainerId is int parent && movedContainers.Contains(parent)))
            {
                continue;
            }

            ViewModel.MoveNode(id, node.X + sdx, node.Y + sdy, snap: false);
            UpdateNodePosition(id);
        }
    }

    private void Link_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (LinkIdOf(sender) is not int linkId)
        {
            return;
        }

        HandleLinkPointerPressed(linkId, e, sender as UIElement);
    }

    private void HandleLinkPointerPressed(int linkId, PointerRoutedEventArgs e, UIElement? capture)
    {
        e.Handled = true;
        Focus(FocusState.Programmatic);
        if (_spaceDown)
        {
            StartPan(capture ?? Board, e);
            return;
        }

        _pendingFromId = null;
        SelectLink(linkId, IsCtrlDown(e));
        ApplySelection();
        ViewModel.Status = _selectedLinkIds.Count + _selectedNodeIds.Count > 1
            ? $"已选中 {_selectedNodeIds.Count} 个节点、{_selectedLinkIds.Count} 条连线。按 Delete 删除。"
            : "已选中连线，可拖动调整弧度，或按 Delete 删除。";

        if (ViewModel.IsLinkTool || IsCtrlDown(e))
        {
            return;
        }

        if (!TryGetLinkAnchors(linkId, out _curveA, out _curveB))
        {
            return;
        }

        ViewModel.PushUndo();
        _curvePending = true;
        _curveDragging = false;
        _curveLinkId = linkId;
        _curvePress = e.GetCurrentPoint(Board).Position;
        Board.CapturePointer(e.Pointer);
    }

    private int? FindLinkUnderPointer(PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(Board).Position;
        foreach (var hit in VisualTreeHelper.FindElementsInHostCoordinates(pos, Board, true))
        {
            if (hit is FrameworkElement element
                && element.Tag is int id
                && _linkVisuals.TryGetValue(id, out var visual)
                && (ReferenceEquals(hit, visual.Hit) || ReferenceEquals(hit, visual.Handle)))
            {
                return id;
            }
        }

        return null;
    }

    private void Link_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panning)
        {
            ContinuePan(e);
            return;
        }

        if (!_curvePending || !e.Pointer.IsInContact)
        {
            return;
        }

        ContinueCurveDrag(e.GetCurrentPoint(Board).Position);
    }

    private void Link_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panning && ReferenceEquals(_panCapture, sender))
        {
            EndPan(e);
            return;
        }

        EndCurveDrag(e);
    }

    private void ContinueCurveDrag(Point pos)
    {
        if (!_curveDragging)
        {
            var dx = pos.X - _curvePress.X;
            var dy = pos.Y - _curvePress.Y;
            if (dx * dx + dy * dy < 25)
            {
                return;
            }

            _curveDragging = true;
        }

        ViewModel.SetLinkCurve(_curveLinkId, CurveFromPoint(_curveA, _curveB, pos));
        if (_linkVisuals.TryGetValue(_curveLinkId, out var visual))
        {
            UpdateLinkGeometry(visual);
        }
    }

    private void EndCurveDrag(PointerRoutedEventArgs e)
    {
        if (!_curvePending)
        {
            return;
        }

        if (_curveDragging)
        {
            ViewModel.Status = "已调整连线弧度。";
        }

        ViewModel.DiscardUndoIfUnchanged();
        _curvePending = false;
        _curveDragging = false;
        try
        {
            Board.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // ignore
        }
    }

    private async void Link_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LinkIdOf(sender) is not int linkId)
        {
            return;
        }

        e.Handled = true;
        _curvePending = false;
        _curveDragging = false;
        SelectLink(linkId, additive: false);
        ApplySelection();
        await EditSelectionAsync();
    }

    private static int? LinkIdOf(object sender) => sender is FrameworkElement { Tag: int id } ? id : null;

    private async Task EditSelectionAsync()
    {
        if (_selectedLinkId is int linkId)
        {
            await EditSelectedLinkAsync(linkId);
            return;
        }

        if (_selectedNodeId is int nodeId)
        {
            await EditSelectedNodeAsync(nodeId);
            return;
        }

        ViewModel.Status = "请先点选连线、容器或互联网节点。";
    }

    private async Task EditSelectedLinkAsync(int linkId)
    {
        var link = ViewModel.FindLink(linkId);
        if (link is null)
        {
            return;
        }

        var result = await PromptDialogs.PromptLinkEditAsync(XamlRoot, link);
        if (result is null)
        {
            return;
        }

        ViewModel.SetLinkAnnotation(linkId, result.Value.Label, result.Value.ColorHex, result.Value.Dashed);
        RebuildCanvas();
    }

    private async Task EditSelectedNodeAsync(int nodeId)
    {
        var node = ViewModel.FindNode(nodeId);
        if (node is null)
        {
            ViewModel.Status = "请先点选连线、容器或互联网节点。";
            return;
        }

        if (node.IsContainer)
        {
            var name = await PromptDialogs.PromptContainerNameAsync(XamlRoot, node.Label);
            if (name is null)
            {
                return;
            }

            ViewModel.SetNodeLabel(nodeId, name);
            RebuildCanvas();
            return;
        }

        if (!node.IsIsp)
        {
            ViewModel.Status = "设备节点批注请在设备列表中编辑；容器可改名称，互联网节点可改运营商。";
            return;
        }

        var result = await PromptDialogs.PromptIspAsync(XamlRoot, node.Label, node.IspKey);
        if (result is null)
        {
            return;
        }

        ViewModel.SetNodeLabel(nodeId, result.Value.Label, result.Value.IspKey);
        RebuildCanvas();
    }

    private void UpdateNodePosition(int nodeId)
    {
        var node = ViewModel.FindNode(nodeId);
        if (node is null || !_nodeVisuals.TryGetValue(nodeId, out var border))
        {
            return;
        }

        Canvas.SetLeft(border, TopologyMetrics.ClampX(node.X));
        Canvas.SetTop(border, TopologyMetrics.ClampY(node.Y));
        border.Width = TopologyMetrics.WidthOf(node);
        border.Height = TopologyMetrics.HeightOf(node);
        if (_containerCaptions.TryGetValue(nodeId, out var caption))
        {
            caption.Text = string.IsNullOrWhiteSpace(node.Label) ? "容器" : node.Label.Trim();
            caption.Width = TopologyMetrics.WidthOf(node);
            Canvas.SetLeft(caption, TopologyMetrics.ClampX(node.X));
            Canvas.SetTop(
                caption,
                TopologyMetrics.ClampY(node.Y) + TopologyMetrics.HeightOf(node) + TopologyMetrics.ContainerCaptionGap);
        }
        foreach (var link in _linkVisuals.Values)
        {
            if (link.From == nodeId || link.To == nodeId)
            {
                try
                {
                    UpdateLinkGeometry(link);
                }
                catch
                {
                    // ignore
                }
            }
        }

        if (!node.IsContainer)
        {
            return;
        }

        foreach (var child in ViewModel.ChildrenOf(nodeId))
        {
            UpdateNodePosition(child.Id);
        }
    }

    private void UpdateLinkGeometry(LinkVisual visual)
    {
        if (!TryGetLinkAnchors(visual.Id, out var a, out var b)
            || !TopologyMetrics.IsValid(a.X)
            || !TopologyMetrics.IsValid(a.Y)
            || !TopologyMetrics.IsValid(b.X)
            || !TopologyMetrics.IsValid(b.Y))
        {
            visual.Stroke.Data = null;
            visual.Hit.Data = null;
            visual.Handle.Visibility = Visibility.Collapsed;
            return;
        }

        var curve = ViewModel.FindLink(visual.Id)?.Curve ?? 0;
        if (!TopologyMetrics.IsValid(curve))
        {
            curve = 0;
        }

        var geometry = CreateCurveGeometry(a, b, curve);
        if (geometry is null)
        {
            visual.Stroke.Data = null;
            visual.Hit.Data = null;
            visual.Handle.Visibility = Visibility.Collapsed;
            return;
        }

        visual.Stroke.Data = geometry;
        visual.Hit.Data = CreateCurveGeometry(a, b, curve);
        var mid = CurvePoint(a, b, curve);
        if (!TopologyMetrics.IsValid(mid.X) || !TopologyMetrics.IsValid(mid.Y))
        {
            visual.Handle.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(visual.Handle, mid.X - visual.Handle.Width / 2);
        Canvas.SetTop(visual.Handle, mid.Y - visual.Handle.Height / 2);
        visual.Handle.Visibility = Visibility.Visible;
        if (visual.Caption is null)
        {
            return;
        }

        var ox = 0.0;
        var oy = -14.0;
        if (TryNormal(a, b, out var nx, out var ny))
        {
            ox = nx * 14;
            oy = ny * 14;
        }

        visual.Caption.Measure(new Size(240, 40));
        var size = visual.Caption.DesiredSize;
        Canvas.SetLeft(visual.Caption, mid.X + ox - size.Width / 2);
        Canvas.SetTop(visual.Caption, mid.Y + oy - size.Height / 2);
    }

    private bool TryGetLinkAnchors(int linkId, out Point a, out Point b)
    {
        a = default;
        b = default;
        var link = ViewModel.FindLink(linkId);
        if (link is null
            || !_nodeVisuals.TryGetValue(link.FromNodeId, out var from)
            || !_nodeVisuals.TryGetValue(link.ToNodeId, out var to))
        {
            return false;
        }

        a = CenterOf(from);
        b = CenterOf(to);
        var siblings = ViewModel.Document.Links
            .Where(l => l.FromNodeId == link.FromNodeId && l.ToNodeId == link.ToNodeId)
            .OrderBy(l => l.Id)
            .ToList();
        var index = siblings.FindIndex(l => l.Id == linkId);
        OffsetPair(ref a, ref b, Math.Max(index, 0), siblings.Count);
        return true;
    }

    private static void OffsetPair(ref Point a, ref Point b, int index, int count)
    {
        if (count <= 1)
        {
            return;
        }

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
        {
            return;
        }

        var shift = (index - (count - 1) / 2.0) * LinkOffset;
        var ox = -dy / len * shift;
        var oy = dx / len * shift;
        a = new Point(a.X + ox, a.Y + oy);
        b = new Point(b.X + ox, b.Y + oy);
    }

    private static PathGeometry? CreateCurveGeometry(Point a, Point b, double curve)
    {
        var control = CurveControl(a, b, curve);
        if (!TopologyMetrics.IsValid(control.X) || !TopologyMetrics.IsValid(control.Y))
        {
            return null;
        }

        var figure = new PathFigure { StartPoint = a, IsClosed = false };
        figure.Segments.Add(new QuadraticBezierSegment
        {
            Point1 = control,
            Point2 = b
        });
        return new PathGeometry { Figures = { figure } };
    }

    private static Point CurveControl(Point a, Point b, double curve)
    {
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        return TryNormal(a, b, out var nx, out var ny)
            ? new Point(mid.X + 2 * nx * curve, mid.Y + 2 * ny * curve)
            : mid;
    }

    private static Point CurvePoint(Point a, Point b, double curve)
    {
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        return TryNormal(a, b, out var nx, out var ny)
            ? new Point(mid.X + nx * curve, mid.Y + ny * curve)
            : mid;
    }

    private static double CurveFromPoint(Point a, Point b, Point point)
    {
        var mx = (a.X + b.X) / 2;
        var my = (a.Y + b.Y) / 2;
        return TryNormal(a, b, out var nx, out var ny)
            ? (point.X - mx) * nx + (point.Y - my) * ny
            : 0;
    }

    private static bool TryNormal(Point a, Point b, out double nx, out double ny)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
        {
            nx = 0;
            ny = 0;
            return false;
        }

        nx = -dy / len;
        ny = dx / len;
        return true;
    }

    private void ApplySelection()
    {
        var defaultBorder = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        foreach (var (id, border) in _nodeVisuals)
        {
            if (_pendingFromId == id)
            {
                border.BorderBrush = PendingNodeBorder;
                border.BorderThickness = new Thickness(2.5);
            }
            else if (_selectedNodeIds.Contains(id))
            {
                border.BorderBrush = SelectedNodeBorder;
                border.BorderThickness = new Thickness(2.5);
            }
            else
            {
                border.BorderBrush = defaultBorder;
                border.BorderThickness = new Thickness(1.5);
            }
        }

        foreach (var (id, visual) in _linkVisuals)
        {
            var selected = _selectedLinkIds.Contains(id);
            visual.Stroke.StrokeThickness = selected ? 4.5 : 2.5;
            visual.Handle.Opacity = selected ? 1 : 0.45;
        }
    }

    private static Point CenterOf(Border border)
    {
        var w = border.Width > 0 ? border.Width : NodeWidth;
        var h = border.Height > 0 ? border.Height : NodeHeight;
        return new(Canvas.GetLeft(border) + w / 2, Canvas.GetTop(border) + h / 2);
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 8)
        {
            s = s[2..];
        }

        if (s.Length == 6
            && byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return Color.FromArgb(255, r, g, b);
        }

        return fallback;
    }

    private sealed record ContainerResizeTag(int NodeId, ResizeEdges Edges, InputSystemCursorShape Cursor);

    [Flags]
    private enum ResizeEdges
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    private sealed record LinkVisual(
        int Id,
        int From,
        int To,
        Microsoft.UI.Xaml.Shapes.Path Stroke,
        Microsoft.UI.Xaml.Shapes.Path Hit,
        Ellipse Handle,
        Border? Caption);
}
