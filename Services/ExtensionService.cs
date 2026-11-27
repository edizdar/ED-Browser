using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using EDBrowser.Models;
using Microsoft.Web.WebView2.Core;

namespace EDBrowser.Services;

/// <summary>
/// Manages Chromium browser extensions for WebView2:
/// downloads/unpacks CRX2/CRX3/ZIP packages from Chrome Web Store & Microsoft Edge Add-ons
/// or local files/folders, and syncs them with CoreWebView2Profile.
/// </summary>
public sealed class ExtensionService
{
    private static readonly Regex StoreIdRegex = new(@"\b([a-p]{32})\b", RegexOptions.Compiled);
    private readonly StorageService _storage;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public List<InstalledExtension> Items { get; }

    public ExtensionService(StorageService storage)
    {
        _storage = storage;
        Directory.CreateDirectory(_storage.ExtensionsDirectory);
        Items = _storage.LoadExtensions();
    }

    public static bool TryExtractStoreExtension(string? input, out string storeName, out string extensionId)
    {
        storeName = string.Empty;
        extensionId = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var raw = input.Trim();

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("microsoftedge.microsoft.com") && uri.AbsolutePath.Contains("/addons/detail/", StringComparison.OrdinalIgnoreCase))
            {
                var m = StoreIdRegex.Match(uri.AbsolutePath);
                if (m.Success)
                {
                    storeName = "Edge Add-ons";
                    extensionId = m.Groups[1].Value;
                    return true;
                }
            }
            if (host.Contains("chromewebstore.google.com") || (host.Contains("chrome.google.com") && uri.AbsolutePath.Contains("/webstore/detail/", StringComparison.OrdinalIgnoreCase)))
            {
                var m = StoreIdRegex.Match(uri.AbsolutePath);
                if (m.Success)
                {
                    storeName = "Chrome Web Store";
                    extensionId = m.Groups[1].Value;
                    return true;
                }
            }
            return false;
        }

        if (Regex.IsMatch(raw, "^[a-p]{32}$"))
        {
            storeName = "Chrome Web Store";
            extensionId = raw;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Unpacks raw CRX3, CRX2, or plain ZIP bytes into destinationDir safely.
    /// </summary>
    public static void UnpackCrxOrZipBytes(byte[] rawBytes, string destinationDir)
    {
        if (rawBytes is null || rawBytes.Length < 16)
            throw new InvalidDataException("Geçersiz veya boş eklenti paketi.");

        int zipOffset = 0;

        // Check Cr24 magic ("Cr24" = 0x43, 0x72, 0x32, 0x34)
        if (rawBytes[0] == 0x43 && rawBytes[1] == 0x72 && rawBytes[2] == 0x32 && rawBytes[3] == 0x34)
        {
            uint version = BitConverter.ToUInt32(rawBytes, 4);
            if (version == 3)
            {
                uint headerLen = BitConverter.ToUInt32(rawBytes, 8);
                zipOffset = checked((int)(12 + headerLen));
            }
            else if (version == 2)
            {
                uint pubLen = BitConverter.ToUInt32(rawBytes, 8);
                uint sigLen = BitConverter.ToUInt32(rawBytes, 12);
                zipOffset = checked((int)(16 + pubLen + sigLen));
            }
        }

        if (zipOffset <= 0 || zipOffset + 4 > rawBytes.Length ||
            rawBytes[zipOffset] != 0x50 || rawBytes[zipOffset + 1] != 0x4B)
        {
            // Fallback: scan for local file header PK\x03\x04
            zipOffset = -1;
            for (int i = 0; i <= rawBytes.Length - 4; i++)
            {
                if (rawBytes[i] == 0x50 && rawBytes[i + 1] == 0x4B && rawBytes[i + 2] == 0x03 && rawBytes[i + 3] == 0x04)
                {
                    zipOffset = i;
                    break;
                }
            }
            if (zipOffset < 0)
                throw new InvalidDataException("Eklenti paketinin içinde geçerli ZIP verisi bulunamadı.");
        }

        if (Directory.Exists(destinationDir))
            Directory.Delete(destinationDir, recursive: true);
        Directory.CreateDirectory(destinationDir);

        string destFull = Path.GetFullPath(destinationDir);
        using var ms = new MemoryStream(rawBytes, zipOffset, rawBytes.Length - zipOffset, writable: false);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var outPath = Path.GetFullPath(Path.Combine(destFull, rel));
            if (!outPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                continue;

            var parent = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(outPath, overwrite: true);
        }

        if (!File.Exists(Path.Combine(destFull, "manifest.json")))
            throw new InvalidDataException("Eklenti paketinde manifest.json bulunamadı.");
    }

    public async Task<InstalledExtension> InstallFromStoreUrlOrIdAsync(string input, CoreWebView2Profile? profile)
    {
        if (!TryExtractStoreExtension(input, out var storeName, out var extId))
            throw new ArgumentException("Geçerli bir Chrome Web Store / Edge Add-ons URL'si veya 32 karakterli eklenti ID'si girin.");

        string downloadUrl = storeName == "Edge Add-ons"
            ? $"https://edge.microsoft.com/extensionwebstorebase/v1/crx?response=redirect&x=id%3D{extId}%26installsource%3Dondemand%26uc"
            : $"https://clients2.google.com/service/update2/crx?response=redirect&prodversion=134.0.6998.89&acceptformat=crx2,crx3&x=id%3D{extId}%26uc";

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");

        var bytes = await http.GetByteArrayAsync(downloadUrl);
        if (bytes.Length < 100)
            throw new InvalidOperationException("Mağazadan geçerli bir eklenti paketi alınamadı.");

        var folder = Path.Combine(_storage.ExtensionsDirectory, extId);
        UnpackCrxOrZipBytes(bytes, folder);

        return await RegisterFromDirectoryAsync(folder, storeName, extId, profile);
    }

    public async Task<InstalledExtension> InstallFromCrxOrZipFileAsync(string filePath, CoreWebView2Profile? profile)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        var slug = Path.GetFileNameWithoutExtension(filePath);
        var clean = Regex.Replace(slug, @"[^a-zA-Z0-9_-]", "_");
        if (string.IsNullOrWhiteSpace(clean)) clean = "ext";
        var folder = Path.Combine(_storage.ExtensionsDirectory, $"{clean}_{Guid.NewGuid():N}"[..Math.Min(32, clean.Length + 9)]);
        UnpackCrxOrZipBytes(bytes, folder);

        return await RegisterFromDirectoryAsync(folder, "Yerel CRX/ZIP", string.Empty, profile);
    }

    public async Task<InstalledExtension> InstallFromUnpackedFolderAsync(string sourceFolder, CoreWebView2Profile? profile)
    {
        if (!File.Exists(Path.Combine(sourceFolder, "manifest.json")))
            throw new FileNotFoundException("Seçilen klasörde manifest.json bulunamadı.");

        var folderName = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var clean = Regex.Replace(folderName, @"[^a-zA-Z0-9_-]", "_");
        if (string.IsNullOrWhiteSpace(clean)) clean = "local_ext";
        var dest = Path.Combine(_storage.ExtensionsDirectory, $"{clean}_{Guid.NewGuid().ToString("N")[..8]}");
        CopyDirectory(sourceFolder, dest);

        return await RegisterFromDirectoryAsync(dest, "Açık Klasör", string.Empty, profile);
    }

    private async Task<InstalledExtension> RegisterFromDirectoryAsync(string folder, string sourceStore, string storeId, CoreWebView2Profile? profile)
    {
        var meta = ReadManifestMetadata(folder);
        var existing = !string.IsNullOrEmpty(storeId)
            ? Items.FirstOrDefault(x => x.StoreId.Equals(storeId, StringComparison.OrdinalIgnoreCase))
            : null;

        if (existing is not null)
        {
            existing.Name = meta.Name;
            existing.Version = meta.Version;
            existing.Description = meta.Description;
            existing.FolderPath = folder;
            existing.SourceStore = sourceStore;
            existing.PopupPath = meta.PopupPath;
            existing.OptionsPath = meta.OptionsPath;
            existing.IconPath = meta.IconPath;
            existing.Enabled = true;
            _storage.SaveExtensions(Items);
            if (profile is not null)
                await SyncWithProfileAsync(profile);
            return existing;
        }

        var item = new InstalledExtension
        {
            StoreId = storeId,
            Name = meta.Name,
            Version = meta.Version,
            Description = meta.Description,
            FolderPath = folder,
            SourceStore = sourceStore,
            PopupPath = meta.PopupPath,
            OptionsPath = meta.OptionsPath,
            IconPath = meta.IconPath,
            Enabled = true
        };
        Items.Insert(0, item);
        _storage.SaveExtensions(Items);

        if (profile is not null)
            await SyncWithProfileAsync(profile);

        return item;
    }

    public async Task SyncWithProfileAsync(CoreWebView2Profile profile)
    {
        await _syncLock.WaitAsync();
        try
        {
            var loaded = await profile.GetBrowserExtensionsAsync();
            var byId = loaded.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var byName = loaded.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            bool dirty = false;
            foreach (var ext in Items.ToList())
            {
                if (!Directory.Exists(ext.FolderPath) || !File.Exists(Path.Combine(ext.FolderPath, "manifest.json")))
                    continue;

                CoreWebView2BrowserExtension? runtimeExt = null;
                if (!string.IsNullOrEmpty(ext.RuntimeId) && byId.TryGetValue(ext.RuntimeId, out var foundById))
                    runtimeExt = foundById;
                else if (byName.TryGetValue(ext.Name, out var foundByName))
                    runtimeExt = foundByName;

                if (runtimeExt is null)
                {
                    try
                    {
                        runtimeExt = await profile.AddBrowserExtensionAsync(ext.FolderPath);
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (runtimeExt is not null)
                {
                    if (ext.RuntimeId != runtimeExt.Id)
                    {
                        ext.RuntimeId = runtimeExt.Id;
                        dirty = true;
                    }
                    if (runtimeExt.IsEnabled != ext.Enabled)
                        await runtimeExt.EnableAsync(ext.Enabled);
                }
            }

            if (dirty)
                _storage.SaveExtensions(Items);
        }
        catch { }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task ToggleExtensionAsync(CoreWebView2Profile? profile, InstalledExtension ext, bool enabled)
    {
        ext.Enabled = enabled;
        _storage.SaveExtensions(Items);

        if (profile is null) return;
        try
        {
            var loaded = await profile.GetBrowserExtensionsAsync();
            var match = loaded.FirstOrDefault(x =>
                (!string.IsNullOrEmpty(ext.RuntimeId) && x.Id.Equals(ext.RuntimeId, StringComparison.OrdinalIgnoreCase)) ||
                x.Name.Equals(ext.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                await match.EnableAsync(enabled);
            else if (enabled && Directory.Exists(ext.FolderPath))
            {
                var added = await profile.AddBrowserExtensionAsync(ext.FolderPath);
                ext.RuntimeId = added.Id;
                _storage.SaveExtensions(Items);
            }
        }
        catch { }
    }

    public async Task RemoveExtensionAsync(CoreWebView2Profile? profile, InstalledExtension ext)
    {
        if (profile is not null)
        {
            try
            {
                var loaded = await profile.GetBrowserExtensionsAsync();
                var match = loaded.FirstOrDefault(x =>
                    (!string.IsNullOrEmpty(ext.RuntimeId) && x.Id.Equals(ext.RuntimeId, StringComparison.OrdinalIgnoreCase)) ||
                    x.Name.Equals(ext.Name, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    await match.RemoveAsync();
            }
            catch { }
        }

        Items.Remove(ext);
        _storage.SaveExtensions(Items);

        try
        {
            if (Directory.Exists(ext.FolderPath))
                Directory.Delete(ext.FolderPath, recursive: true);
        }
        catch { }
    }

    private static (string Name, string Version, string Description, string PopupPath, string OptionsPath, string IconPath) ReadManifestMetadata(string folder)
    {
        var manifestPath = Path.Combine(folder, "manifest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        string rawName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Eklenti" : "Eklenti";
        string version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0";
        string rawDesc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        string defaultLocale = root.TryGetProperty("default_locale", out var dl) ? dl.GetString() ?? "en" : "en";

        string popup = string.Empty;
        if (root.TryGetProperty("action", out var act) && act.TryGetProperty("default_popup", out var dp))
            popup = dp.GetString() ?? "";
        else if (root.TryGetProperty("browser_action", out var bact) && bact.TryGetProperty("default_popup", out var bdp))
            popup = bdp.GetString() ?? "";

        string options = root.TryGetProperty("options_page", out var op) ? op.GetString() ?? ""
            : (root.TryGetProperty("options_ui", out var oui) && oui.TryGetProperty("page", out var oup)) ? oup.GetString() ?? "" : "";

        string iconPath = string.Empty;
        if (root.TryGetProperty("icons", out var iconsEl) && iconsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var sz in new[] { "32", "48", "16", "64", "128" })
            {
                if (iconsEl.TryGetProperty(sz, out var ip) && ip.GetString() is { Length: > 0 } rel)
                {
                    var full = Path.Combine(folder, rel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(full)) { iconPath = full; break; }
                }
            }
        }

        return (
            ResolveLocaleMsg(folder, rawName, defaultLocale),
            version,
            ResolveLocaleMsg(folder, rawDesc, defaultLocale),
            popup.TrimStart('/'),
            options.TrimStart('/'),
            iconPath
        );
    }

    private static string ResolveLocaleMsg(string folder, string raw, string defaultLocale)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("__MSG_", StringComparison.Ordinal) || !raw.EndsWith("__", StringComparison.Ordinal))
            return raw;

        var key = raw[6..^2];
        foreach (var loc in new[] { "tr", defaultLocale, "en", "en_US" })
        {
            if (string.IsNullOrWhiteSpace(loc)) continue;
            var p = Path.Combine(folder, "_locales", loc, "messages.json");
            if (!File.Exists(p)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.TryGetProperty("message", out var msg))
                    {
                        var val = msg.GetString();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
            catch { }
        }
        return raw;
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
