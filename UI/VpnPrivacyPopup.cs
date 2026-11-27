using System.Runtime.InteropServices;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

/// <summary>
/// Opera/Brave-style floating VPN, Proxy, DoH & Privacy Shield popup panel
/// anchored to the omnibox VPN badge, with live WARP/Tor port detection, saved VPN profiles,
/// exit-IP test, one-click Store VPN extensions link, and instant privacy toggles.
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

    private readonly Form? _ownerForm;
    private readonly BrowserContext _ctx;
    private readonly ThemePalette _p;
    private readonly Action _onSettingsChanged;
    private readonly Action _onRestartRequested;
    private readonly Action<string> _openTabAction;
    private readonly System.Windows.Forms.Timer _outsideClickTimer;
    private readonly float _sc;
    private bool _closing;
    private bool _suppressOutsideDismiss;

    private readonly ComboBox _modeCombo;
    private readonly ComboBox _profileCombo;
    private readonly RoundedTextBox _proxyBox;
    private readonly ComboBox _dohCombo;
    private readonly Label _tunnelProbeLbl;
    private readonly Label _ipResultLbl;

    private readonly ModernToggleSwitch _adToggle;
    private readonly ModernToggleSwitch _httpsToggle;
    private readonly ModernToggleSwitch _stripToggle;
    private readonly ModernToggleSwitch _webrtcToggle;
    private readonly ModernToggleSwitch _fpToggle;

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
        Action onRestartRequested,
        Action<string> openTabAction,
        bool enableOutsideDismiss = true)
    {
        _ownerForm = ownerForm;
        _ctx = ctx;
        _p = palette;
        _onSettingsChanged = onSettingsChanged;
        _onRestartRequested = onRestartRequested;
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

        int popupW = S(388);
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

        // ── Header Card ───────────────────────────────────────────────────────────
        int headH = S(52);
        var headCard = new ModernCardPanel
        {
            Location = new Point(padX, y),
            Size = new Size(innerW, headH),
            FillColor = _p.ElevatedBg,
            BorderColor = _p.Border
        };
        var titleLbl = new Label
        {
            Text = "VPN & Gizlilik Kalkanı",
            Location = new Point(S(12), S(7)),
            Size = new Size(innerW - S(112), S(20)),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        var subLbl = new Label
        {
            Text = $"Engellenen reklam/izleyici: {_ctx.AdBlocker.TotalBlockedCount}",
            Location = new Point(S(12), S(28)),
            Size = new Size(innerW - S(112), S(18)),
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = _p.TextMuted,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        bool tunnelActive = s.ProxyMode is ProxyMode.CloudflareWarp or ProxyMode.Tor or ProxyMode.Custom;
        var badge = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = s.ProxyMode switch
            {
                ProxyMode.Tor => "TOR AĞI",
                ProxyMode.CloudflareWarp => "WARP VPN",
                ProxyMode.Custom => "ÖZEL VPN",
                ProxyMode.Direct => "DOĞRUDAN",
                _ => "SİSTEM"
            },
            Location = new Point(innerW - S(96), S(13)),
            Size = new Size(S(84), S(26)),
            CornerRadius = 12,
            Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
            NormalBg = _p.SurfaceBg,
            HoverBg = _p.SurfaceBg,
            IconColor = tunnelActive ? _p.Green : _p.Accent,
            BorderColor = tunnelActive ? _p.Green : _p.Border
        };
        headCard.Controls.AddRange(new Control[] { titleLbl, subLbl, badge });
        host.Controls.Add(headCard);
        y += headH + S(10);

        // ── Section 1: VPN & Tünel ────────────────────────────────────────────────
        y = AddSectionTitle(host, "VPN / Proxy Tüneli (Yeniden Başlatmada)", y, padX, innerW, S);

        _modeCombo = MakeCombo(new Point(padX, y), innerW, new[]
        {
            "Doğrudan Bağlantı (Proxy Yok)",
            "Sistem Proxy (Windows Varsayılanı)",
            "Özel VPN / SOCKS5 / HTTPS Proxy",
            "Tor Ağı (127.0.0.1:9050 / 9150)",
            "Cloudflare WARP Yerel Tüneli (127.0.0.1:40000)"
        }, (int)s.ProxyMode);
        host.Controls.Add(_modeCombo);
        y += S(34);

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

        // Saved VPN Profile selector + Add / Delete
        int addW = S(62);
        int delW = S(44);
        int gap = S(6);
        int profW = innerW - addW - delW - gap * 2;
        _profileCombo = MakeCombo(new Point(padX, y), profW, Array.Empty<string>(), 0);
        var addProfBtn = MakeSmallBtn("+ Ekle", new Point(padX + profW + gap, y), addW, S(28), (_, _) => AddVpnProfileInline());
        var delProfBtn = MakeSmallBtn("Sil", new Point(padX + profW + addW + gap * 2, y), delW, S(28), (_, _) => DeleteActiveVpnProfile());
        host.Controls.AddRange(new Control[] { _profileCombo, addProfBtn, delProfBtn });
        y += S(34);

        _proxyBox = new RoundedTextBox
        {
            Location = new Point(padX, y),
            Size = new Size(innerW, S(34)),
            PlaceholderText = "socks5://127.0.0.1:1080 veya https://sunucu:port",
            Text = s.CustomProxy
        };
        _proxyBox.ApplyTheme(_p, elevated: false);
        host.Controls.Add(_proxyBox);
        y += S(40);

        PopulateProfilesCombo();
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            int idx = _profileCombo.SelectedIndex;
            if (idx <= 0)
            {
                _ctx.Settings.ActiveVpnProfileId = string.Empty;
            }
            else if (idx - 1 < _ctx.Settings.VpnProfiles.Count)
            {
                var p = _ctx.Settings.VpnProfiles[idx - 1];
                _ctx.Settings.ActiveVpnProfileId = p.Id;
                _proxyBox.Text = p.ProxyUrl;
                _modeCombo.SelectedIndex = (int)ProxyMode.Custom;
            }
        };

        var testIpBtn = new ModernButton
        {
            Text = "IP Test Et",
            Location = new Point(padX, y),
            Height = S(30)
        };
        testIpBtn.ApplyTheme(_p);
        testIpBtn.FitWidthToText(96, 22);
        _ipResultLbl = new Label
        {
            Text = "Çıkış IP henüz test edilmedi",
            Location = new Point(testIpBtn.Right + S(10), y),
            Size = new Size(Math.Max(S(80), innerW - testIpBtn.Width - S(10)), S(30)),
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = _p.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        testIpBtn.Click += async (_, _) =>
        {
            ApplyFieldsToSettings();
            _ipResultLbl.Text = "Test ediliyor…";
            _ipResultLbl.ForeColor = _p.Accent;
            var res = await ProxyService.FetchTraceAsync(_ctx.Settings);
            _ipResultLbl.ForeColor = res.Success ? _p.Green : _p.Red;
            _ipResultLbl.Text = res.Success ? $"{res.Ip} ({res.Location}) • {res.TunnelLabel}" : "Tünel/bağlantı başarısız";
        };
        host.Controls.AddRange(new Control[] { testIpBtn, _ipResultLbl });
        y += S(38);

        // Apply/Restart + VPN Store Extension buttons
        int halfBtnGap = S(8);
        int restartBtnW = (int)(innerW * 0.56);
        int storeBtnW = innerW - restartBtnW - halfBtnGap;
        var restartBtn = new ModernButton
        {
            Text = "Kaydet & Yeniden Başlat",
            Primary = true,
            Location = new Point(padX, y),
            Size = new Size(restartBtnW, S(32))
        };
        restartBtn.ApplyTheme(_p);
        restartBtn.Click += (_, _) =>
        {
            ApplyFieldsToSettings();
            _ctx.Storage.SaveSettings(_ctx.Settings);
            SafeClose();
            if (_ownerForm is { IsDisposed: false, IsHandleCreated: true })
                _ownerForm.BeginInvoke(_onRestartRequested);
            else
                _onRestartRequested();
        };

        var storeVpnBtn = new ModernButton
        {
            Text = "VPN Eklentisi Kur",
            Location = new Point(padX + restartBtnW + halfBtnGap, y),
            Size = new Size(storeBtnW, S(32))
        };
        storeVpnBtn.ApplyTheme(_p);
        storeVpnBtn.Click += (_, _) =>
        {
            SafeClose();
            const string storeVpnUrl = "https://chromewebstore.google.com/search/vpn";
            if (_ownerForm is { IsDisposed: false, IsHandleCreated: true })
                _ownerForm.BeginInvoke(() => _openTabAction(storeVpnUrl));
            else
                _openTabAction(storeVpnUrl);
        };
        host.Controls.AddRange(new Control[] { restartBtn, storeVpnBtn });
        y += S(42);

        y = AddDivider(host, y, padX, innerW, S);

        // ── Section 2: Instant Privacy Shields ────────────────────────────────────
        y = AddSectionTitle(host, "Anlık Gizlilik Korumaları", y, padX, innerW, S);

        (_adToggle, y) = AddToggleRow(host, "Reklam & İzleyici Engelleyici (150+ ağ)", s.AdBlockEnabled, y, padX, innerW, S, v =>
        {
            _ctx.Settings.AdBlockEnabled = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
            _onSettingsChanged();
        });
        (_httpsToggle, y) = AddToggleRow(host, "HTTPS-Only (Otomatik HTTPS Yükseltme)", s.HttpsOnlyMode, y, padX, innerW, S, v =>
        {
            _ctx.Settings.HttpsOnlyMode = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });
        (_stripToggle, y) = AddToggleRow(host, "URL İzleme Temizleyici (utm / fbclid / gclid)", s.StripTrackingParams, y, padX, innerW, S, v =>
        {
            _ctx.Settings.StripTrackingParams = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });
        (_webrtcToggle, y) = AddToggleRow(host, "WebRTC Yerel IP Sızıntı Koruması*", s.WebRtcLeakProtection, y, padX, innerW, S, v =>
        {
            _ctx.Settings.WebRtcLeakProtection = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });
        (_fpToggle, y) = AddToggleRow(host, "Parmak İzi + GPC / DNT Başlıkları", s.FingerprintProtection, y, padX, innerW, S, v =>
        {
            _ctx.Settings.FingerprintProtection = v;
            _ctx.Settings.SendGpcAndDnt = v;
            _ctx.Storage.SaveSettings(_ctx.Settings);
        });

        int dohLblW = S(132);
        var dohLbl = new Label
        {
            Text = "DNS-over-HTTPS*:",
            Location = new Point(padX, y + S(5)),
            Size = new Size(dohLblW, S(24)),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = _p.TextSecondary,
            UseMnemonic = false
        };
        _dohCombo = MakeCombo(new Point(padX + dohLblW, y + S(2)), innerW - dohLblW, new[]
        {
            "Kapalı (Sistem DNS)",
            "Cloudflare (1.1.1.1)",
            "Quad9 Güvenli (9.9.9.9)",
            "AdGuard DNS (Reklamsız)"
        }, (int)s.DohProvider);
        _dohCombo.SelectedIndexChanged += (_, _) =>
        {
            _ctx.Settings.DohProvider = (DohProvider)Math.Clamp(_dohCombo.SelectedIndex, 0, 3);
            _ctx.Storage.SaveSettings(_ctx.Settings);
        };
        host.Controls.AddRange(new Control[] { dohLbl, _dohCombo });
        y += S(38);

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
            Deactivate += (_, _) => { if (!_suppressOutsideDismiss) SafeClose(); };
            _outsideClickTimer.Tick += (_, _) =>
            {
                if (_suppressOutsideDismiss || _closing || IsDisposed || !Visible) return;
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
        if (keyData == Keys.Escape)
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

    private void ApplyFieldsToSettings()
    {
        _ctx.Settings.ProxyMode = (ProxyMode)Math.Clamp(_modeCombo.SelectedIndex, 0, 4);
        _ctx.Settings.CustomProxy = _proxyBox.Text.Trim();
        _ctx.Settings.DohProvider = (DohProvider)Math.Clamp(_dohCombo.SelectedIndex, 0, 3);
    }

    private void PopulateProfilesCombo()
    {
        _profileCombo.Items.Clear();
        _profileCombo.Items.Add("— Kayıtlı VPN Profili Seç —");
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
            _ctx.Storage.SaveSettings(_ctx.Settings);
            PopulateProfilesCombo();
            _proxyBox.Text = prof.ProxyUrl;
            _modeCombo.SelectedIndex = (int)ProxyMode.Custom;
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
        return y + S(10);
    }

    private (ModernToggleSwitch Toggle, int NextY) AddToggleRow(
        Panel host, string label, bool checkedVal, int y, int padX, int width, Func<int, int> S, Action<bool> onChanged)
    {
        int togW = S(44);
        int togH = S(24);
        var lbl = new Label
        {
            Text = label,
            Location = new Point(padX, y),
            Size = new Size(width - togW - S(8), S(26)),
            Font = new Font("Segoe UI", 8.75f),
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
        return (tog, y + S(29));
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
            Font = new Font("Segoe UI", 8.75f),
            FlatStyle = FlatStyle.Flat
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
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
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
