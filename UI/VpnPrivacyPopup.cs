using System.Runtime.InteropServices;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

/// <summary>
/// Instant 1-Click VPN, Built-in Verified Free Server Scanner, WARP/Tor/Custom Profile switcher,
/// and Privacy Shield popup. Fixes ComboBox dropdown dismissal completely and switches tunnels
/// live through LocalVpnRouterService with zero browser restart.
/// </summary>
public sealed class VpnPrivacyPopup : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int CS_DROPSHADOW = 0x00020000;
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;

    private static readonly ProxyMode[] ModeOrder =
    {
        ProxyMode.FreeCommunity,
        ProxyMode.CloudflareWarp,
        ProxyMode.Tor,
        ProxyMode.Custom
    };

    private static List<FreeVpnServer> _cachedServers = new();

    private readonly Form? _ownerForm;
    private readonly BrowserContext _ctx;
    private readonly ThemePalette _p;
    private readonly Action _onSettingsChanged;
    private readonly Action<string> _openTabAction;
    private readonly System.Windows.Forms.Timer _outsideClickTimer;
    private readonly float _sc;
    private bool _closing;
    private bool _suppressOutsideDismiss;
    private bool _comboDropDownOpen;
    private DateTime _lastComboCloseUtc = DateTime.MinValue;
    private bool _syncingUi;

    private readonly ModernCardPanel _heroCard;
    private readonly Label _heroStatusLbl;
    private readonly ModernToggleSwitch _vpnToggle;
    private readonly ComboBox _modeCombo;
    private readonly ComboBox _freeServerCombo;
    private readonly ModernButton _scanBtn;
    private readonly ComboBox _profileCombo;
    private readonly RoundedTextBox _proxyBox;
    private readonly ComboBox _dohCombo;
    private readonly Label _tunnelProbeLbl;
    private readonly Label _ipResultLbl;

    public bool IsComboDropDownGuardActive => _comboDropDownOpen || _suppressOutsideDismiss;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    public VpnPrivacyPopup(
        Form? ownerForm,
        BrowserContext ctx,
        ThemePalette palette,
        Point anchorBottomLeftScreen,
        Action onSettingsChanged,
        Action _unusedRestartCallback,
        Action<string> openTabAction,
        bool enableOutsideDismiss = true)
    {
        _ownerForm = ownerForm;
        _ctx = ctx;
        _p = palette;
        _onSettingsChanged = onSettingsChanged;
        _openTabAction = openTabAction;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = _p.SurfaceBg;
        ForeColor = _p.TextPrimary;
        KeyPreview = true;

        _sc = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        int S(int px) => (int)Math.Round(px * _sc);

        int popupW = S(420);
        int padX = S(14);
        int innerW = popupW - padX * 2;

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            BackColor = _p.SurfaceBg
        };
        Controls.Add(host);

        var s = _ctx.Settings;
        int y = S(12);

        // ── Hero 1-Click Instant VPN Switch Card ──────────────────────────────────
        int heroH = S(64);
        _heroCard = new ModernCardPanel
        {
            Location = new Point(padX, y),
            Size = new Size(innerW, heroH),
            FillColor = _p.ElevatedBg,
            BorderColor = s.VpnEnabled ? _p.Green : _p.Border
        };
        var titleLbl = new Label
        {
            Text = "VPN Koruması (Anlık — Yeniden Başlatma Yok)",
            Location = new Point(S(12), S(9)),
            Size = new Size(innerW - S(76), S(20)),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        _heroStatusLbl = new Label
        {
            Location = new Point(S(12), S(32)),
            Size = new Size(innerW - S(76), S(20)),
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            ForeColor = _p.TextMuted,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        _vpnToggle = new ModernToggleSwitch
        {
            Checked = s.VpnEnabled,
            Location = new Point(innerW - S(60), S(19)),
            Size = new Size(S(48), S(26)),
            OnColorStart = Color.FromArgb(16, 185, 129),
            OnColorEnd = _p.Green,
            OffTrackColor = _p.Border
        };
        _heroCard.Controls.AddRange(new Control[] { titleLbl, _heroStatusLbl, _vpnToggle });
        host.Controls.Add(_heroCard);
        y += heroH + S(10);

        // ── Section 1: Connection Mode & Built-in Verified Free Servers ───────────
        y = AddSectionTitle(host, "1. VPN Türü & Ücretsiz Sunucu Seçimi", y, padX, innerW, S);

        int modeIdx = Array.IndexOf(ModeOrder, s.ProxyMode);
        if (modeIdx < 0) modeIdx = 0;

        _modeCombo = MakeCombo(new Point(padX, y), innerW, new[]
        {
            "Ücretsiz Otomatik VPN Sunucusu (Anlık Bul)",
            "Cloudflare WARP Yerel Tüneli (:40000)",
            "Tor Ağı Yerel Tüneli (:9050 / :9150)",
            "Özel VPN / SOCKS5 / HTTPS Profili"
        }, modeIdx);
        host.Controls.Add(_modeCombo);
        y += S(34);

        // Built-in Verified Free Server Picker + Live Scanner Button
        int scanBtnW = S(104);
        int gap = S(6);
        int freeComboW = innerW - scanBtnW - gap;
        _freeServerCombo = MakeCombo(new Point(padX, y), freeComboW, Array.Empty<string>(), 0);
        _scanBtn = new ModernButton
        {
            Text = "Sunucu Bul",
            Primary = true,
            Location = new Point(padX + freeComboW + gap, y),
            Size = new Size(scanBtnW, S(28))
        };
        _scanBtn.ApplyTheme(_p);
        host.Controls.AddRange(new Control[] { _freeServerCombo, _scanBtn });
        y += S(34);

        PopulateFreeServerCombo();

        _tunnelProbeLbl = new Label
        {
            Text = "Yerel tüneller kontrol ediliyor…",
            Location = new Point(padX, y),
            Size = new Size(innerW, S(18)),
            Font = new Font("Segoe UI", 8f),
            ForeColor = _p.TextMuted,
            UseMnemonic = false
        };
        host.Controls.Add(_tunnelProbeLbl);
        y += S(22);

        // Saved Custom Profiles row
        int addW = S(64);
        int delW = S(44);
        int profW = innerW - addW - delW - gap * 2;
        _profileCombo = MakeCombo(new Point(padX, y), profW, Array.Empty<string>(), 0);
        var addProfBtn = MakeSmallBtn("+ Ekle", new Point(padX + profW + gap, y), addW, S(28), (_, _) => AddVpnProfileInline());
        var delProfBtn = MakeSmallBtn("Sil", new Point(padX + profW + addW + gap * 2, y), delW, S(28), (_, _) => DeleteActiveVpnProfile());
        host.Controls.AddRange(new Control[] { _profileCombo, addProfBtn, delProfBtn });
        y += S(33);

        _proxyBox = new RoundedTextBox
        {
            Location = new Point(padX, y),
            Size = new Size(innerW, S(32)),
            PlaceholderText = "Özel adres: socks5://127.0.0.1:1080 veya http://ip:port",
            Text = s.CustomProxy
        };
        _proxyBox.ApplyTheme(_p, elevated: false);
        _proxyBox.Inner.Leave += (_, _) =>
        {
            var txt = _proxyBox.Text.Trim();
            if (txt != _ctx.Settings.CustomProxy)
            {
                _ctx.Settings.CustomProxy = txt;
                ApplyLiveAndNotify();
            }
        };
        _proxyBox.Inner.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ctx.Settings.CustomProxy = _proxyBox.Text.Trim();
                if (!string.IsNullOrEmpty(_ctx.Settings.CustomProxy))
                {
                    _ctx.Settings.ProxyMode = ProxyMode.Custom;
                    _ctx.Settings.VpnEnabled = true;
                }
                SyncControlsFromSettings();
                ApplyLiveAndNotify();
            }
        };
        host.Controls.Add(_proxyBox);
        y += S(38);

        PopulateProfilesCombo();

        // IP Test Row
        var testIpBtn = new ModernButton
        {
            Text = "IP Test Et",
            Location = new Point(padX, y),
            Height = S(28)
        };
        testIpBtn.ApplyTheme(_p);
        testIpBtn.FitWidthToText(92, 20);
        _ipResultLbl = new Label
        {
            Text = "Çıkış IP test edilmedi",
            Location = new Point(testIpBtn.Right + S(10), y),
            Size = new Size(Math.Max(S(80), innerW - testIpBtn.Width - S(10)), S(28)),
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = _p.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        testIpBtn.Click += async (_, _) => await RunIpTraceTestAsync();
        host.Controls.AddRange(new Control[] { testIpBtn, _ipResultLbl });
        y += S(34);

        // Quick 1-click Profile Sources ("VPN Profili Nereden Bulacağım?")
        int resGap = S(6);
        int resW = (innerW - resGap * 2) / 3;
        var warpLinkBtn = MakeSmallBtn("WARP İndir", new Point(padX, y), resW, S(28), (_, _) => OpenUrlExternalSafe("https://one.one.one.one/"));
        var torLinkBtn = MakeSmallBtn("Tor İndir", new Point(padX + resW + resGap, y), resW, S(28), (_, _) => OpenUrlExternalSafe("https://www.torproject.org/download/"));
        var extVpnBtn = MakeSmallBtn("VPN Eklentisi", new Point(padX + (resW + resGap) * 2, y), innerW - (resW + resGap) * 2, S(28), (_, _) => OpenUrlExternalSafe("https://chromewebstore.google.com/search/vpn"));
        host.Controls.AddRange(new Control[] { warpLinkBtn, torLinkBtn, extVpnBtn });
        y += S(32);

        var noteLbl = new Label
        {
            Text = "Ücretsiz sunucular HTTPS üzerinde IP/ülke gizler; tam sıfır-kayıt gizlilik için WARP veya Tor önerilir.",
            Location = new Point(padX, y),
            Size = new Size(innerW, S(30)),
            Font = new Font("Segoe UI", 7.75f),
            ForeColor = _p.TextMuted,
            UseMnemonic = false
        };
        host.Controls.Add(noteLbl);
        y += S(34);

        y = AddDivider(host, y, padX, innerW, S);

        // ── Section 2: Instant Privacy Shields ────────────────────────────────────
        y = AddSectionTitle(host, "2. Anlık Gizlilik Korumaları", y, padX, innerW, S);

        (_, y) = AddToggleRow(host, "Reklam & İzleyici Engelleyici (150+ ağ)", s.AdBlockEnabled, y, padX, innerW, S, v =>
        {
            _ctx.Settings.AdBlockEnabled = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
            _onSettingsChanged();
        });
        (_, y) = AddToggleRow(host, "HTTPS-Only (Otomatik HTTPS Yükseltme)", s.HttpsOnlyMode, y, padX, innerW, S, v =>
        {
            _ctx.Settings.HttpsOnlyMode = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });
        (_, y) = AddToggleRow(host, "URL İzleme Temizleyici (utm / fbclid / gclid)", s.StripTrackingParams, y, padX, innerW, S, v =>
        {
            _ctx.Settings.StripTrackingParams = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });
        (_, y) = AddToggleRow(host, "WebRTC Yerel IP Sızıntı Koruması*", s.WebRtcLeakProtection, y, padX, innerW, S, v =>
        {
            _ctx.Settings.WebRtcLeakProtection = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });
        (_, y) = AddToggleRow(host, "Parmak İzi + GPC / DNT Başlıkları", s.FingerprintProtection, y, padX, innerW, S, v =>
        {
            _ctx.Settings.FingerprintProtection = v;
            _ctx.Settings.SendGpcAndDnt = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });

        int dohLblW = S(130);
        var dohLbl = new Label
        {
            Text = "DNS-over-HTTPS*:",
            Location = new Point(padX, y + S(4)),
            Size = new Size(dohLblW, S(24)),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = _p.TextSecondary,
            UseMnemonic = false
        };
        _dohCombo = MakeCombo(new Point(padX + dohLblW, y + S(1)), innerW - dohLblW, new[]
        {
            "Kapalı (Sistem DNS)",
            "Cloudflare (1.1.1.1)",
            "Quad9 Güvenli (9.9.9.9)",
            "AdGuard DNS (Reklamsız)"
        }, (int)s.DohProvider);
        _dohCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingUi) return;
            _ctx.Settings.DohProvider = (DohProvider)Math.Clamp(_dohCombo.SelectedIndex, 0, 3);
            _ctx.Storage.SaveSettings(_ctx.Settings);
            _onSettingsChanged();
        };
        host.Controls.AddRange(new Control[] { dohLbl, _dohCombo });
        y += S(36);

        // Wire live event handlers after all controls exist
        WireInteractiveEvents();
        UpdateHeroCardUi();

        int desiredH = y + S(10);
        var wa = Screen.FromPoint(anchorBottomLeftScreen).WorkingArea;
        int maxH = Math.Max(340, wa.Height - 28);
        bool needsScroll = desiredH > maxH;
        host.AutoScroll = needsScroll;
        int finalH = Math.Min(desiredH, maxH);
        int finalW = popupW + (needsScroll ? SystemInformation.VerticalScrollBarWidth : 0);

        int posX = Math.Clamp(anchorBottomLeftScreen.X, wa.Left + 8, wa.Right - finalW - 8);
        int posY = anchorBottomLeftScreen.Y + finalH <= wa.Bottom - 6
            ? anchorBottomLeftScreen.Y
            : Math.Max(wa.Top + 6, wa.Bottom - finalH - 8);

        Size = new Size(finalW, finalH);
        Location = new Point(posX, posY);

        Paint += (_, e) =>
        {
            using var pen = new Pen(_p.Border, 1.2f);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        _outsideClickTimer = new System.Windows.Forms.Timer { Interval = 80 };
        if (enableOutsideDismiss)
        {
            Deactivate += (_, _) =>
            {
                if (!ShouldIgnoreOutsideDismiss())
                    SafeClose();
            };
            _outsideClickTimer.Tick += (_, _) =>
            {
                if (ShouldIgnoreOutsideDismiss() || _closing || IsDisposed || !Visible) return;
                bool lDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool rDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
                if ((lDown || rDown) && !Bounds.Contains(Cursor.Position))
                    SafeClose();
            };
            Shown += async (_, _) =>
            {
                _outsideClickTimer.Start();
                await ProbeLocalTunnelsAsync();
            };
        }
    }

    private bool ShouldIgnoreOutsideDismiss() =>
        _suppressOutsideDismiss || _comboDropDownOpen || (DateTime.UtcNow - _lastComboCloseUtc).TotalMilliseconds < 240;

    private void WireInteractiveEvents()
    {
        _vpnToggle.CheckedChanged += async (_, _) =>
        {
            if (_syncingUi) return;
            bool enable = _vpnToggle.Checked;
            _ctx.Settings.VpnEnabled = enable;

            if (enable)
            {
                if (_ctx.Settings.ProxyMode is ProxyMode.Direct or ProxyMode.System)
                {
                    bool warpOpen = ProxyService.IsPortOpenFast("127.0.0.1", 40000, 120);
                    _ctx.Settings.ProxyMode = warpOpen ? ProxyMode.CloudflareWarp : ProxyMode.FreeCommunity;
                    SyncControlsFromSettings();
                }

                if (_ctx.Settings.ProxyMode == ProxyMode.FreeCommunity &&
                    string.IsNullOrWhiteSpace(_ctx.Settings.ActiveFreeServerUrl))
                {
                    if (_cachedServers.Count > 0)
                    {
                        SelectFreeServer(_cachedServers[0], activateVpn: true);
                        return;
                    }
                    await ScanFreeServersAsync(autoConnectFirst: true);
                    return;
                }
            }

            ApplyLiveAndNotify();
        };

        _modeCombo.SelectedIndexChanged += async (_, _) =>
        {
            if (_syncingUi) return;
            int idx = Math.Clamp(_modeCombo.SelectedIndex, 0, ModeOrder.Length - 1);
            var chosen = ModeOrder[idx];
            _ctx.Settings.ProxyMode = chosen;

            if (chosen == ProxyMode.FreeCommunity && string.IsNullOrWhiteSpace(_ctx.Settings.ActiveFreeServerUrl))
            {
                _ctx.Settings.VpnEnabled = true;
                SyncControlsFromSettings();
                if (_cachedServers.Count > 0)
                {
                    SelectFreeServer(_cachedServers[0], activateVpn: true);
                    return;
                }
                await ScanFreeServersAsync(autoConnectFirst: true);
                return;
            }

            if (chosen is ProxyMode.CloudflareWarp or ProxyMode.Tor ||
                (chosen == ProxyMode.Custom && !string.IsNullOrWhiteSpace(_proxyBox.Text)))
            {
                _ctx.Settings.VpnEnabled = true;
            }

            SyncControlsFromSettings();
            ApplyLiveAndNotify();
        };

        _freeServerCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingUi) return;
            int idx = _freeServerCombo.SelectedIndex;
            if (idx > 0 && idx - 1 < _cachedServers.Count)
            {
                SelectFreeServer(_cachedServers[idx - 1], activateVpn: true);
            }
        };

        _scanBtn.Click += async (_, _) => await ScanFreeServersAsync(autoConnectFirst: true);

        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingUi) return;
            int idx = _profileCombo.SelectedIndex;
            if (idx <= 0)
            {
                _ctx.Settings.ActiveVpnProfileId = string.Empty;
            }
            else if (idx - 1 < _ctx.Settings.VpnProfiles.Count)
            {
                var p = _ctx.Settings.VpnProfiles[idx - 1];
                _ctx.Settings.ActiveVpnProfileId = p.Id;
                _ctx.Settings.CustomProxy = p.ProxyUrl;
                _ctx.Settings.ProxyMode = ProxyMode.Custom;
                _ctx.Settings.VpnEnabled = true;
                SyncControlsFromSettings();
                ApplyLiveAndNotify();
            }
        };
    }

    private void SelectFreeServer(FreeVpnServer srv, bool activateVpn)
    {
        _ctx.Settings.ActiveFreeServerUrl = srv.ProxyUrl;
        _ctx.Settings.ActiveFreeServerLabel = srv.DisplayText;
        _ctx.Settings.ActiveFreeServerCountry = srv.CountryCode;
        _ctx.Settings.ProxyMode = ProxyMode.FreeCommunity;
        if (activateVpn)
            _ctx.Settings.VpnEnabled = true;

        SyncControlsFromSettings();
        ApplyLiveAndNotify();
    }

    private async Task ScanFreeServersAsync(bool autoConnectFirst)
    {
        if (IsDisposed) return;
        _scanBtn.Enabled = false;
        _scanBtn.Text = "Taranıyor…";
        _heroStatusLbl.ForeColor = _p.Accent;
        _heroStatusLbl.Text = "Canlı ücretsiz VPN sunucuları doğrulanıyor…";

        try
        {
            var found = await ProxyService.DiscoverVerifiedFreeRelaysAsync(8);
            if (IsDisposed) return;

            if (found.Count > 0)
            {
                _cachedServers = found;
                PopulateFreeServerCombo();
                if (autoConnectFirst)
                    SelectFreeServer(found[0], activateVpn: true);
                else
                    UpdateHeroCardUi();
            }
            else
            {
                _heroStatusLbl.ForeColor = _p.Red;
                _heroStatusLbl.Text = "Ücretsiz sunucu yanıt vermedi (WARP / Tor / Özel VPN deneyin)";
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _scanBtn.Enabled = true;
                _scanBtn.Text = "Sunucu Bul";
            }
        }
    }

    private void ApplyLiveAndNotify()
    {
        _ctx.VpnRouter.ApplySettings(_ctx.Settings);
        _ctx.Storage.SaveSettings(_ctx.Settings);
        UpdateHeroCardUi();
        _onSettingsChanged();
    }

    private void SyncControlsFromSettings()
    {
        _syncingUi = true;
        try
        {
            var s = _ctx.Settings;
            _vpnToggle.Checked = s.VpnEnabled;
            int modeIdx = Array.IndexOf(ModeOrder, s.ProxyMode);
            if (modeIdx >= 0 && modeIdx < _modeCombo.Items.Count)
                _modeCombo.SelectedIndex = modeIdx;
            if (_proxyBox.Text != s.CustomProxy)
                _proxyBox.Text = s.CustomProxy;
            PopulateFreeServerCombo();
            UpdateHeroCardUi();
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void UpdateHeroCardUi()
    {
        var s = _ctx.Settings;
        _heroCard.BorderColor = s.VpnEnabled ? _p.Green : _p.Border;
        _heroCard.Invalidate();

        if (!s.VpnEnabled)
        {
            _heroStatusLbl.ForeColor = _p.TextMuted;
            _heroStatusLbl.Text = "Kapalı • Doğrudan bağlantı (açmak için sağdaki düğmeye dokun)";
            return;
        }

        _heroStatusLbl.ForeColor = _p.Green;
        _heroStatusLbl.Text = s.ProxyMode switch
        {
            ProxyMode.FreeCommunity when !string.IsNullOrEmpty(s.ActiveFreeServerLabel)
                => $"BAĞLI • {s.ActiveFreeServerLabel}",
            ProxyMode.FreeCommunity => "BAĞLI • Sunucu seçiliyor…",
            ProxyMode.CloudflareWarp => "BAĞLI • Cloudflare WARP (:40000)",
            ProxyMode.Tor => "BAĞLI • Tor Ağı (:9050)",
            ProxyMode.Custom => $"BAĞLI • {ProxyService.GetActiveVpnProfile(s)?.Name ?? (string.IsNullOrEmpty(s.CustomProxy) ? "Özel Profil" : s.CustomProxy)}",
            _ => "BAĞLI"
        };
    }

    private async Task RunIpTraceTestAsync()
    {
        _ctx.VpnRouter.ApplySettings(_ctx.Settings);
        _ipResultLbl.Text = "Test ediliyor…";
        _ipResultLbl.ForeColor = _p.Accent;
        var res = await ProxyService.FetchTraceAsync(_ctx.Settings, _ctx.VpnRouter.ListenPort);
        if (IsDisposed) return;
        _ipResultLbl.ForeColor = res.Success ? _p.Green : _p.Red;
        _ipResultLbl.Text = res.Success ? $"{res.Ip} ({res.Location}) • {res.TunnelLabel}" : "Tünel yanıt vermedi";
    }

    private void OpenUrlExternalSafe(string url)
    {
        SafeClose();
        if (_ownerForm is { IsDisposed: false, IsHandleCreated: true })
            _ownerForm.BeginInvoke(() => _openTabAction(url));
        else
            _openTabAction(url);
    }

    private void PopulateFreeServerCombo()
    {
        bool prev = _syncingUi;
        _syncingUi = true;
        try
        {
            _freeServerCombo.Items.Clear();
            if (_cachedServers.Count == 0)
            {
                _freeServerCombo.Items.Add(string.IsNullOrEmpty(_ctx.Settings.ActiveFreeServerLabel)
                    ? "— 'Sunucu Bul' ile canlı sunucu tara —"
                    : $"Kayıtlı: {_ctx.Settings.ActiveFreeServerLabel}");
                _freeServerCombo.SelectedIndex = 0;
                return;
            }

            _freeServerCombo.Items.Add($"— Doğrulanmış {_cachedServers.Count} Sunucu —");
            int sel = 0;
            for (int i = 0; i < _cachedServers.Count; i++)
            {
                var srv = _cachedServers[i];
                _freeServerCombo.Items.Add(srv.DisplayText);
                if (srv.ProxyUrl.Equals(_ctx.Settings.ActiveFreeServerUrl, StringComparison.OrdinalIgnoreCase))
                    sel = i + 1;
            }
            _freeServerCombo.SelectedIndex = sel;
        }
        finally
        {
            _syncingUi = prev;
        }
    }

    public void PopulatePreviewSample()
    {
        _cachedServers = new List<FreeVpnServer>
        {
            new("socks5://de.example:1080", "socks5", "DE", "Almanya", 84, "198.51.100.10"),
            new("socks5://nl.example:1080", "socks5", "NL", "Hollanda", 96, "198.51.100.11"),
            new("http://us.example:8080", "http", "US", "ABD", 142, "198.51.100.12")
        };
        _syncingUi = true;
        _vpnToggle.Checked = true;
        _modeCombo.SelectedIndex = 0;
        PopulateFreeServerCombo();
        _freeServerCombo.SelectedIndex = 1;
        _heroCard.BorderColor = _p.Green;
        _heroStatusLbl.ForeColor = _p.Green;
        _heroStatusLbl.Text = "BAĞLI • DE — Almanya (84 ms) • SOCKS5";
        _tunnelProbeLbl.Text = "WARP: Açık (:40000)   •   Tor: Kapalı";
        _tunnelProbeLbl.ForeColor = _p.Green;
        _ipResultLbl.Text = "198.51.100.10 (DE) • Ücretsiz VPN (DE)";
        _ipResultLbl.ForeColor = _p.Green;
        _syncingUi = false;
    }

    public void SimulateVpnToggleForTest(bool enabled)
    {
        _vpnToggle.Checked = enabled;
        _ctx.Settings.VpnEnabled = enabled;
        _ctx.VpnRouter.ApplySettings(_ctx.Settings);
        UpdateHeroCardUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && !_comboDropDownOpen)
        {
            SafeClose();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void SafeClose()
    {
        if (_closing || IsDisposed) return;
        _closing = true;
        _outsideClickTimer.Stop();
        Close();
    }

    private async Task ProbeLocalTunnelsAsync()
    {
        bool warp = await ProxyService.CheckLocalPortAsync("127.0.0.1", 40000);
        bool tor9050 = await ProxyService.CheckLocalPortAsync("127.0.0.1", 9050);
        bool tor9150 = !tor9050 && await ProxyService.CheckLocalPortAsync("127.0.0.1", 9150);
        if (IsDisposed) return;

        string warpTxt = warp ? "WARP: Açık (:40000)" : "WARP: Kapalı";
        string torTxt = tor9050 ? "Tor: Açık (:9050)" : tor9150 ? "Tor: Açık (:9150)" : "Tor: Kapalı";
        _tunnelProbeLbl.Text = $"{warpTxt}   •   {torTxt}";
        _tunnelProbeLbl.ForeColor = (warp || tor9050 || tor9150) ? _p.Green : _p.TextMuted;
    }

    private void PopulateProfilesCombo()
    {
        bool prev = _syncingUi;
        _syncingUi = true;
        try
        {
            _profileCombo.Items.Clear();
            _profileCombo.Items.Add("— Özel VPN Profili Seç —");
            int sel = 0;
            for (int i = 0; i < _ctx.Settings.VpnProfiles.Count; i++)
            {
                var vp = _ctx.Settings.VpnProfiles[i];
                _profileCombo.Items.Add($"{vp.Name} ({vp.ProxyUrl})");
                if (vp.Id == _ctx.Settings.ActiveVpnProfileId)
                    sel = i + 1;
            }
            _profileCombo.SelectedIndex = sel;
        }
        finally
        {
            _syncingUi = prev;
        }
    }

    private void AddVpnProfileInline()
    {
        _suppressOutsideDismiss = true;
        try
        {
            var name = PromptDialog.Show(this, "VPN Profili Ekle", "Profil adı (ör: Almanya WireGuard/SOCKS5):", "", _p);
            if (string.IsNullOrWhiteSpace(name)) return;
            var addr = PromptDialog.Show(this, "VPN / Proxy Adresi", "Proxy adresi (ör: socks5://127.0.0.1:1080):", _proxyBox.Text.Trim(), _p);
            if (string.IsNullOrWhiteSpace(addr)) return;

            var prof = new VpnProfile { Name = name.Trim(), ProxyUrl = addr.Trim() };
            _ctx.Settings.VpnProfiles.Add(prof);
            _ctx.Settings.ActiveVpnProfileId = prof.Id;
            _ctx.Settings.ProxyMode = ProxyMode.Custom;
            _ctx.Settings.CustomProxy = prof.ProxyUrl;
            _ctx.Settings.VpnEnabled = true;
            PopulateProfilesCombo();
            SyncControlsFromSettings();
            ApplyLiveAndNotify();
        }
        finally
        {
            _suppressOutsideDismiss = false;
        }
    }

    private void DeleteActiveVpnProfile()
    {
        int idx = _profileCombo.SelectedIndex;
        if (idx <= 0 || idx - 1 >= _ctx.Settings.VpnProfiles.Count) return;
        _ctx.Settings.VpnProfiles.RemoveAt(idx - 1);
        _ctx.Settings.ActiveVpnProfileId = string.Empty;
        _ctx.Storage.SaveSettings(_ctx.Settings);
        PopulateProfilesCombo();
        ApplyLiveAndNotify();
    }

    private int AddSectionTitle(Panel host, string text, int y, int padX, int width, Func<int, int> S)
    {
        host.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(padX, y),
            Size = new Size(width, S(20)),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = _p.Accent,
            UseMnemonic = false
        });
        return y + S(23);
    }

    private int AddDivider(Panel host, int y, int padX, int width, Func<int, int> S)
    {
        host.Controls.Add(new Panel { Location = new Point(padX, y + S(2)), Size = new Size(width, 1), BackColor = _p.Border });
        return y + S(9);
    }

    private (ModernToggleSwitch Toggle, int NextY) AddToggleRow(
        Panel host, string label, bool checkedVal, int y, int padX, int width, Func<int, int> S, Action<bool> onChanged)
    {
        int togW = S(44);
        int togH = S(22);
        var lbl = new Label
        {
            Text = label,
            Location = new Point(padX, y),
            Size = new Size(width - togW - S(8), S(25)),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = _p.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        var tog = new ModernToggleSwitch
        {
            Checked = checkedVal,
            Location = new Point(padX + width - togW, y + S(1)),
            Size = new Size(togW, togH),
            OnColorStart = _p.Accent,
            OnColorEnd = _p.AccentPurple,
            OffTrackColor = _p.Border
        };
        tog.CheckedChanged += (_, _) => onChanged(tog.Checked);
        host.Controls.AddRange(new Control[] { lbl, tog });
        return (tog, y + S(27));
    }

    private ComboBox MakeCombo(Point loc, int width, string[] items, int selectedIndex)
    {
        var cb = new ComboBox
        {
            Location = loc,
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = (int)Math.Round(24 * _sc),
            BackColor = _p.BaseBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 8.5f),
            FlatStyle = FlatStyle.Flat
        };
        cb.DropDown += (_, _) => _comboDropDownOpen = true;
        cb.DropDownClosed += (_, _) =>
        {
            _comboDropDownOpen = false;
            _lastComboCloseUtc = DateTime.UtcNow;
        };
        cb.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using var br = new SolidBrush(sel ? _p.HoverBg : _p.BaseBg);
            e.Graphics.FillRectangle(br, e.Bounds);
            TextRenderer.DrawText(e.Graphics, cb.Items[e.Index]?.ToString() ?? "", cb.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
                _p.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        if (items.Length > 0)
        {
            cb.Items.AddRange(items);
            cb.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        }
        return cb;
    }

    private IconButton MakeSmallBtn(string text, Point loc, int width, int height, EventHandler onClick)
    {
        var b = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = text,
            Location = loc,
            Size = new Size(width, height),
            CornerRadius = 6,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            NormalBg = _p.ElevatedBg,
            HoverBg = _p.HoverBg,
            IconColor = _p.TextPrimary,
            BorderColor = _p.Border
        };
        b.Click += onClick;
        return b;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _outsideClickTimer.Dispose();
        base.Dispose(disposing);
    }
}
