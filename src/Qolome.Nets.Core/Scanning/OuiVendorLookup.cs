namespace Qolome.Nets.Core.Scanning;

public sealed class OuiVendorLookup
{
    public string? Lookup(string? mac) => Data.OuiStore.Lookup(mac);
}
