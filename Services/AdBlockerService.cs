using System.Net.Http;

namespace EDBrowser.Services;

/// <summary>
/// High-speed domain/subdomain + URL-pattern + remote EasyList/Peter Lowe ad, tracker,
/// telemetry blocker with per-site whitelist support and cosmetic CSS injection.
/// </summary>
public sealed class AdBlockerService
{
    public const string CosmeticHideCss =
        ".adsbygoogle, ins.adsbygoogle, [id^='google_ads_iframe'], [id^='div-gpt-ad'], " +
        "[data-ad-slot], [data-ad-client], .ad-banner, .ad-wrapper, .sponsored-ad, " +
        ".taboola-widget, .outbrain-widget, aside[class*='ad-'] { display: none !important; visibility: hidden !important; height: 0 !important; }";

    private static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com",
        "googletagmanager.com", "googletagservices.com", "adservice.google.com",
        "facebook.net", "connect.facebook.net", "analytics.twitter.com", "ads.twitter.com",
        "adnxs.com", "adsafeprotected.com", "advertising.com", "adform.net",
        "scorecardresearch.com", "outbrain.com", "taboola.com", "revcontent.com",
        "criteo.com", "criteo.net", "pubmatic.com", "rubiconproject.com",
        "openx.net", "appnexus.com", "smartadserver.com", "2mdn.net",
        "moatads.com", "adsrvr.org", "quantserve.com", "chartbeat.com",
        "hotjar.com", "mouseflow.com", "adroll.com", "bidswitch.net",
        "casalemedia.com", "contextweb.com", "spotxchange.com", "sharethrough.com",
        "triplelift.com", "yieldmo.com", "media.net", "trafficjunky.net", "propellerads.com",
        "adcolony.com", "adition.com", "adjust.com", "admob.com", "ads.yahoo.com",
        "adserver.org", "adsterra.com", "adtech.de", "adtechus.com", "amazon-adsystem.com",
        "amplitude.com", "appsflyer.com", "atdmt.com", "bat.bing.com", "betrad.com",
        "bizographics.com", "bluekai.com", "bounceexchange.com", "branch.io", "bttrack.com",
        "buysellads.com", "carbonads.com", "clarity.ms", "clicktale.net", "cloudflareinsights.com",
        "connatix.com", "conviva.com", "crazyegg.com", "demdex.net", "dotomi.com",
        "doubleverify.com", "dynamicyield.com", "exoclick.com", "exponential.com", "eyeota.net",
        "flurry.com", "fullstory.com", "fyber.com", "gemius.pl", "go-mpulse.net",
        "heapanalytics.com", "histats.com", "indexexchange.com", "infolinks.com", "inmobi.com",
        "innovid.com", "inspectlet.com", "integralads.com", "ipredictive.com", "ironsrc.com",
        "juicyads.com", "kargo.com", "kissmetrics.com", "krxd.net", "liadm.com",
        "lijit.com", "liveintent.com", "liveramp.com", "loggly.com", "luckyorange.com",
        "mathtag.com", "mc.yandex.ru", "medallia.com", "mgid.com", "mixpanel.com",
        "ml314.com", "mxpnl.com", "nativery.com", "newrelic.com", "nr-data.net",
        "omtrdc.net", "onetrust.com", "optimizely.com", "owneriq.net", "parsely.com",
        "pingdom.net", "pixel.facebook.com", "popads.net", "popcash.net", "pubnative.net",
        "quantcount.com", "richrelevance.com", "rkdms.com", "rlcdn.com", "rtbhouse.com",
        "s-onetag.com", "sail-horizon.com", "segment.com", "segment.io", "serving-sys.com",
        "skimresources.com", "smartclip.net", "snapads.com", "sonobi.com", "sovrn.com",
        "spot.im", "springserve.com", "statcounter.com", "stickyadstv.com", "tapad.com",
        "teads.tv", "tiktok.com/i18n/pixel", "tradedesk.com", "tribalfusion.com", "turn.com",
        "undertone.com", "unityads.unity3d.com", "viglink.com", "vungle.com", "w55c.net",
        "webtrendslive.com", "zemanta.com", "zergnet.com"
    };

    private static readonly string[] BlockedSubstrings =
    {
        "/pagead/", "/ads/js/", "/gtag/js", "/gtm.js", "/fbevents.js", "/adsense/", "/doubleclick/"
    };

    private volatile HashSet<string> _dynamicDomains = new(StringComparer.OrdinalIgnoreCase);
    private long _totalBlocked;

    public long TotalBlockedCount => Interlocked.Read(ref _totalBlocked);
    public int DynamicRuleCount => _dynamicDomains.Count;
    public int TotalRuleCount => BlockedDomains.Count + _dynamicDomains.Count;

    public AdBlockerService() { }

    public AdBlockerService(StorageService storage)
    {
        LoadRemoteRulesFromFile(storage.RemoteAdBlockPath);
    }

    public void LoadRemoteRulesFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            var parsed = ParseDomainFilterLines(File.ReadLines(filePath));
            if (parsed.Count > 0)
                _dynamicDomains = parsed;
        }
        catch { }
    }

    public static HashSet<string> ParseDomainFilterLines(IEnumerable<string> lines)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!') || line.StartsWith('['))
                continue;

            // Support EasyList ||domain.com^ and hosts "0.0.0.0 domain.com" and plain domain lists
            if (line.StartsWith("||", StringComparison.Ordinal))
            {
                int end = line.IndexOfAny(new[] { '^', '/', '$', '*' }, 2);
                var dom = end > 2 ? line[2..end] : line[2..];
                if (IsValidFilterHost(dom)) set.Add(dom);
                continue;
            }

            if (line.StartsWith("0.0.0.0 ", StringComparison.Ordinal) || line.StartsWith("127.0.0.1 ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && IsValidFilterHost(parts[1]))
                    set.Add(parts[1]);
                continue;
            }

            if (IsValidFilterHost(line))
                set.Add(line);
        }
        return set;
    }

    private static bool IsValidFilterHost(string host) =>
        host.Length >= 4 && host.Contains('.') && !host.Contains(' ') && !host.Contains('/') && !host.Contains(':');

    public async Task<(bool Ok, int AddedCount, string Message)> UpdateFilterListsAsync(string cacheFilePath)
    {
        const string peterLoweUrl = "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=plain&showintro=0&mimetype=plaintext";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EDBrowser/5.0");
            var raw = await http.GetStringAsync(peterLoweUrl).ConfigureAwait(false);
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var parsed = ParseDomainFilterLines(lines);
            if (parsed.Count < 100)
                return (false, 0, "Filtre listesi geçersiz görünüyor.");

            var dir = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllLinesAsync(cacheFilePath, parsed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
            _dynamicDomains = parsed;
            return (true, parsed.Count, $"{parsed.Count:N0} güncel reklam/izleyici kuralı yüklendi.");
        }
        catch (Exception ex)
        {
            return (false, 0, "Liste indirilemedi: " + ex.Message);
        }
    }

    public bool ShouldBlock(string url, string? topLevelHost = null, IEnumerable<string>? whitelistHosts = null)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (!string.IsNullOrEmpty(topLevelHost) && whitelistHosts is not null)
        {
            foreach (var w in whitelistHosts)
            {
                if (topLevelHost.Equals(w, StringComparison.OrdinalIgnoreCase) ||
                    topLevelHost.EndsWith("." + w, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
            return false;

        if (!string.IsNullOrEmpty(topLevelHost) &&
            host.Equals(topLevelHost, StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsHostBlocked(host))
            return true;

        var pathAndQuery = uri.PathAndQuery;
        foreach (var sub in BlockedSubstrings)
        {
            if (pathAndQuery.Contains(sub, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public void RecordBlocked() => Interlocked.Increment(ref _totalBlocked);

    private bool IsHostBlocked(string host)
    {
        var dyn = _dynamicDomains;
        if (BlockedDomains.Contains(host) || dyn.Contains(host))
            return true;

        int dot = host.IndexOf('.');
        while (dot >= 0 && dot + 1 < host.Length)
        {
            var parent = host[(dot + 1)..];
            if (BlockedDomains.Contains(parent) || dyn.Contains(parent))
                return true;
            dot = host.IndexOf('.', dot + 1);
        }
        return false;
    }
}
