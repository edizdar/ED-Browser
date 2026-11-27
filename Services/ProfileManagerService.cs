using System.Diagnostics;

namespace EDBrowser.Services;

public sealed class BrowserProfileInfo
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Varsayılan";
    public string ColorHex { get; set; } = "#38BDF8";
}

/// <summary>
/// Manages isolated multi-user browser profiles (separate cookies, passwords, bookmarks,
/// extensions, and WebView2 user-data directories).
/// </summary>
public sealed class ProfileManagerService
{
    private readonly StorageService _storage;

    public string ProfilesConfigPath => Path.Combine(_storage.BaseDataDirectory, "profiles.json");

    public ProfileManagerService(StorageService storage)
    {
        _storage = storage;
    }

    public List<BrowserProfileInfo> LoadProfiles()
    {
        var list = _storage.LoadJson(ProfilesConfigPath, new List<BrowserProfileInfo>());
        if (list.Count == 0 || !list.Any(p => p.Id.Equals("default", StringComparison.OrdinalIgnoreCase)))
        {
            list.Insert(0, new BrowserProfileInfo
            {
                Id = "default",
                Name = "Varsayılan",
                ColorHex = "#38BDF8"
            });
            _storage.SaveJson(ProfilesConfigPath, list);
        }
        return list;
    }

    public BrowserProfileInfo GetCurrentProfile()
    {
        var list = LoadProfiles();
        return list.FirstOrDefault(p => p.Id.Equals(_storage.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
               ?? list[0];
    }

    public BrowserProfileInfo CreateProfile(string name, string colorHex = "#818CF8")
    {
        var list = LoadProfiles();
        var cleanSlug = StorageService.SanitizeProfileId(name);
        var id = $"{cleanSlug}_{Guid.NewGuid().ToString("N")[..6]}";
        var prof = new BrowserProfileInfo
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "Yeni Profil" : name.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#818CF8" : colorHex
        };
        list.Add(prof);
        _storage.SaveJson(ProfilesConfigPath, list);
        return prof;
    }

    public bool DeleteProfile(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Equals("default", StringComparison.OrdinalIgnoreCase))
            return false;

        var list = LoadProfiles();
        int removed = list.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        _storage.SaveJson(ProfilesConfigPath, list);

        try
        {
            var dir = Path.Combine(_storage.BaseDataDirectory, "Profiles", StorageService.SanitizeProfileId(id));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }

        return true;
    }

    public static void LaunchProfileWindow(string profileId)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
            var args = profileId.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"--profile \"{profileId}\"";
            Process.Start(new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
