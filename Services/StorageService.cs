using System.Text.Json;
using System.Text.RegularExpressions;
using EDBrowser.Models;

namespace EDBrowser.Services;

public sealed class StorageService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string BaseDataDirectory { get; }
    public string ActiveProfileId { get; }
    public string DataDirectory { get; }
    public bool IsInstalledMode { get; }
    public string WebViewUserDataDirectory { get; }
    public string DownloadsDirectory { get; }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string SessionPath => Path.Combine(DataDirectory, "session.json");
    public string BookmarksPath => Path.Combine(DataDirectory, "bookmarks.json");
    public string HistoryPath => Path.Combine(DataDirectory, "history.json");
    public string DownloadsPath => Path.Combine(DataDirectory, "downloads.json");
    public string PasswordsPath => Path.Combine(DataDirectory, "passwords.json");
    public string ShortcutsPath => Path.Combine(DataDirectory, "shortcuts.json");
    public string ExtensionsPath => Path.Combine(DataDirectory, "extensions.json");
    public string ExtensionsDirectory => Path.Combine(DataDirectory, "Extensions");
    public string FaviconsDirectory => Path.Combine(DataDirectory, "Favicons");
    public string RemoteAdBlockPath => Path.Combine(DataDirectory, "adblock_remote.txt");
    public string KeyPath => Path.Combine(DataDirectory, "vault.key");

    public StorageService(string? profileId = null)
    {
        IsInstalledMode = File.Exists(Path.Combine(AppContext.BaseDirectory, "installed.flag"));
        BaseDataDirectory = ResolvePortableOrLocalDataDir(IsInstalledMode);
        Directory.CreateDirectory(BaseDataDirectory);

        ActiveProfileId = string.IsNullOrWhiteSpace(profileId) || profileId.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? "default"
            : SanitizeProfileId(profileId);

        DataDirectory = ActiveProfileId == "default"
            ? BaseDataDirectory
            : Path.Combine(BaseDataDirectory, "Profiles", ActiveProfileId);

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(FaviconsDirectory);
        WebViewUserDataDirectory = Path.Combine(DataDirectory, "WebView2Cache");
        Directory.CreateDirectory(WebViewUserDataDirectory);
        DownloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        if (!Directory.Exists(DownloadsDirectory))
        {
            DownloadsDirectory = DataDirectory;
        }
    }

    public static string SanitizeProfileId(string raw)
    {
        var s = Regex.Replace((raw ?? "").Trim(), @"[^a-zA-Z0-9_-]", "_");
        return string.IsNullOrWhiteSpace(s) ? "profile" : s[..Math.Min(s.Length, 40)];
    }

    private static string ResolvePortableOrLocalDataDir(bool isInstalledMode)
    {
        if (isInstalledMode)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EDBrowser4");
        }

        try
        {
            var exeDir = AppContext.BaseDirectory;
            var portableDir = Path.Combine(exeDir, "EDBrowserData");
            var testFile = Path.Combine(exeDir, $".write_test_{Environment.ProcessId}.tmp");
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);
            return portableDir;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EDBrowser4");
        }
    }

    public T LoadJson<T>(string path, T fallback)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var result = JsonSerializer.Deserialize<T>(json, JsonOpts);
                if (result is not null)
                    return result;
            }
        }
        catch
        {
            // Return fallback on corrupt or locked file
        }
        return fallback;
    }

    public void SaveJson<T>(string path, T data)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(data, JsonOpts);
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Ignore transient IO failures during shutdown
        }
    }

    public BrowserSettings LoadSettings()
    {
        var s = LoadJson(SettingsPath, new BrowserSettings());
        if (s.UseCloudflareDoH && s.DohProvider == DohProvider.Off)
            s.DohProvider = DohProvider.Cloudflare;
        s.UseCloudflareDoH = false;
        s.VpnProfiles ??= new List<VpnProfile>();
        s.AdBlockWhitelistHosts ??= new List<string>();
        s.HttpsOnlyBypassHosts ??= new List<string>();
        if (s.Version == "4.4.0" && s.ProxyMode == ProxyMode.FreeCommunity)
        {
            s.ActiveFreeServerUrl = string.Empty;
            s.ActiveFreeServerLabel = string.Empty;
            s.ActiveFreeServerCountry = string.Empty;
        }
        s.Version = "5.0.1";
        s.AutoSleepMinutes = Math.Clamp(s.AutoSleepMinutes, 1, 120);
        s.DeepUnloadMinutes = Math.Clamp(s.DeepUnloadMinutes, 2, 240);
        s.RendererProcessLimit = Math.Clamp(s.RendererProcessLimit, 1, 8);
        return s;
    }

    public void SaveSettings(BrowserSettings settings) => SaveJson(SettingsPath, settings);

    public SavedSession LoadSession() => LoadJson(SessionPath, new SavedSession());
    public void SaveSession(SavedSession session) => SaveJson(SessionPath, session);

    public List<BookmarkItem> LoadBookmarks() => LoadJson(BookmarksPath, new List<BookmarkItem>());
    public void SaveBookmarks(List<BookmarkItem> items) => SaveJson(BookmarksPath, items);

    public List<HistoryEntry> LoadHistory() => LoadJson(HistoryPath, new List<HistoryEntry>());
    public void SaveHistory(List<HistoryEntry> entries)
    {
        if (entries.Count > 2000)
            entries = entries.Take(2000).ToList();
        SaveJson(HistoryPath, entries);
    }

    public List<DownloadRecord> LoadDownloads() => LoadJson(DownloadsPath, new List<DownloadRecord>());
    public void SaveDownloads(List<DownloadRecord> records) => SaveJson(DownloadsPath, records);

    public List<QuickShortcut> LoadShortcuts() => LoadJson(ShortcutsPath, new List<QuickShortcut>());
    public void SaveShortcuts(List<QuickShortcut> items) => SaveJson(ShortcutsPath, items);

    public List<InstalledExtension> LoadExtensions() => LoadJson(ExtensionsPath, new List<InstalledExtension>());
    public void SaveExtensions(List<InstalledExtension> items) => SaveJson(ExtensionsPath, items);
}
