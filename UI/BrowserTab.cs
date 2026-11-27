using System.Net.Http;
using System.Text.Json;
using EDBrowser.Models;
using EDBrowser.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EDBrowser.UI;

public sealed class BrowserTab : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; private set; } = "Yeni Sekme";
    public string Url { get; private set; } = string.Empty;
    public bool IsLoading { get; private set; }
    public bool IsSleeping { get; private set; }
    public bool IsDeepUnloaded { get; private set; }
    public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;
    public int BlockedOnTab { get; private set; }
    public double ZoomFactor { get; set; } = 1.0;
    public bool IsPinned { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public Color GroupColor { get; set; } = Color.Empty;
    public bool IsReaderMode { get; private set; }
    public string FaviconUri { get; private set; } = string.Empty;
    public bool IsMuted => WebView?.CoreWebView2?.IsMuted ?? false;
    public bool IsPlayingAudio => WebView?.CoreWebView2?.IsDocumentPlayingAudio ?? false;
    public bool CanGoBack => WebView?.CoreWebView2?.CanGoBack ?? false;
    public bool CanGoForward => WebView?.CoreWebView2?.CanGoForward ?? false;

    public WebView2? WebView { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool HasNavigated => !string.IsNullOrEmpty(Url);

    private readonly BrowserContext _ctx;
    private readonly Control _hostContainer;
    private readonly bool _incognito;
    private readonly string? _incognitoDir;
    private string? _pendingUrl;
    private string _topHost = string.Empty;
    private readonly HashSet<string> _httpsFailedHosts = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastUpgradedFromHttpUrl;
    private bool _disposed;

    public event Action<BrowserTab>? StateChanged;
    public event Action<BrowserTab, string>? OpenNewTabRequested;
    public event Action<BrowserTab, string>? UrlNavigated;
    public event Action<BrowserTab, DownloadRecord>? DownloadStarted;
    public event Action<BrowserTab, DownloadRecord>? DownloadUpdated;
    public event Action<BrowserTab, DownloadRecord>? DownloadCompleted;
    public event Action<BrowserTab, string>? PasswordOfferRequested;
    public event Action<Keys>? AcceleratorForwarded;
    public event Action? FindRequested;
    public event Action? BookmarkRequested;
    public event Action<BrowserTab, double>? ZoomChanged;
    public event Action<int, int>? InspectRequested;
    public event Action? ViewSourceRequested;
    public event Action<string>? DevToolsElementPicked;

    public BrowserTab(
        BrowserContext ctx,
        Control hostContainer,
        bool incognito,
        string? incognitoDir)
    {
        _ctx = ctx;
        _hostContainer = hostContainer;
        _incognito = incognito;
        _incognitoDir = incognitoDir;
    }

    public async Task NavigateAsync(string url)
    {
        if (_disposed || string.IsNullOrWhiteSpace(url))
            return;

        Url = url;
        IsDeepUnloaded = false;
        IsSleeping = false;
        LastActiveUtc = DateTime.UtcNow;

        if (WebView is null)
        {
            _pendingUrl = url;
            await EnsureWebViewAsync();
        }
        else if (IsInitialized && WebView.CoreWebView2 is not null)
        {
            if (WebView.CoreWebView2.IsSuspended)
                WebView.Visible = true;
            WebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            WebView.CoreWebView2.Navigate(url);
        }
        else
        {
            _pendingUrl = url;
        }

        StateChanged?.Invoke(this);
    }

    public async Task EnsureWebViewAsync()
    {
        if (_disposed || WebView is not null)
            return;

        var wv = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = true,
            DefaultBackgroundColor = _ctx.Settings.Theme == BrowserTheme.Light
                ? Color.White
                : Color.FromArgb(12, 12, 20)
        };

        WebView = wv;
        ((System.ComponentModel.ISupportInitialize)wv).BeginInit();
        _hostContainer.Controls.Add(wv);
        ((System.ComponentModel.ISupportInitialize)wv).EndInit();
        wv.BringToFront();
        wv.CreateControl();
        _ = wv.Handle;

        try
        {
            var env = await _ctx.GetSharedEnvironmentAsync(_incognito ? _incognitoDir : null);
            if (_disposed || wv.IsDisposed) return;
            if (!wv.IsHandleCreated)
            {
                wv.CreateControl();
                _ = wv.Handle;
            }

            if (_incognito)
            {
                var ctrlOpts = env.CreateCoreWebView2ControllerOptions();
                ctrlOpts.IsInPrivateModeEnabled = true;
                await wv.EnsureCoreWebView2Async(env, ctrlOpts);
            }
            else
            {
                await wv.EnsureCoreWebView2Async(env);
            }
            if (_disposed) return;

            var core = wv.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = true;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.DocumentTitleChanged += OnDocumentTitleChanged;
            core.SourceChanged += OnSourceChanged;
            core.NewWindowRequested += OnNewWindowRequested;
            core.DownloadStarting += OnDownloadStarting;
            core.ContextMenuRequested += OnContextMenuRequested;
            core.WebMessageReceived += OnWebMessageReceived;
            core.BasicAuthenticationRequested += OnBasicAuthRequested;
            core.IsDocumentPlayingAudioChanged += (_, _) => StateChanged?.Invoke(this);
            core.IsMutedChanged += (_, _) => StateChanged?.Invoke(this);
            core.FaviconChanged += (_, _) =>
            {
                FaviconUri = core.FaviconUri ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(Url))
                    _ = _ctx.Favicons.FetchAndCacheAsync(Url, FaviconUri);
            };
            wv.ZoomFactorChanged += (_, _) =>
            {
                ZoomFactor = wv.ZoomFactor;
                ZoomChanged?.Invoke(this, ZoomFactor);
            };
            wv.KeyDown += OnWebViewKeyDown;

            // Inject lightweight form submit listener for password save prompt
            if (!_incognito)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    document.addEventListener('submit', function(e) {
                        try {
                            var form = e.target;
                            if (!form || !form.querySelector) return;
                            var p = form.querySelector('input[type=""password""]');
                            if (!p || !p.value) return;
                            var u = form.querySelector('input[type=""email""],input[type=""text""],input[name*=""user""],input[name*=""email""],input[id*=""user""],input[id*=""email""]');
                            if (u && u.value && window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage(JSON.stringify({
                                    type: 'ed-pw-submit',
                                    url: location.href,
                                    username: u.value,
                                    password: p.value
                                }));
                            }
                        } catch(_) {}
                    }, true);
                ");
            }

            if (_ctx.Settings.FingerprintProtection)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    (function(){
                        try {
                            Object.defineProperty(Navigator.prototype, 'globalPrivacyControl', { get: () => true, configurable: true });
                            Object.defineProperty(Navigator.prototype, 'doNotTrack', { get: () => '1', configurable: true });
                            if (navigator.getBattery) {
                                Navigator.prototype.getBattery = () => Promise.reject(new DOMException('Disabled for privacy', 'NotAllowedError'));
                            }
                            Object.defineProperty(Navigator.prototype, 'hardwareConcurrency', { get: () => 4, configurable: true });
                            Object.defineProperty(Navigator.prototype, 'deviceMemory', { get: () => 8, configurable: true });
                        } catch(_) {}
                    })();
                ");
            }

            IsInitialized = true;
            wv.ZoomFactor = ZoomFactor;

            if (!_incognito)
                _ = _ctx.Extensions.SyncWithProfileAsync(core.Profile);

            if (!string.IsNullOrEmpty(_pendingUrl))
            {
                var target = _pendingUrl;
                _pendingUrl = null;
                core.Navigate(target);
            }
        }
        catch (Exception ex)
        {
            Title = "Hata";
            try
            {
                var logPath = Path.Combine(_ctx.Storage.DataDirectory, "edbrowser.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:O}] WebView2 init error (wvHandle={wv.IsHandleCreated}:{wv.Handle}, hostCreated={_hostContainer.IsHandleCreated}): {ex}\n");
            }
            catch { }
        }
    }

    private void OnBasicAuthRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
    {
        var prof = ProxyService.GetActiveVpnProfile(_ctx.Settings);
        if (_ctx.Settings.ProxyMode == ProxyMode.Custom && prof is not null && !string.IsNullOrEmpty(prof.Username))
        {
            e.Response.UserName = prof.Username;
            e.Response.Password = prof.Password;
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var reqUrl = e.Request.Uri;

        if (_ctx.Settings.SendGpcAndDnt)
        {
            try
            {
                e.Request.Headers.SetHeader("DNT", "1");
                e.Request.Headers.SetHeader("Sec-GPC", "1");
            }
            catch { }
        }

        if (_ctx.Settings.AdBlockEnabled && _ctx.AdBlocker.ShouldBlock(reqUrl, _topHost, _ctx.Settings.AdBlockWhitelistHosts))
        {
            _ctx.AdBlocker.RecordBlocked();
            BlockedOnTab++;
            e.Response = WebView!.CoreWebView2.Environment.CreateWebResourceResponse(
                Stream.Null, 204, "Blocked", "");
            return;
        }

        if (!_ctx.Settings.LoadImages && e.ResourceContext == CoreWebView2WebResourceContext.Image)
        {
            e.Response = WebView!.CoreWebView2.Environment.CreateWebResourceResponse(
                Stream.Null, 204, "Image Blocked", "");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var rawUri))
        {
            if (_ctx.Settings.HttpsOnlyMode &&
                rawUri.Scheme == Uri.UriSchemeHttp &&
                !_httpsFailedHosts.Contains(rawUri.Host) &&
                !_ctx.Settings.HttpsOnlyBypassHosts.Contains(rawUri.Host, StringComparer.OrdinalIgnoreCase) &&
                ProxyService.TryUpgradeToHttps(e.Uri, out var httpsUri))
            {
                _lastUpgradedFromHttpUrl = e.Uri;
                e.Cancel = true;
                WebView?.CoreWebView2?.Navigate(httpsUri);
                return;
            }

            if (_ctx.Settings.StripTrackingParams)
            {
                var cleaned = ProxyService.CleanTrackingParameters(e.Uri);
                if (!string.Equals(cleaned, e.Uri, StringComparison.Ordinal))
                {
                    e.Cancel = true;
                    WebView?.CoreWebView2?.Navigate(cleaned);
                    return;
                }
            }

            _topHost = rawUri.Host;
        }

        IsLoading = true;
        IsSleeping = false;
        IsReaderMode = false;
        BlockedOnTab = 0;
        Url = e.Uri;
        StateChanged?.Invoke(this);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        IsLoading = false;
        if (!e.IsSuccess && !string.IsNullOrEmpty(_lastUpgradedFromHttpUrl) &&
            Uri.TryCreate(Url, UriKind.Absolute, out var curUri) &&
            curUri.Scheme == Uri.UriSchemeHttps &&
            e.WebErrorStatus is CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect
                or CoreWebView2WebErrorStatus.CertificateExpired
                or CoreWebView2WebErrorStatus.CertificateRevoked
                or CoreWebView2WebErrorStatus.CertificateIsInvalid
                or CoreWebView2WebErrorStatus.CannotConnect
                or CoreWebView2WebErrorStatus.ConnectionAborted
                or CoreWebView2WebErrorStatus.ConnectionReset)
        {
            _httpsFailedHosts.Add(curUri.Host);
            var fallbackHttp = _lastUpgradedFromHttpUrl;
            _lastUpgradedFromHttpUrl = null;
            WebView?.CoreWebView2?.Navigate(fallbackHttp);
            return;
        }
        _lastUpgradedFromHttpUrl = null;

        if (WebView?.CoreWebView2 is not null)
        {
            Url = WebView.CoreWebView2.Source;
            if (!string.IsNullOrWhiteSpace(WebView.CoreWebView2.DocumentTitle))
                Title = WebView.CoreWebView2.DocumentTitle;
        }

        if (!_incognito && e.IsSuccess && !string.IsNullOrWhiteSpace(Url))
            _ctx.AddHistory(Title, Url);

        if (e.IsSuccess && WebView?.CoreWebView2 is not null)
        {
            _ = _ctx.Favicons.FetchAndCacheAsync(Url, FaviconUri);
            if (_ctx.Settings.AdBlockEnabled && _ctx.Settings.CosmeticAdBlockEnabled &&
                !_ctx.Settings.AdBlockWhitelistHosts.Contains(_topHost, StringComparer.OrdinalIgnoreCase))
            {
                var cssJson = JsonSerializer.Serialize(AdBlockerService.CosmeticHideCss);
                _ = WebView.CoreWebView2.ExecuteScriptAsync($@"
                    (function(){{
                        if (!document.getElementById('ed-cosmetic-adblock')) {{
                            var st = document.createElement('style');
                            st.id = 'ed-cosmetic-adblock';
                            st.textContent = {cssJson};
                            (document.head || document.documentElement).appendChild(st);
                        }}
                    }})();");
            }
        }

        StateChanged?.Invoke(this);
        UrlNavigated?.Invoke(this, Url);
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (WebView?.CoreWebView2 is not null && !string.IsNullOrWhiteSpace(WebView.CoreWebView2.DocumentTitle))
        {
            Title = WebView.CoreWebView2.DocumentTitle;
            StateChanged?.Invoke(this);
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (WebView?.CoreWebView2 is not null)
        {
            Url = WebView.CoreWebView2.Source;
            StateChanged?.Invoke(this);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
            OpenNewTabRequested?.Invoke(this, e.Uri);
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var fileName = Path.GetFileName(e.ResultFilePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "indirilen_dosya";

        var savePath = Path.Combine(_ctx.Storage.DownloadsDirectory, fileName);
        if (File.Exists(savePath))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            savePath = Path.Combine(_ctx.Storage.DownloadsDirectory, $"{name}_{DateTime.Now:HHmmss}{ext}");
        }

        e.ResultFilePath = savePath;
        var op = e.DownloadOperation;
        var rec = new DownloadRecord
        {
            FileName = Path.GetFileName(savePath),
            FullPath = savePath,
            Url = op.Uri,
            TotalBytes = (long)(op.TotalBytesToReceive ?? 0),
            ReceivedBytes = op.BytesReceived,
            State = "InProgress"
        };

        if (!_incognito)
        {
            _ctx.Downloads.Insert(0, rec);
            _ctx.Storage.SaveDownloads(_ctx.Downloads);
        }

        DownloadStarted?.Invoke(this, rec);

        op.BytesReceivedChanged += (_, _) =>
        {
            rec.ReceivedBytes = op.BytesReceived;
            rec.TotalBytes = (long)(op.TotalBytesToReceive ?? 0);
            DownloadUpdated?.Invoke(this, rec);
        };

        op.StateChanged += (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.Completed)
            {
                rec.State = "Completed";
                rec.ReceivedBytes = rec.TotalBytes > 0 ? rec.TotalBytes : op.BytesReceived;
                if (!_incognito) _ctx.Storage.SaveDownloads(_ctx.Downloads);
                DownloadCompleted?.Invoke(this, rec);
            }
            else if (op.State == CoreWebView2DownloadState.Interrupted)
            {
                rec.State = "Interrupted";
                if (!_incognito) _ctx.Storage.SaveDownloads(_ctx.Downloads);
                DownloadCompleted?.Invoke(this, rec);
            }
        };
    }

    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (WebView?.CoreWebView2 is null) return;
        var env = WebView.CoreWebView2.Environment;
        var target = e.ContextMenuTarget;

        int insertAt = 0;

        if (target.HasLinkUri && !string.IsNullOrEmpty(target.LinkUri))
        {
            var link = target.LinkUri;
            var openNew = env.CreateContextMenuItem("🔗 Bağlantıyı Yeni Sekmede Aç", null, CoreWebView2ContextMenuItemKind.Command);
            openNew.CustomItemSelected += (_, _) => OpenNewTabRequested?.Invoke(this, link);
            e.MenuItems.Insert(insertAt++, openNew);

            var copyLink = env.CreateContextMenuItem("📋 Bağlantı Adresini Kopyala", null, CoreWebView2ContextMenuItemKind.Command);
            copyLink.CustomItemSelected += (_, _) => Clipboard.SetText(link);
            e.MenuItems.Insert(insertAt++, copyLink);

            e.MenuItems.Insert(insertAt++, env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
        }

        if (target.Kind == CoreWebView2ContextMenuTargetKind.Image && !string.IsNullOrEmpty(target.SourceUri))
        {
            var imgUrl = target.SourceUri;
            var openImg = env.CreateContextMenuItem("🖼️ Resmi Yeni Sekmede Aç", null, CoreWebView2ContextMenuItemKind.Command);
            openImg.CustomItemSelected += (_, _) => OpenNewTabRequested?.Invoke(this, imgUrl);
            e.MenuItems.Insert(insertAt++, openImg);

            var saveImg = env.CreateContextMenuItem("💾 Resmi İndir", null, CoreWebView2ContextMenuItemKind.Command);
            saveImg.CustomItemSelected += (_, _) => _ = DownloadDirectFileAsync(imgUrl);
            e.MenuItems.Insert(insertAt++, saveImg);

            var copyImg = env.CreateContextMenuItem("📋 Resim Adresini Kopyala", null, CoreWebView2ContextMenuItemKind.Command);
            copyImg.CustomItemSelected += (_, _) => Clipboard.SetText(imgUrl);
            e.MenuItems.Insert(insertAt++, copyImg);

            e.MenuItems.Insert(insertAt++, env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
        }

        if (target.HasSelection && !string.IsNullOrWhiteSpace(target.SelectionText))
        {
            var sel = target.SelectionText.Trim();
            var label = sel.Length > 22 ? sel[..22] + "…" : sel;
            var searchItem = env.CreateContextMenuItem($"🔍 \"{label}\" ara", null, CoreWebView2ContextMenuItemKind.Command);
            searchItem.CustomItemSelected += (_, _) =>
            {
                var q = _ctx.NormalizeUrlOrSearch(sel);
                OpenNewTabRequested?.Invoke(this, q);
            };
            e.MenuItems.Insert(insertAt++, searchItem);
        }

        var findItem = env.CreateContextMenuItem("🔍 Sayfada Ara (Ctrl+F)", null, CoreWebView2ContextMenuItemKind.Command);
        findItem.CustomItemSelected += (_, _) => FindRequested?.Invoke();
        e.MenuItems.Add(findItem);

        var bmItem = env.CreateContextMenuItem("🔖 Yer İmine Ekle (Ctrl+D)", null, CoreWebView2ContextMenuItemKind.Command);
        bmItem.CustomItemSelected += (_, _) => BookmarkRequested?.Invoke();
        e.MenuItems.Add(bmItem);

        e.MenuItems.Add(env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));

        var viewSrcItem = env.CreateContextMenuItem("📄 Sayfa Kaynağını Görüntüle (Ctrl+U)", null, CoreWebView2ContextMenuItemKind.Command);
        viewSrcItem.CustomItemSelected += (_, _) => ViewSourceRequested?.Invoke();
        e.MenuItems.Add(viewSrcItem);

        var loc = e.Location;
        var inspectItem = env.CreateContextMenuItem("🛠️ İncele (Geliştirici Araçları)", null, CoreWebView2ContextMenuItemKind.Command);
        inspectItem.CustomItemSelected += (_, _) => InspectRequested?.Invoke(loc.X, loc.Y);
        e.MenuItems.Add(inspectItem);
    }

    private async Task DownloadDirectFileAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await http.GetByteArrayAsync(url);
            var uri = new Uri(url);
            var fname = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fname) || !fname.Contains('.'))
                fname = $"image_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var dest = Path.Combine(_ctx.Storage.DownloadsDirectory, fname);
            await File.WriteAllBytesAsync(dest, bytes);

            var rec = new DownloadRecord
            {
                FileName = Path.GetFileName(dest),
                FullPath = dest,
                Url = url,
                TotalBytes = bytes.Length,
                ReceivedBytes = bytes.Length,
                State = "Completed"
            };
            if (!_incognito)
            {
                _ctx.Downloads.Insert(0, rec);
                _ctx.Storage.SaveDownloads(_ctx.Downloads);
            }
            DownloadCompleted?.Invoke(this, rec);
        }
        catch
        {
            // Ignore failed direct download
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json)) return;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var tp)) return;
            var msgType = tp.GetString();

            if (msgType == "ed-devtools-pick")
            {
                DevToolsElementPicked?.Invoke(json);
                return;
            }

            if (_incognito) return;

            if (msgType == "ed-pw-submit")
            {
                var url = doc.RootElement.GetProperty("url").GetString() ?? "";
                var user = doc.RootElement.GetProperty("username").GetString() ?? "";
                var pass = doc.RootElement.GetProperty("password").GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(pass))
                {
                    if (!_ctx.Vault.HasCredential(url, user))
                    {
                        PasswordOfferRequested?.Invoke(this, JsonSerializer.Serialize(new { url, user, pass }));
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private void OnWebViewKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.KeyCode;
        var mods = e.Modifiers;
        var combo = e.KeyData;

        bool shouldForward = key is Keys.F5 or Keys.F11 or Keys.F12 or Keys.Escape ||
            (mods == Keys.Shift && key == Keys.Escape) ||
            (mods == (Keys.Control | Keys.Alt) && key == Keys.R) ||
            (mods == Keys.Control && key is Keys.T or Keys.W or Keys.L or Keys.R or Keys.D or Keys.F or Keys.P
                or Keys.N or Keys.H or Keys.S or Keys.U or Keys.Oemcomma
                or Keys.Oemplus or Keys.Add or Keys.OemMinus or Keys.Subtract or Keys.D0 or Keys.NumPad0
                or >= Keys.D1 and <= Keys.D9) ||
            (mods == (Keys.Control | Keys.Shift) && key is Keys.P or Keys.B or Keys.J or Keys.S or Keys.I or Keys.T or Keys.O or Keys.C or Keys.E or Keys.L or Keys.V or Keys.X or Keys.Delete) ||
            (mods == Keys.Alt && key is Keys.Left or Keys.Right);

        if (shouldForward)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            AcceleratorForwarded?.Invoke(combo);
        }
    }

    public async Task ActivateAsync()
    {
        LastActiveUtc = DateTime.UtcNow;

        if (IsDeepUnloaded && !string.IsNullOrEmpty(Url))
        {
            IsDeepUnloaded = false;
            IsSleeping = false;
            await NavigateAsync(Url);
            return;
        }

        if (WebView is not null)
        {
            WebView.Visible = true;
            WebView.BringToFront();
            if (WebView.CoreWebView2 is not null)
            {
                WebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                IsSleeping = false;
            }
        }
        StateChanged?.Invoke(this);
    }

    public void Deactivate()
    {
        if (WebView is not null)
        {
            WebView.Visible = false;
            if (_ctx.Settings.BackgroundTabThrottle && WebView.CoreWebView2 is not null)
            {
                WebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
        }
    }

    public async Task<bool> TrySleepAsync()
    {
        if (IsSleeping || IsDeepUnloaded || IsLoading || WebView?.CoreWebView2 is null)
            return false;

        try
        {
            WebView.Visible = false;
            WebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            bool ok = await WebView.CoreWebView2.TrySuspendAsync();
            if (ok)
            {
                IsSleeping = true;
                StateChanged?.Invoke(this);
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public void SetRestoredStub(string title, string url)
    {
        Title = string.IsNullOrWhiteSpace(title) ? url : title;
        Url = url;
        IsDeepUnloaded = true;
        IsSleeping = false;
    }

    public void DeepUnload()
    {
        if (IsDeepUnloaded || string.IsNullOrEmpty(Url) || WebView is null)
            return;

        _hostContainer.Controls.Remove(WebView);
        WebView.Dispose();
        WebView = null;
        IsInitialized = false;
        IsSleeping = false;
        IsDeepUnloaded = true;
        StateChanged?.Invoke(this);
    }

    public void GoBack() { if (CanGoBack) WebView?.CoreWebView2?.GoBack(); }
    public void GoForward() { if (CanGoForward) WebView?.CoreWebView2?.GoForward(); }

    public void ReloadOrStop()
    {
        if (WebView?.CoreWebView2 is null) return;
        if (IsLoading) WebView.CoreWebView2.Stop();
        else WebView.CoreWebView2.Reload();
    }

    public void ApplyZoom(double factor)
    {
        ZoomFactor = Math.Clamp(factor, 0.25, 3.0);
        if (WebView is not null)
            WebView.ZoomFactor = ZoomFactor;
        ZoomChanged?.Invoke(this, ZoomFactor);
    }

    public void ToggleMute()
    {
        if (WebView?.CoreWebView2 is null) return;
        WebView.CoreWebView2.IsMuted = !WebView.CoreWebView2.IsMuted;
        StateChanged?.Invoke(this);
    }

    public async Task ToggleMediaPlayPauseAsync()
    {
        if (WebView?.CoreWebView2 is null) return;
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(@"
                (function(){
                    var els = Array.from(document.querySelectorAll('video,audio'));
                    var playing = els.find(e => !e.paused && !e.ended);
                    if (playing) { playing.pause(); }
                    else if (els.length > 0) { els[0].play().catch(()=>{}); }
                })();");
        }
        catch { }
    }

    public async Task TogglePictureInPictureAsync()
    {
        if (WebView?.CoreWebView2 is null) return;
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(@"
                (async function(){
                    try {
                        if (document.pictureInPictureElement) {
                            await document.exitPictureInPicture();
                            return;
                        }
                        var vids = Array.from(document.querySelectorAll('video')).filter(v => v.readyState >= 1);
                        if (vids.length > 0) await vids[0].requestPictureInPicture();
                    } catch(_) {}
                })();");
        }
        catch { }
    }

    public async Task ToggleReaderModeAsync()
    {
        if (WebView?.CoreWebView2 is null || !HasNavigated) return;
        bool dark = _incognito || _ctx.Settings.Theme == BrowserTheme.Dark;
        try
        {
            var res = await WebView.CoreWebView2.ExecuteScriptAsync($@"
                (function(){{
                    var old = document.getElementById('ed-reader-overlay');
                    if (old) {{
                        old.remove();
                        document.documentElement.style.overflow = '';
                        return 'off';
                    }}
                    var root = document.querySelector('article, main, [role=""main""], .post-content, .article-content') || document.body;
                    var clone = root.cloneNode(true);
                    clone.querySelectorAll('script,style,noscript,iframe,nav,aside,header,footer,.ad,.ads,.advertisement').forEach(e => e.remove());
                    var title = document.querySelector('h1')?.innerText || document.title || '';
                    var wrap = document.createElement('div');
                    wrap.id = 'ed-reader-overlay';
                    wrap.style.cssText = 'position:fixed;inset:0;z-index:2147483647;overflow:auto;padding:40px 24px;' +
                        'background:{(dark ? "#10121b" : "#faf9f6")};color:{(dark ? "#e2e8f0" : "#1e293b")};' +
                        'font-family:Georgia,Segoe UI,serif;font-size:19px;line-height:1.75;';
                    var inner = document.createElement('div');
                    inner.style.cssText = 'max-width:740px;margin:0 auto;';
                    var bar = document.createElement('div');
                    bar.style.cssText = 'display:flex;justify-content:space-between;align-items:center;margin-bottom:24px;padding-bottom:12px;border-bottom:1px solid rgba(148,163,184,0.25);font-family:Segoe UI,sans-serif;font-size:13px;';
                    bar.innerHTML = '<span>ED Okuyucu Modu</span><div><button id=""ed-r-sm"" style=""margin-right:6px;padding:4px 10px;cursor:pointer;"">A-</button><button id=""ed-r-lg"" style=""margin-right:6px;padding:4px 10px;cursor:pointer;"">A+</button><button id=""ed-r-x"" style=""padding:4px 12px;cursor:pointer;"">Kapat</button></div>';
                    var h = document.createElement('h1');
                    h.style.cssText = 'font-size:30px;line-height:1.3;margin:0 0 22px 0;';
                    h.innerText = title;
                    inner.appendChild(bar);
                    inner.appendChild(h);
                    inner.appendChild(clone);
                    wrap.appendChild(inner);
                    document.body.appendChild(wrap);
                    document.documentElement.style.overflow = 'hidden';
                    document.getElementById('ed-r-sm').onclick = () => {{ var cur = parseFloat(wrap.style.fontSize)||19; wrap.style.fontSize = Math.max(14, cur-2)+'px'; }};
                    document.getElementById('ed-r-lg').onclick = () => {{ var cur = parseFloat(wrap.style.fontSize)||19; wrap.style.fontSize = Math.min(30, cur+2)+'px'; }};
                    document.getElementById('ed-r-x').onclick = () => {{ wrap.remove(); document.documentElement.style.overflow = ''; }};
                    return 'on';
                }})();");
            IsReaderMode = res.Contains("on", StringComparison.OrdinalIgnoreCase);
            StateChanged?.Invoke(this);
        }
        catch { }
    }

    public void OpenDevTools() => WebView?.CoreWebView2?.OpenDevToolsWindow();

    public async Task SavePageAsAsync()
    {
        if (WebView?.CoreWebView2 is null) return;
        try { await WebView.CoreWebView2.ShowSaveAsUIAsync(); }
        catch { }
    }

    public void PrintPage()
    {
        if (WebView?.CoreWebView2 is null) return;
        try { WebView.CoreWebView2.ShowPrintUI(); }
        catch { _ = WebView.CoreWebView2.ExecuteScriptAsync("window.print()"); }
    }

    public async Task<bool> PrintToPdfAsync(string filePath)
    {
        if (WebView?.CoreWebView2 is null) return false;
        try { return await WebView.CoreWebView2.PrintToPdfAsync(filePath); }
        catch { return false; }
    }

    public async Task ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds kinds)
    {
        if (WebView?.CoreWebView2 is null) return;
        try { await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(kinds); }
        catch { }
    }

    public async Task<bool> HasPasswordInputAsync()
    {
        if (WebView?.CoreWebView2 is null) return false;
        try
        {
            var res = await WebView.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[type=\"password\"]')!==null");
            return res == "true";
        }
        catch
        {
            return false;
        }
    }

    public async Task FillPasswordAsync(string username, string password)
    {
        if (WebView?.CoreWebView2 is null) return;
        var uJson = JsonSerializer.Serialize(username);
        var pJson = JsonSerializer.Serialize(password);
        await WebView.CoreWebView2.ExecuteScriptAsync($@"
            (function(){{
                var u = document.querySelector('input[type=""email""],input[type=""text""],input[name*=""user""],input[name*=""email""],input[id*=""user""],input[id*=""email""]');
                var p = document.querySelector('input[type=""password""]');
                if (u) {{ u.value = {uJson}; u.dispatchEvent(new Event('input', {{bubbles:true}})); u.dispatchEvent(new Event('change', {{bubbles:true}})); }}
                if (p) {{ p.value = {pJson}; p.dispatchEvent(new Event('input', {{bubbles:true}})); p.dispatchEvent(new Event('change', {{bubbles:true}})); }}
            }})();");
    }

    public async Task FindInPageAsync(string query, bool forward = true)
    {
        if (WebView?.CoreWebView2 is null || string.IsNullOrEmpty(query)) return;
        var qJson = JsonSerializer.Serialize(query);
        var backFlag = forward ? "false" : "true";
        await WebView.CoreWebView2.ExecuteScriptAsync($"window.find({qJson}, false, {backFlag}, true, false, false, false);");
    }

    public async Task ClearFindSelectionAsync()
    {
        if (WebView?.CoreWebView2 is null) return;
        await WebView.CoreWebView2.ExecuteScriptAsync("window.getSelection()?.removeAllRanges();");
    }

    public async Task<byte[]?> CapturePreviewPngAsync()
    {
        if (WebView?.CoreWebView2 is null) return null;
        using var ms = new MemoryStream();
        await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (WebView is not null)
        {
            _hostContainer.Controls.Remove(WebView);
            WebView.Dispose();
            WebView = null;
        }
    }
}
