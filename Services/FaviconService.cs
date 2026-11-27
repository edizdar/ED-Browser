using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http;

namespace EDBrowser.Services;

/// <summary>
/// Lightweight memory + disk favicon cache keyed by host. Keeps tab strips, bookmarks bar,
/// and quick shortcuts snappy without holding large bitmaps.
/// </summary>
public sealed class FaviconService : IDisposable
{
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, Image> _mem = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public event Action<string>? FaviconUpdated;

    public FaviconService(StorageService storage)
    {
        _cacheDir = storage.FaviconsDirectory;
        Directory.CreateDirectory(_cacheDir);
    }

    public Image? GetCached(string? url)
    {
        var host = ExtractHost(url);
        if (string.IsNullOrEmpty(host)) return null;

        if (_mem.TryGetValue(host, out var cached))
            return cached;

        var diskPath = GetDiskPath(host);
        if (File.Exists(diskPath))
        {
            try
            {
                using var fs = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var raw = Image.FromStream(fs);
                var copy = new Bitmap(raw);
                _mem.TryAdd(host, copy);
                return copy;
            }
            catch { }
        }
        return null;
    }

    public async Task<Image?> FetchAndCacheAsync(string? url, string? explicitFaviconUri = null)
    {
        var host = ExtractHost(url);
        if (string.IsNullOrEmpty(host)) return null;

        var existing = GetCached(url);
        if (existing is not null && string.IsNullOrWhiteSpace(explicitFaviconUri))
            return existing;

        if (!_inFlight.TryAdd(host, true))
            return existing;

        try
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitFaviconUri) &&
                explicitFaviconUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                candidates.Add(explicitFaviconUri);
            candidates.Add($"https://{host}/favicon.ico");
            candidates.Add($"https://www.google.com/s2/favicons?sz=32&domain={Uri.EscapeDataString(host)}");

            foreach (var src in candidates)
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(src).ConfigureAwait(false);
                    if (bytes is not { Length: >= 64 }) continue;

                    using var ms = new MemoryStream(bytes);
                    using var loaded = Image.FromStream(ms);
                    var normalized = ResizeToIcon(loaded, 20);
                    normalized.Save(GetDiskPath(host), ImageFormat.Png);

                    _mem.AddOrUpdate(host, normalized, (_, old) =>
                    {
                        if (!ReferenceEquals(old, normalized))
                            try { old.Dispose(); } catch { }
                        return normalized;
                    });
                    FaviconUpdated?.Invoke(host);
                    return normalized;
                }
                catch { }
            }
        }
        finally
        {
            _inFlight.TryRemove(host, out _);
        }
        return existing;
    }

    private static Bitmap ResizeToIcon(Image src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(src, new Rectangle(0, 0, size, size));
        return bmp;
    }

    private string GetDiskPath(string host) =>
        Path.Combine(_cacheDir, StorageService.SanitizeProfileId(host) + ".png");

    private static string ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host.ToLowerInvariant();
        return "";
    }

    public void Dispose()
    {
        foreach (var img in _mem.Values)
            try { img.Dispose(); } catch { }
        _mem.Clear();
    }
}
