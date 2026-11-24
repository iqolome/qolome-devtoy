namespace Qolome.Nets.Core.Scanning;

public static class OuiVendorLookup
{
    public static string? Lookup(string? mac) => Data.OuiStore.Lookup(mac);
}