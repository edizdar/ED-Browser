using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using EDBrowser.Models;

namespace EDBrowser.Services;

public sealed record IpTraceInfo(string Ip, string Location, bool Success, string TunnelLabel = "Sistem");

/// <summary>
/// Manages Chromium proxy/VPN tunnel arguments (Custom SOCKS5/HTTPS VPN profiles,
/// Cloudflare WARP 127.0.0.1:40000, Tor 127.0.0.1:9050/9150), multi-provider DoH,
/// proxy-aware IP trace, HTTPS-Only upgrades, and tracking parameter stripping.
/// </summary>
public static partial class ProxyService
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "fbclid", "gclid", "dclid", "yclid", "msclkid",
        "mc_cid", "mc_eid", "igshid", "si", "mkt_tok",
        "oly_anon_id", "oly_enc_id", "_hsenc", "_hsmi", "vero_id"
    };

    [GeneratedRegex(@"ip=(.+)", RegexOptions.Compiled)]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"loc=(.+)", RegexOptions.Compiled)]
    private static partial Regex LocRegex();

    public static VpnProfile? GetActiveVpnProfile(BrowserSettings s) =>
        string.IsNullOrEmpty(s.ActiveVpnProfileId)
            ? null
            : s.VpnProfiles.FirstOrDefault(p => p.Id == s.ActiveVpnProfileId);

    public static string? ResolveEffectiveProxyUrl(BrowserSettings s, bool torFallback9150 = false)
    {
        switch (s.ProxyMode)
        {
            case ProxyMode.Tor:
                if (torFallback9150 || (!IsPortOpenFast("127.0.0.1", 9050) && IsPortOpenFast("127.0.0.1", 9150)))
                    return "socks5://127.0.0.1:9150";
                return "socks5://127.0.0.1:9050";

            case ProxyMode.CloudflareWarp:
                return "socks5://127.0.0.1:40000";

            case ProxyMode.Custom:
                var prof = GetActiveVpnProfile(s);
                var raw = !string.IsNullOrWhiteSpace(prof?.ProxyUrl) ? prof.ProxyUrl : s.CustomProxy;
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var norm = raw.Trim();
                if (!norm.Contains("://", StringComparison.Ordinal))
                    norm = "http://" + norm;
                return norm;

            default:
                return null;
        }
    }

    public static string BuildChromiumProxyArgs(BrowserSettings settings)
    {
        var args = new List<string>();
        switch (settings.ProxyMode)
        {
            case ProxyMode.Direct:
                args.Add("--no-proxy-server");
                break;

            case ProxyMode.Tor:
            case ProxyMode.CloudflareWarp:
            case ProxyMode.Custom:
                var eff = ResolveEffectiveProxyUrl(settings);
                if (!string.IsNullOrWhiteSpace(eff))
                {
                    args.Add($"--proxy-server=\"{eff}\"");
                    args.Add("--proxy-bypass-list=\"localhost;127.0.0.1;<local>\"");
                }
                break;

            case ProxyMode.System:
            default:
                break;
        }

        string? dohTemplate = settings.DohProvider switch
        {
            DohProvider.Cloudflare => "https://cloudflare-dns.com/dns-query",
            DohProvider.Quad9 => "https://dns.quad9.net/dns-query",
            DohProvider.AdGuard => "https://dns.adguard-dns.com/dns-query",
            _ => settings.UseCloudflareDoH ? "https://cloudflare-dns.com/dns-query" : null
        };

        if (!string.IsNullOrEmpty(dohTemplate))
        {
            args.Add("--dns-over-https-mode=secure");
            args.Add($"--dns-over-https-templates=\"{dohTemplate}\"");
        }

        return string.Join(' ', args);
    }

    public static bool IsPortOpenFast(string host, int port, int timeoutMs = 150)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            bool ok = ar.AsyncWaitHandle.WaitOne(timeoutMs);
            if (!ok) return false;
            client.EndConnect(ar);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> CheckLocalPortAsync(string host, int port, int timeoutMs = 350)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IpTraceInfo> FetchTraceAsync(BrowserSettings? settings = null)
    {
        string label = settings?.ProxyMode switch
        {
            ProxyMode.Direct => "Doğrudan",
            ProxyMode.Tor => "Tor Ağı",
            ProxyMode.CloudflareWarp => "Cloudflare WARP",
            ProxyMode.Custom => GetActiveVpnProfile(settings)?.Name is { Length: > 0 } pn ? pn : "Özel VPN/Proxy",
            _ => "Sistem"
        };

        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) };
            if (settings is not null)
            {
                if (settings.ProxyMode == ProxyMode.Direct)
                {
                    handler.UseProxy = false;
                }
                else
                {
                    var effUrl = ResolveEffectiveProxyUrl(settings);
                    if (!string.IsNullOrEmpty(effUrl))
                    {
                        var proxy = new WebProxy(effUrl);
                        var prof = GetActiveVpnProfile(settings);
                        if (prof is not null && !string.IsNullOrEmpty(prof.Username))
                            proxy.Credentials = new NetworkCredential(prof.Username, prof.Password);
                        handler.Proxy = proxy;
                        handler.UseProxy = true;
                    }
                }
            }

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
            var raw = await http.GetStringAsync("https://cloudflare.com/cdn-cgi/trace");
            var ip = IpRegex().Match(raw).Groups[1].Value.Trim();
            var loc = LocRegex().Match(raw).Groups[1].Value.Trim();
            return new IpTraceInfo(
                string.IsNullOrEmpty(ip) ? "Bilinmiyor" : ip,
                loc,
                true,
                label);
        }
        catch
        {
            return new IpTraceInfo("Bağlantı hatası", "", false, label);
        }
    }

    public static string CleanTrackingParameters(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.Contains('?', StringComparison.Ordinal))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return url;

        var rawQuery = uri.Query;
        if (string.IsNullOrEmpty(rawQuery) || rawQuery == "?")
            return url;

        var parts = rawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(parts.Length);
        bool removedAny = false;

        foreach (var part in parts)
        {
            int eq = part.IndexOf('=');
            var rawKey = eq >= 0 ? part[..eq] : part;
            var key = Uri.UnescapeDataString(rawKey);
            if (TrackingParams.Contains(key))
                removedAny = true;
            else
                kept.Add(part);
        }

        if (!removedAny)
            return url;

        var basePart = uri.GetLeftPart(UriPartial.Path);
        var sb = new StringBuilder(basePart);
        if (kept.Count > 0)
        {
            sb.Append('?');
            sb.Append(string.Join('&', kept));
        }
        if (!string.IsNullOrEmpty(uri.Fragment))
            sb.Append(uri.Fragment);

        return sb.ToString();
    }

    public static bool TryUpgradeToHttps(string url, out string upgradedUrl)
    {
        upgradedUrl = url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host == "::1" ||
            IsPrivateOrLoopbackIp(host))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };
        upgradedUrl = builder.Uri.AbsoluteUri;
        return true;
    }

    private static bool IsPrivateOrLoopbackIp(string host)
    {
        if (!IPAddress.TryParse(host, out var ip))
            return false;
        if (IPAddress.IsLoopback(ip))
            return true;

        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            if (b[0] == 10 || b[0] == 127) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        }
        return false;
    }
}
