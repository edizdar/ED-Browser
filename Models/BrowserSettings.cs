namespace EDBrowser.Models;

public enum BrowserTheme
{
    Dark,
    Light
}

public enum ProxyMode
{
    Direct = 0,
    System = 1,
    Custom = 2,
    Tor = 3,
    CloudflareWarp = 4,
    FreeCommunity = 5
}

public enum DohProvider
{
    Off = 0,
    Cloudflare = 1,
    Quad9 = 2,
    AdGuard = 3
}

public sealed class VpnProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ProxyUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class BrowserSettings
{
    public string Version { get; set; } = "4.4.1";
    public string HomePage { get; set; } = "https://www.google.com";
    public string SearchEngine { get; set; } = "https://www.google.com/search?q=";
    public BrowserTheme Theme { get; set; } = BrowserTheme.Dark;
    public bool ShowBookmarkBar { get; set; } = true;
    public bool ShowLeftSidebar { get; set; } = false;
    public bool AdBlockEnabled { get; set; } = true;
    public bool HardwareAcceleration { get; set; } = true;
    public bool BackgroundTabThrottle { get; set; } = true;
    public bool LoadImages { get; set; } = true;
    public bool CheckUpdatesOnStartup { get; set; } = true;

    // v4 RAM optimization settings
    public int AutoSleepMinutes { get; set; } = 2;
    public int DeepUnloadMinutes { get; set; } = 10;
    public int RendererProcessLimit { get; set; } = 2;

    // Privacy & VPN / Proxy / DoH settings
    public bool VpnEnabled { get; set; } = false;
    public ProxyMode ProxyMode { get; set; } = ProxyMode.System;
    public string CustomProxy { get; set; } = string.Empty;
    public string ActiveFreeServerUrl { get; set; } = string.Empty;
    public string ActiveFreeServerLabel { get; set; } = string.Empty;
    public string ActiveFreeServerCountry { get; set; } = string.Empty;
    public List<VpnProfile> VpnProfiles { get; set; } = new();
    public string ActiveVpnProfileId { get; set; } = string.Empty;
    public DohProvider DohProvider { get; set; } = DohProvider.Cloudflare;
    public bool UseCloudflareDoH { get; set; } = false; // legacy migration flag
    public bool HttpsOnlyMode { get; set; } = true;
    public bool WebRtcLeakProtection { get; set; } = true;
    public bool StripTrackingParams { get; set; } = true;
    public bool SendGpcAndDnt { get; set; } = true;
    public bool FingerprintProtection { get; set; } = true;
    public bool ClearDataOnExit { get; set; } = false;
}
