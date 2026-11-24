namespace Qolome.Nets.Core.Data;

internal sealed class OuiLookupTable
{
    public static OuiLookupTable Empty { get; } = new([], [], [], [], [], [], []);

    private readonly string[] _vendors;
    private readonly uint[] _p24;
    private readonly ushort[] _v24;
    private readonly uint[] _p28;
    private readonly ushort[] _v28;
    private readonly ulong[] _p36;
    private readonly ushort[] _v36;

    private OuiLookupTable(
        string[] vendors,
        uint[] p24, ushort[] v24,
        uint[] p28, ushort[] v28,
        ulong[] p36, ushort[] v36)
    {
        _vendors = vendors;
        _p24 = p24;
        _v24 = v24;
        _p28 = p28;
        _v28 = v28;
        _p36 = p36;
        _v36 = v36;
    }

    public int Count => _p24.Length + _p28.Length + _p36.Length;

    public int VendorCount => _vendors.Length;

    public string? Lookup(string? mac)
    {
        if (!TryReadLeadingHex(mac, out var value, out var digits))
        {
            return null;
        }

        if (digits >= 9)
        {
            return Find(_p36, _v36, value)
                ?? Find(_p28, _v28, (uint)(value >> 8))
                ?? Find(_p24, _v24, (uint)(value >> 12));
        }

        if (digits >= 8)
        {
            return Find(_p28, _v28, (uint)(value >> 4))
                ?? Find(_p24, _v24, (uint)(value >> 8));
        }

        if (digits >= 7)
        {
            return Find(_p28, _v28, (uint)value)
                ?? Find(_p24, _v24, (uint)(value >> 4));
        }

        return Find(_p24, _v24, (uint)value);
    }

    public static OuiLookupTable Build(IEnumerable<(string Prefix, int BitLength, string Vendor)> rows)
    {
        var builder = new Builder();
        foreach (var row in rows)
        {
            builder.Add(row.Prefix, row.BitLength, row.Vendor);
        }

        return builder.ToTable();
    }

    internal sealed class Builder
    {
        private readonly Dictionary<string, ushort> _intern = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _vendors = [];
        private readonly List<(uint Prefix, ushort Vendor)> _l24 = [];
        private readonly List<(uint Prefix, ushort Vendor)> _l28 = [];
        private readonly List<(ulong Prefix, ushort Vendor)> _l36 = [];

        public void Add(string? prefix, int bits, string? vendor)
        {
            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(vendor))
            {
                return;
            }

            var id = Intern(vendor);
            if (bits == 36 && TryParseULong(prefix, 9, out var p36))
            {
                _l36.Add((p36, id));
            }
            else if (bits == 28 && TryParseUInt(prefix, 7, out var p28))
            {
                _l28.Add((p28, id));
            }
            else if (TryParseUInt(prefix, 6, out var p24))
            {
                _l24.Add((p24, id));
            }
        }

        public OuiLookupTable ToTable()
        {
            var (p24, v24) = Compact(_l24);
            var (p28, v28) = Compact(_l28);
            var (p36, v36) = Compact(_l36);
            return new OuiLookupTable([.. _vendors], p24, v24, p28, v28, p36, v36);
        }

        private ushort Intern(string name)
        {
            if (_intern.TryGetValue(name, out var id))
            {
                return id;
            }

            if (_vendors.Count >= ushort.MaxValue)
            {
                throw new InvalidOperationException("OUI 厂商名超过 65535，无法压缩索引。");
            }

            id = (ushort)_vendors.Count;
            _intern[name] = id;
            _vendors.Add(name);
            return id;
        }

        private static (uint[] Prefixes, ushort[] Vendors) Compact(List<(uint Prefix, ushort Vendor)> list)
        {
            list.Sort((a, b) => a.Prefix.CompareTo(b.Prefix));
            Dedupe(list);
            var keys = new uint[list.Count];
            var values = new ushort[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                keys[i] = list[i].Prefix;
                values[i] = list[i].Vendor;
            }

            return (keys, values);
        }

        private static (ulong[] Prefixes, ushort[] Vendors) Compact(List<(ulong Prefix, ushort Vendor)> list)
        {
            list.Sort((a, b) => a.Prefix.CompareTo(b.Prefix));
            Dedupe(list);
            var keys = new ulong[list.Count];
            var values = new ushort[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                keys[i] = list[i].Prefix;
                values[i] = list[i].Vendor;
            }

            return (keys, values);
        }

        private static void Dedupe(List<(uint Prefix, ushort Vendor)> list)
        {
            var w = 0;
            for (var r = 0; r < list.Count; r++)
            {
                if (w == 0 || list[r].Prefix != list[w - 1].Prefix)
                {
                    list[w++] = list[r];
                }
                else
                {
                    list[w - 1] = list[r];
                }
            }

            if (w < list.Count)
            {
                list.RemoveRange(w, list.Count - w);
            }
        }

        private static void Dedupe(List<(ulong Prefix, ushort Vendor)> list)
        {
            var w = 0;
            for (var r = 0; r < list.Count; r++)
            {
                if (w == 0 || list[r].Prefix != list[w - 1].Prefix)
                {
                    list[w++] = list[r];
                }
                else
                {
                    list[w - 1] = list[r];
                }
            }

            if (w < list.Count)
            {
                list.RemoveRange(w, list.Count - w);
            }
        }

        private static bool TryParseUInt(string hex, int length, out uint value)
        {
            value = 0;
            if (hex.Length < length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                var nibble = ParseHexNibble(hex[i]);
                if (nibble < 0)
                {
                    return false;
                }

                value = (value << 4) | (uint)nibble;
            }

            return true;
        }

        private static bool TryParseULong(string hex, int length, out ulong value)
        {
            value = 0;
            if (hex.Length < length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                var nibble = ParseHexNibble(hex[i]);
                if (nibble < 0)
                {
                    return false;
                }

                value = (value << 4) | (uint)nibble;
            }

            return true;
        }

        private static int ParseHexNibble(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
    }

    private string? Find(uint[] prefixes, ushort[] vendors, uint key)
    {
        var i = Array.BinarySearch(prefixes, key);
        return i >= 0 ? _vendors[vendors[i]] : null;
    }

    private string? Find(ulong[] prefixes, ushort[] vendors, ulong key)
    {
        var i = Array.BinarySearch(prefixes, key);
        return i >= 0 ? _vendors[vendors[i]] : null;
    }

    private static bool TryReadLeadingHex(string? mac, out ulong value, out int digits)
    {
        value = 0;
        digits = 0;
        if (string.IsNullOrWhiteSpace(mac))
        {
            return false;
        }

        foreach (var c in mac)
        {
            var nibble = HexNibble(c);
            if (nibble < 0)
            {
                continue;
            }

            value = (value << 4) | (uint)nibble;
            digits++;
            if (digits == 9)
            {
                return true;
            }
        }

        return digits >= 6;
    }

    private static int HexNibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };
}