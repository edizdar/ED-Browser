namespace EDBrowser.Services;

/// <summary>
/// High-speed domain/subdomain + URL-pattern ad, tracker, and telemetry blocker.
/// Covers all networks from v3.1.8 plus extended EasyList/EasyPrivacy domains to cut JS RAM usage.
/// </summary>
public sealed class AdBlockerService
{
    private static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // v3.1.8 core list
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

        // Extended major ad & tracker networks for RAM savings
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

    public long TotalBlockedCount => Interlocked.Read(ref _totalBlocked);
    private long _totalBlocked;

    public bool ShouldBlock(string url, string? topLevelHost = null)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
            return false;

        // Avoid blocking first-party domain itself
        if (!string.IsNullOrEmpty(topLevelHost) &&
            host.Equals(topLevelHost, StringComparison.OrdinalIgnoreCase))
            return false;

        // Walk domain labels from full host up to 2-label root (O(labels) without allocations)
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

    private static bool IsHostBlocked(string host)
    {
        if (BlockedDomains.Contains(host))
            return true;

        int dot = host.IndexOf('.');
        while (dot >= 0 && dot + 1 < host.Length)
        {
            var parent = host[(dot + 1)..];
            if (BlockedDomains.Contains(parent))
                return true;
            dot = host.IndexOf('.', dot + 1);
        }
        return false;
    }
}
