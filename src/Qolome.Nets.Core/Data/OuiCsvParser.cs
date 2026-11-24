using System.Text;

namespace Qolome.Nets.Core.Data;

public static class OuiCsvParser
{
    public static IEnumerable<OuiEntry> Enumerate(TextReader reader)
    {
        var header = reader.ReadLine();
        if (header is null)
        {
            yield break;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = Split(line);
            if (fields.Count < 3)
            {
                continue;
            }

            var registry = fields[0].Trim();
            var assignment = NormalizeAssignment(fields[1]);
            var vendor = NormalizeVendor(fields[2]);
            if (assignment is null || string.IsNullOrWhiteSpace(vendor))
            {
                continue;
            }

            var bits = BitsOf(registry, assignment.Length);
            if (bits is null)
            {
                continue;
            }

            yield return new OuiEntry
            {
                Prefix = assignment,
                BitLength = bits.Value,
                Registry = registry.Length == 0 ? RegistryName(bits.Value) : registry,
                Vendor = vendor
            };
        }
    }

    public static IReadOnlyList<OuiEntry> Parse(TextReader reader)
    {
        var map = new Dictionary<string, OuiEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Enumerate(reader))
        {
            map[row.Prefix] = row;
        }

        return map.Values.ToList();
    }

    public static IReadOnlyList<OuiEntry> ParseFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public static IReadOnlyList<OuiEntry> ParseText(string text)
    {
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    private static int? BitsOf(string registry, int hexLength)
    {
        var key = registry.Trim().ToUpperInvariant();
        if (key is "MA-L" or "OUI")
        {
            return 24;
        }

        if (key is "MA-M" or "OUI28" or "MAM")
        {
            return 28;
        }

        if (key is "MA-S" or "OUI36")
        {
            return 36;
        }

        return hexLength switch
        {
            6 => 24,
            7 => 28,
            9 => 36,
            _ => null
        };
    }

    private static string RegistryName(int bits) => bits switch
    {
        28 => "MA-M",
        36 => "MA-S",
        _ => "MA-L"
    };

    private static string? NormalizeAssignment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length is 6 or 7 or 9 ? hex : null;
    }

    private static string NormalizeVendor(string? raw)
    {
        var text = (raw ?? "").Trim().Trim('"');
        if (text.Length > 256)
        {
            text = text[..256];
        }

        return text;
    }

    private static List<string> Split(string line)
    {
        var fields = new List<string>();
        var buffer = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        buffer.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    buffer.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(buffer.ToString());
                buffer.Clear();
            }
            else
            {
                buffer.Append(c);
            }
        }

        fields.Add(buffer.ToString());
        return fields;
    }
}
