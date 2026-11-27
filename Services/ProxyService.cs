using System.Net.Http;
using System.Text.RegularExpressions;
using EDBrowser.Models;

namespace EDBrowser.Services;

public sealed record IpTraceInfo(string Ip, string Location, bool Success);

/// <summary>
/// Proxy & Cloudflare DoH helper matching v3.1.8 proxy-engine capabilities.
/// </summary>
public static partial class ProxyService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    [GeneratedRegex(@"ip=(.+)", RegexOptions.Compiled)]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"loc=(.+)", RegexOptions.Compiled)]
    private static partial Regex LocRegex();

    public static string BuildChromiumProxyArgs(BrowserSettings settings)
    {
        var args = new List<string>();
        switch (settings.ProxyMode)
        {
            case ProxyMode.Direct:
                args.Add("--no-proxy-server");
                break;
            case ProxyMode.Tor:
                args.Add("--proxy-server=\"socks5://127.0.0.1:9050\"");
                args.Add("--proxy-bypass-list=\"<local>\"");
                break;
            case ProxyMode.Custom when !string.IsNullOrWhiteSpace(settings.CustomProxy):
                var norm = settings.CustomProxy.Trim();
                if (!norm.Contains("://", StringComparison.Ordinal))
                    norm = "http://" + norm;
                args.Add($"--proxy-server=\"{norm}\"");
                args.Add("--proxy-bypass-list=\"localhost;127.0.0.1;<local>\"");
                break;
            case ProxyMode.System:
            default:
                break;
        }

        if (settings.UseCloudflareDoH)
        {
            args.Add("--dns-over-https-mode=secure");
            args.Add("--dns-over-https-templates=\"https://cloudflare-dns.com/dns-query\"");
        }

        return string.Join(' ', args);
    }

    public static async Task<IpTraceInfo> FetchTraceAsync()
    {
        try
        {
            var raw = await Http.GetStringAsync("https://cloudflare.com/cdn-cgi/trace");
            var ip = IpRegex().Match(raw).Groups[1].Value.Trim();
            var loc = LocRegex().Match(raw).Groups[1].Value.Trim();
            return new IpTraceInfo(
                string.IsNullOrEmpty(ip) ? "Bilinmiyor" : ip,
                loc,
                true);
        }
        catch
        {
            return new IpTraceInfo("Bağlantı hatası", "", false);
        }
    }
}
