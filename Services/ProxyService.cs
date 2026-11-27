using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EDBrowser.Models;

namespace EDBrowser.Services;

public sealed record IpTraceInfo(string Ip, string Location, bool Success, string TunnelLabel = "Sistem");

public sealed record FreeVpnServer(
    string ProxyUrl,
    string Protocol,
    string CountryCode,
    string CountryName,
    int LatencyMs,
    string ExitIp)
{
    public string DisplayText => $"{CountryCode} — {CountryName} ({LatencyMs} ms) • {Protocol.ToUpperInvariant()}";
}

/// <summary>
/// Manages Chromium proxy/VPN tunnel arguments (Zero-restart LocalVpnRouterService,
/// Built-in Verified Free Server Scanner, Custom SOCKS5/HTTPS VPN profiles,
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

    private static readonly Dictionary<string, string> CountryNamesTr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = "Almanya",
        ["NL"] = "Hollanda",
        ["US"] = "ABD",
        ["GB"] = "İngiltere",
        ["FR"] = "Fransa",
        ["SG"] = "Singapur",
        ["JP"] = "Japonya",
        ["CA"] = "Kanada",
        ["CH"] = "İsviçre",
        ["SE"] = "İsveç",
        ["FI"] = "Finlandiya",
        ["PL"] = "Polonya",
        ["TR"] = "Türkiye",
        ["RO"] = "Romanya",
        ["IT"] = "İtalya",
        ["ES"] = "İspanya",
        ["BR"] = "Brezilya",
        ["KR"] = "G. Kore",
        ["IN"] = "Hindistan",
        ["AU"] = "Avustralya",
        ["AT"] = "Avusturya",
        ["BE"] = "Belçika",
        ["CZ"] = "Çekya",
        ["NO"] = "Norveç",
        ["IE"] = "İrlanda",
        ["HK"] = "Hong Kong",
        ["TW"] = "Tayvan",
        ["UA"] = "Ukrayna",
        ["ID"] = "Endonezya",
        ["TH"] = "Tayland",
        ["VN"] = "Vietnam",
        ["SA"] = "S. Arabistan",
        ["CM"] = "Kamerun",
        ["MX"] = "Meksika",
        ["AR"] = "Arjantin",
        ["CL"] = "Şili",
        ["CO"] = "Kolombiya",
        ["ZA"] = "G. Afrika",
        ["AE"] = "BAE",
        ["IL"] = "İsrail",
        ["BG"] = "Bulgaristan",
        ["GR"] = "Yunanistan",
        ["HU"] = "Macaristan",
        ["DK"] = "Danimarka",
        ["PT"] = "Portekiz",
        ["SK"] = "Slovakya",
        ["LV"] = "Letonya",
        ["LT"] = "Litvanya",
        ["EE"] = "Estonya",
        ["MY"] = "Malezya",
        ["PH"] = "Filipinler",
        ["BD"] = "Bangladeş",
        ["PK"] = "Pakistan",
        ["EG"] = "Mısır",
        ["RU"] = "Rusya"
    };

    [GeneratedRegex(@"ip=(.+)", RegexOptions.Compiled)]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"loc=(.+)", RegexOptions.Compiled)]
    private static partial Regex LocRegex();

    public static string MapCountryNameTr(string? isoCode, string? fallbackName = null)
    {
        if (!string.IsNullOrWhiteSpace(isoCode) && CountryNamesTr.TryGetValue(isoCode.Trim(), out var tr))
            return tr;
        if (!string.IsNullOrWhiteSpace(fallbackName))
            return fallbackName.Trim();
        return string.IsNullOrWhiteSpace(isoCode) ? "Bilinmiyor" : isoCode.Trim().ToUpperInvariant();
    }

    public static VpnProfile? GetActiveVpnProfile(BrowserSettings s) =>
        string.IsNullOrEmpty(s.ActiveVpnProfileId)
            ? null
            : s.VpnProfiles.FirstOrDefault(p => p.Id == s.ActiveVpnProfileId);

    public static string? ResolveEffectiveProxyUrl(BrowserSettings s, bool torFallback9150 = false, bool ignoreVpnEnabledFlag = false)
    {
        if (!ignoreVpnEnabledFlag && !s.VpnEnabled)
            return null;

        switch (s.ProxyMode)
        {
            case ProxyMode.Tor:
                if (torFallback9150 || (!IsPortOpenFast("127.0.0.1", 9050) && IsPortOpenFast("127.0.0.1", 9150)))
                    return "socks5://127.0.0.1:9150";
                return "socks5://127.0.0.1:9050";

            case ProxyMode.CloudflareWarp:
                return "socks5://127.0.0.1:40000";

            case ProxyMode.FreeCommunity:
                return string.IsNullOrWhiteSpace(s.ActiveFreeServerUrl) ? null : s.ActiveFreeServerUrl.Trim();

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

    public static string BuildChromiumProxyArgs(BrowserSettings settings, int localRouterPort = 0)
    {
        var args = new List<string>();
        if (localRouterPort > 0)
        {
            args.Add($"--proxy-server=\"http://127.0.0.1:{localRouterPort}\"");
            args.Add("--proxy-bypass-list=\"localhost;127.0.0.1;<local>\"");
        }
        else
        {
            switch (settings.ProxyMode)
            {
                case ProxyMode.Direct:
                    args.Add("--no-proxy-server");
                    break;

                case ProxyMode.Tor:
                case ProxyMode.CloudflareWarp:
                case ProxyMode.Custom:
                case ProxyMode.FreeCommunity:
                    var eff = ResolveEffectiveProxyUrl(settings, ignoreVpnEnabledFlag: true);
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

    public static async Task<IpTraceInfo> FetchTraceAsync(BrowserSettings? settings = null, int localRouterPort = 0)
    {
        string label;
        if (settings is { VpnEnabled: false })
        {
            label = settings.ProxyMode == ProxyMode.System ? "VPN Kapalı (Sistem)" : "VPN Kapalı (Doğrudan)";
        }
        else
        {
            label = settings?.ProxyMode switch
            {
                ProxyMode.Direct => "Doğrudan",
                ProxyMode.Tor => "Tor Ağı",
                ProxyMode.CloudflareWarp => "Cloudflare WARP",
                ProxyMode.FreeCommunity => string.IsNullOrEmpty(settings.ActiveFreeServerCountry)
                    ? "Ücretsiz VPN"
                    : $"Ücretsiz VPN ({settings.ActiveFreeServerCountry})",
                ProxyMode.Custom => GetActiveVpnProfile(settings)?.Name is { Length: > 0 } pn ? pn : "Özel VPN/Proxy",
                _ => "Sistem"
            };
        }

        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) };
            if (localRouterPort > 0)
            {
                handler.Proxy = new WebProxy($"http://127.0.0.1:{localRouterPort}");
                handler.UseProxy = true;
            }
            else if (settings is not null)
            {
                if (!settings.VpnEnabled && settings.ProxyMode == ProxyMode.Direct)
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

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
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

    private sealed record CandidateRelay(string ProxyUrl, string Protocol, string CountryCode, string FallbackCountry);

    public static async Task<bool> VerifyRelayMultiSiteAsync(string proxyUrl, int timeoutMs = 4000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl)) return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy(proxyUrl.Trim()),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromMilliseconds(Math.Min(2800, timeoutMs))
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = await client.GetAsync("https://www.google.com/generate_204", cts.Token).ConfigureAwait(false);
            return (int)resp.StatusCode is 204 or 200;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<FreeVpnServer>> DiscoverVerifiedFreeRelaysAsync(int maxVerified = 8, CancellationToken ct = default)
    {
        var candidates = new Dictionary<string, CandidateRelay>(StringComparer.OrdinalIgnoreCase);
        using var fetchHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        fetchHttp.DefaultRequestHeaders.UserAgent.ParseAdd("EDBrowser/4.4");

        var tProxyScrape = FetchProxyScrapeCandidatesAsync(fetchHttp, ct);
        var tGeoNode = FetchGeoNodeCandidatesAsync(fetchHttp, ct);
        await Task.WhenAll(tProxyScrape, tGeoNode).ConfigureAwait(false);

        // Interleave HTTP and SOCKS5 so broken/restricted SOCKS5 nodes don't starve working HTTP relays
        var ps = tProxyScrape.Result;
        var gn = tGeoNode.Result;
        int maxCount = Math.Max(ps.Count, gn.Count);
        for (int i = 0; i < maxCount; i++)
        {
            if (i < ps.Count) candidates.TryAdd(ps[i].ProxyUrl, ps[i]);
            if (i < gn.Count) candidates.TryAdd(gn[i].ProxyUrl, gn[i]);
        }

        if (candidates.Count == 0)
            return new List<FreeVpnServer>();

        var ordered = candidates.Values.Take(60).ToList();

        var verified = new ConcurrentBag<FreeVpnServer>();
        using var sem = new SemaphoreSlim(16, 16);
        using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var tasks = ordered.Select(async cand =>
        {
            if (verifyCts.IsCancellationRequested) return;
            try
            {
                await sem.WaitAsync(verifyCts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            try
            {
                if (verifyCts.IsCancellationRequested) return;
                using var perProbeCts = CancellationTokenSource.CreateLinkedTokenSource(verifyCts.Token);
                perProbeCts.CancelAfter(TimeSpan.FromMilliseconds(4200));

                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy(cand.ProxyUrl),
                    UseProxy = true,
                    ConnectTimeout = TimeSpan.FromMilliseconds(2600)
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(4000) };

                // Check 1: General arbitrary HTTPS site (eliminates Cloudflare-only reverse proxies like broken CZ node)
                var sw = Stopwatch.StartNew();
                using var gResp = await client.GetAsync("https://www.google.com/generate_204", perProbeCts.Token).ConfigureAwait(false);
                if ((int)gResp.StatusCode is not (204 or 200))
                    return;
                long googleMs = sw.ElapsedMilliseconds;

                // Check 2: Cloudflare trace (verifies multi-host HTTPS CONNECT tunneling + reveals exit IP & country)
                var trace = await client.GetStringAsync("https://cloudflare.com/cdn-cgi/trace", perProbeCts.Token).ConfigureAwait(false);
                var ip = IpRegex().Match(trace).Groups[1].Value.Trim();
                var loc = LocRegex().Match(trace).Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(ip)) return;

                var cc = string.IsNullOrWhiteSpace(loc) ? cand.CountryCode : loc.ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(cc)) cc = "GL";
                var name = MapCountryNameTr(cc, cand.FallbackCountry);

                verified.Add(new FreeVpnServer(
                    cand.ProxyUrl,
                    cand.Protocol,
                    cc,
                    name,
                    (int)Math.Max(1, googleMs),
                    ip));

                if (verified.Count >= maxVerified)
                    verifyCts.Cancel();
            }
            catch
            {
                // Candidate unreachable, slow, or blocked general HTTPS CONNECT
            }
            finally
            {
                sem.Release();
            }
        });

        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }

        return verified
            .OrderBy(v => v.LatencyMs)
            .Take(maxVerified)
            .ToList();
    }

    private static async Task<List<CandidateRelay>> FetchProxyScrapeCandidatesAsync(HttpClient http, CancellationToken ct)
    {
        var list = new List<CandidateRelay>();
        try
        {
            const string url = "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&proxy_format=protocolipport&format=json&limit=80";
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("proxies", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("alive", out var aliveEl) && aliveEl.ValueKind == JsonValueKind.False)
                    continue;
                var proto = item.TryGetProperty("protocol", out var pr) ? pr.GetString()?.ToLowerInvariant() ?? "" : "";
                if (proto != "socks5" && proto != "http" && proto != "https") continue;
                if (proto == "https") proto = "http";

                var ip = item.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                int port = item.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out int pv) ? pv : 0;
                if (string.IsNullOrWhiteSpace(ip) || port <= 0) continue;

                string cc = "";
                string country = "";
                if (item.TryGetProperty("ip_data", out var ipData) && ipData.ValueKind == JsonValueKind.Object)
                {
                    if (ipData.TryGetProperty("countryCode", out var ccEl)) cc = ccEl.GetString() ?? "";
                    if (ipData.TryGetProperty("country", out var cnEl)) country = cnEl.GetString() ?? "";
                }
                list.Add(new CandidateRelay($"{proto}://{ip}:{port}", proto, cc, country));
            }
        }
        catch { }
        return list;
    }

    private static async Task<List<CandidateRelay>> FetchGeoNodeCandidatesAsync(HttpClient http, CancellationToken ct)
    {
        var list = new List<CandidateRelay>();
        try
        {
            const string url = "https://proxylist.geonode.com/api/proxy-list?limit=50&page=1&sort_by=lastChecked&sort_type=desc&protocols=socks5%2Chttp";
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in arr.EnumerateArray())
            {
                var ip = item.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                string? portStr = item.TryGetProperty("port", out var portEl)
                    ? (portEl.ValueKind == JsonValueKind.Number ? portEl.GetInt32().ToString() : portEl.GetString())
                    : null;
                if (string.IsNullOrWhiteSpace(ip) || !int.TryParse(portStr, out int port) || port <= 0) continue;

                string proto = "";
                if (item.TryGetProperty("protocols", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pArr.EnumerateArray())
                    {
                        var pv = p.GetString()?.ToLowerInvariant() ?? "";
                        if (pv == "socks5") { proto = "socks5"; break; }
                        if (pv is "http" or "https") proto = "http";
                    }
                }
                if (string.IsNullOrEmpty(proto)) continue;

                string cc = item.TryGetProperty("country", out var ccEl) ? ccEl.GetString() ?? "" : "";
                list.Add(new CandidateRelay($"{proto}://{ip}:{port}", proto, cc, ""));
            }
        }
        catch { }
        return list;
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
