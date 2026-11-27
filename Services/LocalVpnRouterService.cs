using System.Net;
using System.Net.Sockets;
using System.Text;
using EDBrowser.Models;

namespace EDBrowser.Services;

/// <summary>
/// In-process local HTTP/HTTPS CONNECT &amp; plain-HTTP proxy router bound to 127.0.0.1:&lt;ephemeral&gt;.
/// WebView2 points to this router once at startup; toggling VPN ON/OFF or switching tunnels takes
/// effect on the very next connection with zero browser restart. Includes per-handshake timeouts
/// and automatic multi-relay failover for FreeCommunity relays.
/// </summary>
public sealed class LocalVpnRouterService : IDisposable
{
    private enum RouteKind
    {
        Direct = 0,
        Socks5 = 1,
        HttpProxy = 2
    }

    private sealed record RouteConfig(
        RouteKind Kind,
        string UpstreamHost,
        int UpstreamPort,
        string Username,
        string Password,
        string Summary,
        bool IsFreeCommunity = false,
        FreeVpnServer? FreeServer = null);

    private readonly object _lock = new();
    private readonly List<(FreeVpnServer Server, RouteConfig Route)> _failoverPool = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private volatile RouteConfig _route = new(RouteKind.Direct, "", 0, "", "", "Doğrudan (VPN Kapalı)");

    public event Action<FreeVpnServer>? FailoverServerPromoted;

