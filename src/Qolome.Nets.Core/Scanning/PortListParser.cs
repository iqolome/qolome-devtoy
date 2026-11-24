using System.Globalization;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public static class PortListParser
{
    public const string DefaultTcpPortsText =
        "21,22,23,25,53,80,110,135,139,143,443,445,554,993,995,1433,1883,3306,3389,5000,5432,5900,6379,8000,8080,8443,9100,27017";

    public const string DefaultUdpPortsText =
        "53,67,69,123,137,161,162,500,514,520,1194,1434,1900,4500,5060,5353,5355,3702";

    public const string DefaultPortsText = DefaultTcpPortsText;

    private const int MaxPorts = 4096;
    private static readonly char[] TokenSeparators = [',', ';', '，', '、', '\n', '\r', '\t'];

    public static IReadOnlyList<int> Parse(string? text) => Parse(text, required: true);

    public static IReadOnlyList<int> ParseOrEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : Parse(text, required: true);

    public static string LookupService(int port, string? protocol = PortPurposeCatalog.Tcp) =>
        PortPurposeCatalog.LookupService(port, protocol);

    private static IReadOnlyList<int> Parse(string? text, bool required)
    {
        var source = text?.Trim() ?? "";
        if (source.Length == 0)
        {
            if (required)
            {
                throw new ScanException("请至少指定一个端口。");
            }

            return [];
        }

        var ports = new SortedSet<int>();
        foreach (var raw in source.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddToken(ports, raw);
        }

        if (ports.Count == 0)
        {
            throw new ScanException("请至少指定一个端口。");
        }

        if (ports.Count > MaxPorts)
        {
            throw new ScanException($"端口数量超过 {MaxPorts}，请缩小范围。");
        }

        return ports.ToList();
    }

    private static void AddToken(SortedSet<int> ports, string raw)
    {
        var token = NormalizeRangeSeparators(StripProtocolSuffix(raw)).Trim();
        if (token.Length == 0)
        {
            return;
        }

        var dash = token.IndexOf('-');
        if (dash >= 0)
        {
            if (dash == 0 || dash == token.Length - 1 || token.IndexOf('-', dash + 1) >= 0)
            {
                throw new ScanException($"无法解析端口区间：{raw}");
            }

            var startText = token[..dash].Trim();
            var endText = token[(dash + 1)..].Trim();
            if (!TryParsePortNumber(startText, out var start) || !TryParsePortNumber(endText, out var end))
            {
                throw new ScanException($"无法解析端口区间：{raw}");
            }

            if (start > end)
            {
                (start, end) = (end, start);
            }

            if (end - start + 1 > MaxPorts)
            {
                throw new ScanException($"单个端口区间不能超过 {MaxPorts} 个端口。");
            }

            for (var port = start; port <= end; port++)
            {
                AddPort(ports, port);
            }

            return;
        }

        if (!TryParsePortNumber(token, out var single))
        {
            throw new ScanException($"无法解析端口：{raw}");
        }

        AddPort(ports, single);
    }

    private static string StripProtocolSuffix(string token)
    {
        var slash = token.LastIndexOf('/');
        if (slash <= 0)
        {
            return token;
        }

        return token[..slash];
    }

    private static string NormalizeRangeSeparators(string token) =>
        token
            .Replace('~', '-')
            .Replace('～', '-')
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('－', '-')
            .Replace('\u2212', '-');

    private static bool TryParsePortNumber(string text, out int port) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);

    private static void AddPort(SortedSet<int> ports, int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ScanException($"端口 {port} 无效。");
        }

        ports.Add(port);
        if (ports.Count > MaxPorts)
        {
            throw new ScanException($"端口数量超过 {MaxPorts}，请缩小范围。");
        }
    }
}
