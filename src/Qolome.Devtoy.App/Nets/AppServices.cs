using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.App;

public static class AppServices
{
    public static InventoryService Inventory { get; } = new();

    public static PortPurposeStore PortPurposes { get; } = new();

    public static ScanOptions Scan { get; private set; } = ScanOptions.Defaults();

    public static int? SelectedEnvironmentId { get; set; }

    public static int? SelectedGroupId { get; set; }

    public static void LoadScanSettings()
    {
        Scan = ScanSettingsStore.Load();
    }

    public static void SaveScanSettings()
    {
        ScanSettingsStore.Save(Scan);
    }
}
