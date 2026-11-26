using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Qolome.Nets.Core.Scanning;

/// <summary>查询本机公网出口 IPv4/IPv6 与 ASN/ISP/位置（依赖公网 HTTP API）。</summary>
public static class EgressNetworkLookup
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Qolome.DevToy/1.0 (+local-host-info)");
        return client;
    }

    public static async Task<EgressNetworkInfo> QueryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var v4Task = QueryEndpointAsync("https://api.ip.sb/geoip", cancellationToken);
            var v6Task = QueryEndpointAsync("https://api-ipv6.ip.sb/geoip", cancellationToken);
            await Task.WhenAll(v4Task, v6Task).ConfigureAwait(false);

            var v4 = await v4Task.ConfigureAwait(false);
            var v6 = await v6Task.ConfigureAwait(false);

            if (v4 is null && v6 is null)
            {
                return EgressNetworkInfo.Failed("无法获取出口地址（请检查外网或代理）。");
            }

            var primary = v4 ?? v6!;
            return new EgressNetworkInfo
            {
                Ipv4 = PickIp(v4?.Ip, AddressFamilyHint.V4) ?? PickIp(primary.Ip, AddressFamilyHint.V4),
                Ipv6 = PickIp(v6?.Ip, AddressFamilyHint.V6) ?? PickIp(primary.Ip, AddressFamilyHint.V6),
                Asn = FormatAsn(primary.Asn, primary.AsnOrganization) ?? FormatAsn(v6?.Asn, v6?.AsnOrganization),
                Isp = FirstNonEmpty(primary.Isp, v6?.Isp, primary.Organization, v6?.Organization),
                Organization = FirstNonEmpty(primary.Organization, primary.AsnOrganization, v6?.Organization, v6?.AsnOrganization),
                TimeZone = FirstNonEmpty(primary.Timezone, v6?.Timezone),
                Location = FormatLocation(primary) ?? FormatLocation(v6),
            };
        }
        catch (Exception ex)
        {
            return EgressNetworkInfo.Failed(ex.Message);
        }
    }

    private static async Task<IpSbGeo?> QueryEndpointAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<IpSbGeo>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private enum AddressFamilyHint { V4, V6 }

    private static string? PickIp(string? ip, AddressFamilyHint hint)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip.Trim(), out var parsed))
        {
            return null;
        }

        return hint switch
        {
            AddressFamilyHint.V4 when parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                => parsed.ToString(),
            AddressFamilyHint.V6 when parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                => parsed.ToString(),
            _ => null
        };
    }

    private static string? FormatAsn(long? asn, string? org)
    {
        if (asn is null or 0)
        {
            return string.IsNullOrWhiteSpace(org) ? null : org.Trim();
        }

        var text = $"AS{asn}";
        if (!string.IsNullOrWhiteSpace(org))
        {
            text += $" {org.Trim()}";
        }

        return text;
    }

    private static string? FormatLocation(IpSbGeo? geo)
    {
        if (geo is null)
        {
            return null;
        }

        var parts = new[] { geo.Country, geo.Region, geo.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private sealed class IpSbGeo
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("isp")]
        public string? Isp { get; set; }

        [JsonPropertyName("organization")]
        public string? Organization { get; set; }

        [JsonPropertyName("asn")]
        public long? Asn { get; set; }

        [JsonPropertyName("asn_organization")]
        public string? AsnOrganization { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }
}
