using EDBrowser.Models;
using Microsoft.Web.WebView2.Core;

namespace EDBrowser.Services;

public sealed class BrowserContext
{
    public StorageService Storage { get; }
    public ProfileManagerService Profiles { get; }
    public FaviconService Favicons { get; }
    public BrowserSettings Settings { get; set; }
    public PasswordVaultService Vault { get; }
    public AdBlockerService AdBlocker { get; }
    public ExtensionService Extensions { get; }
    public LocalVpnRouterService VpnRouter { get; }
    public List<BookmarkItem> Bookmarks { get; set; }
    public List<HistoryEntry> History { get; set; }
    public List<DownloadRecord> Downloads { get; set; }
    public List<QuickShortcut> Shortcuts { get; set; }

    private CoreWebView2Environment? _sharedEnv;
    private readonly SemaphoreSlim _envLock = new(1, 1);

    public BrowserContext(string? profileId = null)
    {
        Storage = new StorageService(profileId);
        Profiles = new ProfileManagerService(Storage);
        Favicons = new FaviconService(Storage);
        Settings = Storage.LoadSettings();
        Vault = new PasswordVaultService(Storage);
        AdBlocker = new AdBlockerService(Storage);
        Extensions = new ExtensionService(Storage);
        VpnRouter = new LocalVpnRouterService();
        VpnRouter.Start();
        VpnRouter.ApplySettings(Settings);
        Bookmarks = Storage.LoadBookmarks();
        History = Storage.LoadHistory();
        Downloads = Storage.LoadDownloads();
        Shortcuts = Storage.LoadShortcuts();
    }

    public async Task<CoreWebView2Environment> GetSharedEnvironmentAsync(string? userDataOverride = null)
    {
        if (userDataOverride is null && _sharedEnv is not null)
            return _sharedEnv;

        await _envLock.WaitAsync();
        try
        {
            if (userDataOverride is null && _sharedEnv is not null)
                return _sharedEnv;

            var args = BuildBrowserArguments(Settings, VpnRouter.ListenPort);
            var options = new CoreWebView2EnvironmentOptions(args)
            {
                Language = "tr-TR",
                AreBrowserExtensionsEnabled = true
            };

            var userData = userDataOverride ?? Storage.WebViewUserDataDirectory;
            var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
            if (userDataOverride is null)
                _sharedEnv = env;
            return env;
        }
        finally
        {
            _envLock.Release();
        }
    }

    public static string BuildBrowserArguments(BrowserSettings s, int localRouterPort = 0)
    {
        var args = new List<string>
        {
            $"--renderer-process-limit={Math.Clamp(s.RendererProcessLimit, 1, 8)}",
            "--disable-background-networking",
            "--disable-sync",
            "--disable-component-update",
            "--disable-default-apps",
            "--disable-domain-reliability",
            "--disable-breakpad",
            "--no-first-run",
            "--disable-features=Translate,OptimizationHints,MediaRouter,msEdgeShoppingAssist,msEdgeTelemetry",
            "--js-flags=--max-old-space-size=256"
        };

        if (s.WebRtcLeakProtection)
        {
            args.Add("--force-webrtc-ip-handling-policy=disable_non_proxied_udp");
            args.Add("--enforce-webrtc-ip-permission-check");
        }

        if (!s.HardwareAcceleration)
            args.Add("--disable-gpu");

        if (s.BackgroundTabThrottle)
            args.Add("--enable-features=QuickIntensiveWakeUpThrottlingAfterLoading");

        var proxyArgs = ProxyService.BuildChromiumProxyArgs(s, localRouterPort);
        if (!string.IsNullOrWhiteSpace(proxyArgs))
            args.Add(proxyArgs);

        return string.Join(' ', args);
    }

    public void AddHistory(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return;

        if (History.Count > 0 && History[0].Url.Equals(url, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(title))
                History[0].Title = title;
            History[0].VisitedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        else
        {
            History.Insert(0, new HistoryEntry
            {
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url
            });
            if (History.Count > 2000)
                History.RemoveRange(2000, History.Count - 2000);
        }

        Storage.SaveHistory(History);
    }

    public void SaveAll()
    {
        Storage.SaveSettings(Settings);
        Storage.SaveBookmarks(Bookmarks);
        Storage.SaveHistory(History);
        Storage.SaveDownloads(Downloads);
        Storage.SaveShortcuts(Shortcuts);
    }

    public string NormalizeUrlOrSearch(string input)
    {
        var u = (input ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(u)) return string.Empty;
        if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            u.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return u;

        if (u.Contains("://", StringComparison.Ordinal))
            return u;

        if (u.Contains('.') && !u.Contains(' '))
            return "https://" + u;

        var engine = string.IsNullOrWhiteSpace(Settings.SearchEngine)
            ? "https://www.google.com/search?q="
            : Settings.SearchEngine;
        return engine + Uri.EscapeDataString(u);
    }
}