    public int ListenPort { get; private set; }
    public bool IsRunning => ListenPort > 0 && _cts is { IsCancellationRequested: false };
    public string CurrentRouteSummary => _route.Summary;
    public bool HasFailoverRoutes
    {
        get { lock (_lock) return _failoverPool.Count > 0; }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_listener is not null) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(128);
            ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
        }
    }

    public void SetFreeFailoverPool(IEnumerable<FreeVpnServer> servers)
    {
        var items = new List<(FreeVpnServer, RouteConfig)>();
        foreach (var srv in servers)
        {
            if (TryBuildRouteFromUrl(srv.ProxyUrl, "", "", srv.DisplayText, isFree: true, srv, out var cfg))
                items.Add((srv, cfg));
        }

        lock (_lock)
        {
            _failoverPool.Clear();
            _failoverPool.AddRange(items);
        }
    }

    public void ApplySettings(BrowserSettings s)
    {
        if (!s.VpnEnabled)
        {
            if (s.ProxyMode == ProxyMode.System)
            {
                try
                {
                    var probe = new Uri("https://example.com");
                    var sysUri = WebRequest.DefaultWebProxy?.GetProxy(probe);
                    if (sysUri is not null && !sysUri.Host.Equals(probe.Host, StringComparison.OrdinalIgnoreCase) && sysUri.Port > 0)
                    {
                        _route = new RouteConfig(RouteKind.HttpProxy, sysUri.Host, sysUri.Port, "", "", $"Sistem Proxy ({sysUri.Host}:{sysUri.Port})");
                        return;
                    }
                }
                catch { }
            }
            _route = new RouteConfig(RouteKind.Direct, "", 0, "", "", "Doğrudan (VPN Kapalı)");
            return;
        }

        var effUrl = ProxyService.ResolveEffectiveProxyUrl(s, ignoreVpnEnabledFlag: true);
        if (string.IsNullOrWhiteSpace(effUrl))
        {
            _route = new RouteConfig(RouteKind.Direct, "", 0, "", "", "Sunucu bekleniyor (Doğrudan)");
            return;
        }

        string user = "";
        string pass = "";
        if (s.ProxyMode == ProxyMode.Custom)
        {
            var prof = ProxyService.GetActiveVpnProfile(s);
            if (prof is not null && !string.IsNullOrEmpty(prof.Username))
            {
                user = prof.Username;
                pass = prof.Password;
            }
        }

        FreeVpnServer? freeSrv = s.ProxyMode == ProxyMode.FreeCommunity
            ? new FreeVpnServer(
                effUrl,
                effUrl.StartsWith("socks", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http",
                string.IsNullOrEmpty(s.ActiveFreeServerCountry) ? "GL" : s.ActiveFreeServerCountry,
                ProxyService.MapCountryNameTr(s.ActiveFreeServerCountry),
                0,
                "")
            : null;

        string summaryHint = s.ProxyMode switch
        {
            ProxyMode.CloudflareWarp => "Cloudflare WARP (SOCKS5 :40000)",
            ProxyMode.Tor => "Tor Ağı (SOCKS5)",
            ProxyMode.FreeCommunity => string.IsNullOrEmpty(s.ActiveFreeServerLabel) ? $"Ücretsiz VPN ({effUrl})" : s.ActiveFreeServerLabel,
            _ => effUrl
        };

        if (TryBuildRouteFromUrl(effUrl, user, pass, summaryHint, s.ProxyMode == ProxyMode.FreeCommunity, freeSrv, out var cfg))
            _route = cfg;
        else
            _route = new RouteConfig(RouteKind.Direct, "", 0, "", "", "Sunucu bekleniyor (Doğrudan)");
    }

    private static bool TryBuildRouteFromUrl(
        string rawUrl,
        string fallbackUser,
        string fallbackPass,
        string summary,
        bool isFree,
        FreeVpnServer? freeSrv,
        out RouteConfig config)
    {
        config = new RouteConfig(RouteKind.Direct, "", 0, "", "", "Doğrudan");
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return false;

        int port = uri.Port > 0 ? uri.Port : (uri.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase) ? 1080 : 8080);
        string user = fallbackUser;
        string pass = fallbackPass;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            int colon = uri.UserInfo.IndexOf(':');
            if (colon >= 0)
            {
                user = Uri.UnescapeDataString(uri.UserInfo[..colon]);
                pass = Uri.UnescapeDataString(uri.UserInfo[(colon + 1)..]);
            }
            else
            {
                user = Uri.UnescapeDataString(uri.UserInfo);
            }
        }

        bool isSocks5 = uri.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase) ||
                        uri.Scheme.Equals("socks5h", StringComparison.OrdinalIgnoreCase);

        config = new RouteConfig(
            isSocks5 ? RouteKind.Socks5 : RouteKind.HttpProxy,
            uri.Host,
            port,
            user,
            pass,
            summary,
            isFree,
            freeSrv);
        return true;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var clientStream = client.GetStream();
                var (headerBytes, bodyPrefix) = await ReadHttpHeaderBlockAsync(clientStream, ct).ConfigureAwait(false);
                if (headerBytes.Length == 0)
                    return;

                var headerText = Encoding.ASCII.GetString(headerBytes);
                int firstLineEnd = headerText.IndexOf("\r\n", StringComparison.Ordinal);
                if (firstLineEnd < 0)
                    return;

                var requestLine = headerText[..firstLineEnd];
                var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    return;

                var method = parts[0];
                var target = parts[1];
                var version = parts[2];
                var route = _route;

                if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseHostPort(target, 443, out var host, out var port))
                    {
                        await WriteErrorAsync(clientStream, "400 Bad Request", ct).ConfigureAwait(false);
                        return;
                    }

                    TcpClient? upstream = null;
                    try
                    {
                        upstream = await OpenTunnelWithFailoverAsync(host, port, route, ct).ConfigureAwait(false);
                        using (upstream)
                        {
                            upstream.NoDelay = true;
                            using var upStream = upstream.GetStream();
                            var okResp = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\nProxy-Agent: ED-Browser-Router/4.4.1\r\n\r\n");
                            await clientStream.WriteAsync(okResp, ct).ConfigureAwait(false);
                            await clientStream.FlushAsync(ct).ConfigureAwait(false);

                            if (bodyPrefix.Length > 0)
                            {
                                await upStream.WriteAsync(bodyPrefix, ct).ConfigureAwait(false);
                                await upStream.FlushAsync(ct).ConfigureAwait(false);
                            }

                            await PumpBidirectionalAsync(clientStream, upStream, ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        upstream?.Dispose();
                        await WriteErrorAsync(clientStream, "502 Bad Gateway", ct).ConfigureAwait(false);
                    }
                    return;
                }

                // Plain HTTP request (GET http://host/path HTTP/1.1 etc.)
                if (!TryParseHttpTarget(target, headerText[(firstLineEnd + 2)..], out var targetHost, out var targetPort, out var pathAndQuery))
                {
                    await WriteErrorAsync(clientStream, "400 Bad Request", ct).ConfigureAwait(false);
                    return;
                }

                if (route.Kind == RouteKind.HttpProxy)
                {
                    TcpClient? upstream = null;
                    try
                    {
                        upstream = await ConnectWithTimeoutAsync(route.UpstreamHost, route.UpstreamPort, 5000, ct).ConfigureAwait(false);
                        using (upstream)
                        {
                            upstream.NoDelay = true;
                            using var upStream = upstream.GetStream();
                            var forwarded = InjectProxyAuthIfNeeded(headerBytes, route);
                            await upStream.WriteAsync(forwarded, ct).ConfigureAwait(false);
                            if (bodyPrefix.Length > 0)
                                await upStream.WriteAsync(bodyPrefix, ct).ConfigureAwait(false);
                            await upStream.FlushAsync(ct).ConfigureAwait(false);
                            await PumpBidirectionalAsync(clientStream, upStream, ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        upstream?.Dispose();
                        if (route.IsFreeCommunity)
                        {
                            try
                            {
                                upstream = await OpenTunnelWithFailoverAsync(targetHost, targetPort, route, ct, skipPrimary: true).ConfigureAwait(false);
                                using (upstream)
                                {
                                    upstream.NoDelay = true;
                                    using var upStream = upstream.GetStream();
                                    var rewritten = RewriteToOriginHttpRequest(method, pathAndQuery, version, headerText[(firstLineEnd + 2)..]);
                                    await upStream.WriteAsync(rewritten, ct).ConfigureAwait(false);
                                    if (bodyPrefix.Length > 0)
                                        await upStream.WriteAsync(bodyPrefix, ct).ConfigureAwait(false);
                                    await upStream.FlushAsync(ct).ConfigureAwait(false);
                                    await PumpBidirectionalAsync(clientStream, upStream, ct).ConfigureAwait(false);
                                }
                                return;
                            }
                            catch
                            {
                                upstream?.Dispose();
                            }
                        }
                        await WriteErrorAsync(clientStream, "502 Bad Gateway", ct).ConfigureAwait(false);
                    }
                    return;
                }

                // Direct or SOCKS5 tunnel for plain HTTP
                TcpClient? directOrSocks = null;
                try
                {
                    directOrSocks = await OpenTunnelWithFailoverAsync(targetHost, targetPort, route, ct).ConfigureAwait(false);
                    using (directOrSocks)
                    {
                        directOrSocks.NoDelay = true;
                        using var upStream = directOrSocks.GetStream();
                        var rewritten = RewriteToOriginHttpRequest(method, pathAndQuery, version, headerText[(firstLineEnd + 2)..]);
                        await upStream.WriteAsync(rewritten, ct).ConfigureAwait(false);
                        if (bodyPrefix.Length > 0)
                            await upStream.WriteAsync(bodyPrefix, ct).ConfigureAwait(false);
                        await upStream.FlushAsync(ct).ConfigureAwait(false);
                        await PumpBidirectionalAsync(clientStream, upStream, ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                    directOrSocks?.Dispose();
                    await WriteErrorAsync(clientStream, "502 Bad Gateway", ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // Client disconnected or aborted
            }
        }
    }

    private async Task<TcpClient> OpenTunnelWithFailoverAsync(
        string targetHost,
        int targetPort,
        RouteConfig primary,
        CancellationToken ct,
        bool skipPrimary = false)
    {
        if (!skipPrimary)
        {
            try
            {
                using var hsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                hsCts.CancelAfter(5500);
                return await OpenTunnelAsync(targetHost, targetPort, primary, hsCts.Token).ConfigureAwait(false);
            }
            catch when (primary.IsFreeCommunity && !ct.IsCancellationRequested)
            {
                // Fall through to backup relays
            }
        }

        List<(FreeVpnServer Server, RouteConfig Route)> backups;
        lock (_lock)
        {
            backups = _failoverPool
                .Where(x => !(x.Route.UpstreamHost.Equals(primary.UpstreamHost, StringComparison.OrdinalIgnoreCase) &&
                              x.Route.UpstreamPort == primary.UpstreamPort))
                .Take(3)
                .ToList();
        }

        Exception? lastErr = null;
        foreach (var (srv, cfg) in backups)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var hsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                hsCts.CancelAfter(4500);
                var client = await OpenTunnelAsync(targetHost, targetPort, cfg, hsCts.Token).ConfigureAwait(false);

                // Promote working relay so subsequent connections don't waste time hitting dead primary
                bool promoted = false;
                lock (_lock)
                {
                    if (_route.UpstreamHost.Equals(primary.UpstreamHost, StringComparison.OrdinalIgnoreCase) &&
                        _route.UpstreamPort == primary.UpstreamPort)
                    {
                        _route = cfg;
                        promoted = true;
                        int idx = _failoverPool.FindIndex(x =>
                            x.Route.UpstreamHost.Equals(primary.UpstreamHost, StringComparison.OrdinalIgnoreCase) &&
                            x.Route.UpstreamPort == primary.UpstreamPort);
                        if (idx >= 0)
                        {
                            var failedEntry = _failoverPool[idx];
                            _failoverPool.RemoveAt(idx);
                            _failoverPool.Add(failedEntry);
                        }
                    }
                }
                if (promoted)
                    FailoverServerPromoted?.Invoke(srv);

                return client;
            }
            catch (Exception ex)
            {
                lastErr = ex;
            }
        }

        throw lastErr ?? new IOException("All free VPN relays failed to tunnel target host.");
    }

    private static async Task<TcpClient> OpenTunnelAsync(string targetHost, int targetPort, RouteConfig route, CancellationToken ct)
    {
        if (route.Kind == RouteKind.Direct)
            return await ConnectWithTimeoutAsync(targetHost, targetPort, 8000, ct).ConfigureAwait(false);

        if (route.Kind == RouteKind.HttpProxy)
        {
            var client = await ConnectWithTimeoutAsync(route.UpstreamHost, route.UpstreamPort, 4000, ct).ConfigureAwait(false);
            try
            {
                var stream = client.GetStream();
                var sb = new StringBuilder();
                sb.Append($"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\nProxy-Connection: keep-alive\r\n");
                if (!string.IsNullOrEmpty(route.Username))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{route.Username}:{route.Password}"));
                    sb.Append($"Proxy-Authorization: Basic {creds}\r\n");
                }
                sb.Append("\r\n");

                await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                var (respHeaders, _) = await ReadHttpHeaderBlockAsync(stream, ct).ConfigureAwait(false);
                if (respHeaders.Length == 0)
                    throw new IOException("Upstream proxy closed connection during CONNECT");
                var statusLine = Encoding.ASCII.GetString(respHeaders).Split("\r\n", 2)[0];
                if (!statusLine.Contains(" 200", StringComparison.Ordinal))
                    throw new IOException("Upstream proxy CONNECT failed: " + statusLine);

                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        // SOCKS5 (RFC 1928 + optional RFC 1929 auth) with remote domain resolution (ATYP = 0x03)
        var socks = await ConnectWithTimeoutAsync(route.UpstreamHost, route.UpstreamPort, 4000, ct).ConfigureAwait(false);
        try
        {
            var stream = socks.GetStream();
            bool hasCreds = !string.IsNullOrEmpty(route.Username);
            byte[] greeting = hasCreds ? new byte[] { 0x05, 0x02, 0x00, 0x02 } : new byte[] { 0x05, 0x01, 0x00 };
            await stream.WriteAsync(greeting, ct).ConfigureAwait(false);

            var methodResp = new byte[2];
            await ReadExactAsync(stream, methodResp, ct).ConfigureAwait(false);
            if (methodResp[0] != 0x05 || methodResp[1] == 0xFF)
                throw new IOException("SOCKS5 greeting rejected");

            if (methodResp[1] == 0x02)
            {
                if (!hasCreds) throw new IOException("SOCKS5 requires credentials");
                var uBytes = Encoding.UTF8.GetBytes(route.Username);
                var pBytes = Encoding.UTF8.GetBytes(route.Password);
                if (uBytes.Length > 255 || pBytes.Length > 255)
                    throw new IOException("SOCKS5 credentials too long");

                var authBuf = new byte[3 + uBytes.Length + pBytes.Length];
                authBuf[0] = 0x01;
                authBuf[1] = (byte)uBytes.Length;
                Buffer.BlockCopy(uBytes, 0, authBuf, 2, uBytes.Length);
                authBuf[2 + uBytes.Length] = (byte)pBytes.Length;
                Buffer.BlockCopy(pBytes, 0, authBuf, 3 + uBytes.Length, pBytes.Length);
                await stream.WriteAsync(authBuf, ct).ConfigureAwait(false);

                var authResp = new byte[2];
                await ReadExactAsync(stream, authResp, ct).ConfigureAwait(false);
                if (authResp[1] != 0x00)
                    throw new IOException("SOCKS5 authentication failed");
            }

            byte[] req;
            if (IPAddress.TryParse(targetHost, out var ipAddr) && ipAddr.AddressFamily == AddressFamily.InterNetwork)
            {
                var ipBytes = ipAddr.GetAddressBytes();
                req = new byte[10];
                req[0] = 0x05; req[1] = 0x01; req[2] = 0x00; req[3] = 0x01;
                Buffer.BlockCopy(ipBytes, 0, req, 4, 4);
                req[8] = (byte)(targetPort >> 8);
                req[9] = (byte)(targetPort & 0xFF);
            }
            else
            {
                var hostBytes = Encoding.ASCII.GetBytes(targetHost);
                if (hostBytes.Length is 0 or > 255)
                    throw new IOException("Invalid SOCKS5 target host");
                req = new byte[7 + hostBytes.Length];
                req[0] = 0x05; req[1] = 0x01; req[2] = 0x00; req[3] = 0x03;
                req[4] = (byte)hostBytes.Length;
                Buffer.BlockCopy(hostBytes, 0, req, 5, hostBytes.Length);
                req[5 + hostBytes.Length] = (byte)(targetPort >> 8);
                req[6 + hostBytes.Length] = (byte)(targetPort & 0xFF);
            }

            await stream.WriteAsync(req, ct).ConfigureAwait(false);
            var repHead = new byte[4];
            await ReadExactAsync(stream, repHead, ct).ConfigureAwait(false);
            if (repHead[0] != 0x05 || repHead[1] != 0x00)
                throw new IOException($"SOCKS5 CONNECT failed code {repHead[1]}");

            int tailLen = repHead[3] switch
            {
                0x01 => 6,
                0x04 => 18,
                0x03 => await ReadByteAsync(stream, ct).ConfigureAwait(false) + 2,
                _ => throw new IOException("Unknown SOCKS5 ATYP")
            };
            var sink = new byte[tailLen];
            await ReadExactAsync(stream, sink, ct).ConfigureAwait(false);
            return socks;
        }
        catch
        {
            socks.Dispose();
            throw;
        }
    }

    private static async Task<TcpClient> ConnectWithTimeoutAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        var client = new TcpClient();
        using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkCts.CancelAfter(timeoutMs);
        try
        {
            await client.ConnectAsync(host, port, linkCts.Token).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task PumpBidirectionalAsync(NetworkStream a, NetworkStream b, CancellationToken ct)
    {
        using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t1 = CopyStreamSafeAsync(a, b, closeCts.Token);
        var t2 = CopyStreamSafeAsync(b, a, closeCts.Token);
        await Task.WhenAny(t1, t2).ConfigureAwait(false);
        closeCts.Cancel();
        try { await Task.WhenAll(t1, t2).ConfigureAwait(false); } catch { }
    }

    private static async Task CopyStreamSafeAsync(Stream src, Stream dst, CancellationToken ct)
    {
        var buf = new byte[32768];
        try
        {
            while (true)
            {
                int read = await src.ReadAsync(buf, ct).ConfigureAwait(false);
                if (read <= 0) break;
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                await dst.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch { }
    }

    private static async Task<(byte[] Headers, byte[] BodyPrefix)> ReadHttpHeaderBlockAsync(NetworkStream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream(1024);
        var buf = new byte[4096];
        while (ms.Length < 65536)
        {
            int n = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n <= 0) break;
            ms.Write(buf, 0, n);
            var span = ms.GetBuffer().AsSpan(0, (int)ms.Length);
            int idx = span.IndexOf("\r\n\r\n"u8);
            if (idx >= 0)
            {
                int headerEnd = idx + 4;
                return (span[..headerEnd].ToArray(), span[headerEnd..].ToArray());
            }
        }
        return (Array.Empty<byte>(), Array.Empty<byte>());
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(offset, buf.Length - offset), ct).ConfigureAwait(false);
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
        }
    }

    private static async Task<int> ReadByteAsync(NetworkStream stream, CancellationToken ct)
    {
        var b = new byte[1];
        await ReadExactAsync(stream, b, ct).ConfigureAwait(false);
        return b[0];
    }

    private static bool TryParseHostPort(string raw, int defaultPort, out string host, out int port)
    {
        host = "";
        port = defaultPort;
        raw = raw.Trim();
        if (raw.StartsWith('[') && raw.Contains(']'))
        {
            int close = raw.IndexOf(']');
            host = raw[1..close];
            if (close + 1 < raw.Length && raw[close + 1] == ':' && int.TryParse(raw[(close + 2)..], out int p6))
                port = p6;
            return !string.IsNullOrWhiteSpace(host);
        }
        int lastColon = raw.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(raw[(lastColon + 1)..], out int p))
        {
            host = raw[..lastColon];
            port = p;
        }
        else
        {
            host = raw;
        }
        return !string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535;
    }

    private static bool TryParseHttpTarget(string target, string headersRest, out string host, out int port, out string pathAndQuery)
    {
        host = "";
        port = 80;
        pathAndQuery = "/";

        if (Uri.TryCreate(target, UriKind.Absolute, out var abs))
        {
            host = abs.Host;
            port = abs.Port > 0 ? abs.Port : 80;
            pathAndQuery = string.IsNullOrEmpty(abs.PathAndQuery) ? "/" : abs.PathAndQuery;
            return !string.IsNullOrWhiteSpace(host);
        }

        pathAndQuery = target.StartsWith('/') ? target : "/" + target;
        foreach (var line in headersRest.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                return TryParseHostPort(line[5..], 80, out host, out port);
        }
        return false;
    }

    private static byte[] RewriteToOriginHttpRequest(string method, string pathAndQuery, string version, string headersRest)
    {
        var sb = new StringBuilder();
        sb.Append($"{method} {pathAndQuery} {version}\r\n");
        foreach (var line in headersRest.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.Append(line).Append("\r\n");
        }
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] InjectProxyAuthIfNeeded(byte[] headerBytes, RouteConfig route)
    {
        if (string.IsNullOrEmpty(route.Username))
            return headerBytes;
        var text = Encoding.ASCII.GetString(headerBytes);
        if (text.Contains("\r\nProxy-Authorization:", StringComparison.OrdinalIgnoreCase))
            return headerBytes;
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{route.Username}:{route.Password}"));
        var injected = text[..^2] + $"Proxy-Authorization: Basic {creds}\r\n\r\n";
        return Encoding.ASCII.GetBytes(injected);
    }

    private static async Task WriteErrorAsync(NetworkStream stream, string status, CancellationToken ct)
    {
        try
        {
            var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _cts?.Dispose();
            _listener = null;
            _cts = null;
            ListenPort = 0;
        }
    }
}
