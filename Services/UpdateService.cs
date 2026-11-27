using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace EDBrowser.Services;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? DirectDownloadUrl,
    string? Error = null);

/// <summary>
/// Checks GitHub Releases for newer ED Browser versions with automatic rate-limit fallback.
/// </summary>
public static class UpdateService
{
    private const string RepoOwner = "edizdar";
    private const string RepoName = "ED-Browser";
    private const string ReleasesLatestApi = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    private const string ReleasesLatestWeb = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, bool isInstalledMode)
    {
        var curClean = NormalizeVersionString(currentVersion);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ED-Browser/{curClean}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.GetAsync(ReleasesLatestApi);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tag = root.TryGetProperty("tag_name", out var tp) ? tp.GetString() ?? "" : "";
                var htmlUrl = root.TryGetProperty("html_url", out var hp) ? hp.GetString() ?? ReleasesLatestWeb : ReleasesLatestWeb;
                var latestClean = NormalizeVersionString(tag);

                string? directUrl = null;
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    var wantKeyword = isInstalledMode ? "Setup" : "Portable";
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                        var dl = asset.TryGetProperty("browser_download_url", out var dp) ? dp.GetString() : null;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            name.Contains(wantKeyword, StringComparison.OrdinalIgnoreCase))
                        {
                            directUrl = dl;
                            break;
                        }
                    }
                    if (directUrl is null)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                directUrl = asset.TryGetProperty("browser_download_url", out var dp) ? dp.GetString() : null;
                                break;
                            }
                        }
                    }
                }

                bool newer = IsNewer(latestClean, curClean);
                return new UpdateCheckResult(newer, curClean, latestClean, htmlUrl, directUrl);
            }
        }
        catch
        {
            // Fall through to HTML redirect fallback
        }

        return await CheckViaRedirectFallbackAsync(curClean, isInstalledMode);
    }

    private static async Task<UpdateCheckResult> CheckViaRedirectFallbackAsync(string curClean, bool isInstalledMode)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ED-Browser/{curClean}");

            using var resp = await http.GetAsync(ReleasesLatestWeb);
            if (resp.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect or HttpStatusCode.RedirectKeepVerb)
            {
                var loc = resp.Headers.Location?.ToString() ?? string.Empty;
                int tagIdx = loc.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                if (tagIdx >= 0)
                {
                    var tag = loc[(tagIdx + 5)..].Trim('/');
                    var latestClean = NormalizeVersionString(tag);
                    var releaseUrl = loc.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? loc
                        : $"https://github.com{loc}";
                    var assetName = isInstalledMode
                        ? $"ED.Browser.Setup.{latestClean}.exe"
                        : $"ED.Browser.{latestClean}.Portable.exe";
                    var directUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/{assetName}";
                    return new UpdateCheckResult(IsNewer(latestClean, curClean), curClean, latestClean, releaseUrl, directUrl);
                }
            }

            return new UpdateCheckResult(false, curClean, curClean, ReleasesLatestWeb, null, "Sürüm bilgisi alınamadı.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, curClean, curClean, ReleasesLatestWeb, null, ex.Message);
        }
    }

    public static string NormalizeVersionString(string raw)
    {
        var s = (raw ?? string.Empty).Trim().TrimStart('v', 'V');
        return string.IsNullOrEmpty(s) ? "0.0.0" : s;
    }

    public static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(NormalizeVersionString(latest), out var lv) &&
            Version.TryParse(NormalizeVersionString(current), out var cv))
        {
            return lv > cv;
        }
        return false;
    }

    public static async Task<(bool Ok, string Message)> DownloadAndApplyUpdateAsync(
        string downloadUrl,
        bool isInstalledMode,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                return (false, "İndirme bağlantısı bulunamadı.");

            var stageDir = Path.Combine(Path.GetTempPath(), "EDBrowserUpdate");
            Directory.CreateDirectory(stageDir);
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName = isInstalledMode ? "ED.Browser.Setup.Update.exe" : "ED.Browser.Portable.Update.exe";
            var dlPath = Path.Combine(stageDir, fileName);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ED-Browser-Updater/5.0");
            using var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(dlPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buf = new byte[65536];
                long readTotal = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    readTotal += n;
                    if (total > 0)
                        progress?.Report((int)Math.Clamp(readTotal * 100 / total, 0, 100));
                }
            }
            progress?.Report(100);

            if (isInstalledMode)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlPath)
                {
                    UseShellExecute = true
                });
                return (true, "Kurulum sihirbazı başlatıldı.");
            }

            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlPath) { UseShellExecute = true });
                return (true, "Yeni sürüm başlatıldı.");
            }

            var batPath = Path.Combine(stageDir, "apply_portable_update.cmd");
            var script = $@"@echo off
timeout /t 2 /nobreak >nul
copy /y ""{dlPath}"" ""{currentExe}"" >nul
start """" ""{currentExe}""
del ""{dlPath}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";
            await File.WriteAllTextAsync(batPath, script, ct).ConfigureAwait(false);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
            {
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                UseShellExecute = false
            });
            return (true, "Güncelleme uygulanıyor, tarayıcı yeniden başlatılacak…");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
