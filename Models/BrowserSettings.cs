namespace EDBrowser.Models;

public enum BrowserTheme
{
    Dark,
    Light
}

public enum ProxyMode
{
    Direct,
    System,
    Custom,
    Tor
}

public sealed class BrowserSettings
{
    public string Version { get; set; } = "4.2.0";
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

    // Optional Proxy/DoH settings (parity with v3.1.8 proxy-engine)
    public ProxyMode ProxyMode { get; set; } = ProxyMode.System;
    public string CustomProxy { get; set; } = string.Empty;
    public bool UseCloudflareDoH { get; set; } = false;
}
