using System.Diagnostics;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

public sealed class SettingsDialog : Form
{
    public bool Saved { get; private set; }

    private static readonly (string Name, string Url)[] Engines =
    {
        ("Google", "https://www.google.com/search?q="),
        ("Bing", "https://www.bing.com/search?q="),
        ("DuckDuckGo", "https://duckduckgo.com/?q="),
        ("Yahoo", "https://search.yahoo.com/search?p="),
        ("Yandex", "https://yandex.com.tr/search/?text="),
        ("Brave Search", "https://search.brave.com/search?q=")
    };

    private readonly BrowserContext _ctx;
    private readonly ThemePalette _p;
    private readonly Action _onImportClick;

    private readonly Panel _sidebar;
    private readonly Panel _contentHost;
    private readonly Panel _footer;
    private readonly List<IconButton> _navButtons = new();
    private readonly Panel[] _sections = new Panel[4];
    private readonly List<(Label Label, int ReservePx)> _wrappingLabels = new();

    // General controls
    private readonly RoundedTextBox _homeBox;
    private readonly ComboBox _searchCombo;
    private readonly ComboBox _themeCombo;

    // Privacy & Network controls
    private readonly ModernToggleSwitch _adToggle;
    private readonly ModernToggleSwitch _bmBarToggle;
    private readonly ModernToggleSwitch _dohToggle;
    private readonly ComboBox _proxyCombo;
    private readonly RoundedTextBox _customProxyBox;
    private readonly Label _ipStatusLbl;

    // Performance controls
    private readonly ModernToggleSwitch _hwaToggle;
    private readonly ModernToggleSwitch _bgtToggle;
    private readonly ModernToggleSwitch _imgToggle;
    private readonly NumericUpDown _sleepNum;
    private readonly NumericUpDown _deepUnloadNum;
    private readonly NumericUpDown _procLimitNum;

    // Update controls
    private readonly ModernToggleSwitch _updateStartupToggle;
    private readonly Label _updateStatusLbl;

    public SettingsDialog(BrowserContext ctx, ThemePalette palette, Action onImportClick)
    {
        _ctx = ctx;
        _p = palette;
        _onImportClick = onImportClick;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Ayarlar — ED Browser v4.1.0";
        Size = new Size(920, 720);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = _p.BaseBg;
        ForeColor = _p.TextPrimary;
        ThemeService.ApplyNativeTitleBarTheme(Handle, _ctx.Settings.Theme != BrowserTheme.Light);

        // 1. Left Navigation Sidebar
        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 216,
            BackColor = _p.SurfaceBg,
            Padding = new Padding(14, 18, 14, 14)
        };

        var brandPanel = new Panel { Dock = DockStyle.Top, Height = 64 };
        var brandTitle = new Label
        {
            Text = "ED Browser",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = _p.Accent,
            Dock = DockStyle.Top,
            Height = 28
        };
        var brandSub = new Label
        {
            Text = $"Sürüm {_ctx.Settings.Version}  •  Ayarlar",
            Font = new Font("Segoe UI", 8.75f),
            ForeColor = _p.TextMuted,
            Dock = DockStyle.Top,
            Height = 22
        };
        brandPanel.Controls.Add(brandSub);
        brandPanel.Controls.Add(brandTitle);

        var navHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        AddNavItem(navHost, 0, VectorIcon.Sliders, "Genel");
        AddNavItem(navHost, 1, VectorIcon.Shield, "Gizlilik & Ağ");
        AddNavItem(navHost, 2, VectorIcon.Bolt, "Performans & RAM");
        AddNavItem(navHost, 3, VectorIcon.Info, "Sistem & Hakkında");

        _sidebar.Controls.Add(navHost);
        _sidebar.Controls.Add(brandPanel);

        // 2. Sticky Footer Bar
        _footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = _p.SurfaceBg,
            Padding = new Padding(20, 12, 20, 12)
        };
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(_p.Border, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        var cancelBtn = new ModernButton
        {
            Text = "İptal",
            Dock = DockStyle.Right,
            Width = 104,
            DialogResult = DialogResult.Cancel
        };
        cancelBtn.ApplyTheme(_p);

        var spacer = new Panel { Dock = DockStyle.Right, Width = 10 };

        var saveBtn = new ModernButton
        {
            Text = "Değişiklikleri Kaydet",
            Primary = true,
            Dock = DockStyle.Right,
            Width = 196
        };
        saveBtn.ApplyTheme(_p);
        saveBtn.FitWidthToText(170, 36);
        saveBtn.Click += (_, _) => SaveAndClose();

        _footer.Controls.Add(saveBtn);
        _footer.Controls.Add(spacer);
        _footer.Controls.Add(cancelBtn);

        // 3. Right Content Host
        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _p.BaseBg,
            AutoScroll = true,
            Padding = new Padding(26, 20, 26, 20)
        };
        _contentHost.Resize += (_, _) => UpdateWrapWidths();

        // Initialize fields for the 4 sections
        var s = _ctx.Settings;

        _homeBox = CreateTextBox(s.HomePage, "https://www.google.com");
        _searchCombo = CreateCombo(Engines.Select(e => e.Name).ToArray(),
            Math.Max(0, Array.FindIndex(Engines, e => e.Url == s.SearchEngine)));
        _themeCombo = CreateCombo(new[] { "Karanlık (Dark)", "Aydınlık (Light)" },
            s.Theme == BrowserTheme.Light ? 1 : 0);

        _adToggle = CreateToggle(s.AdBlockEnabled);
        _bmBarToggle = CreateToggle(s.ShowBookmarkBar);
        _dohToggle = CreateToggle(s.UseCloudflareDoH);
        _proxyCombo = CreateCombo(new[]
        {
            "Doğrudan (Direct)",
            "Sistem Proxy (Windows)",
            "Özel Proxy (HTTP / SOCKS5)",
            "Tor Ağı (127.0.0.1:9050)"
        }, (int)s.ProxyMode);
        _customProxyBox = CreateTextBox(s.CustomProxy, "socks5://127.0.0.1:1080");
        _ipStatusLbl = new Label
        {
            Text = "Durum: Test edilmedi",
            Font = new Font("Segoe UI", 8.75f),
            ForeColor = _p.TextSecondary,
            AutoSize = true
        };

        _hwaToggle = CreateToggle(s.HardwareAcceleration);
        _bgtToggle = CreateToggle(s.BackgroundTabThrottle);
        _imgToggle = CreateToggle(s.LoadImages);
        _sleepNum = CreateNumeric(s.AutoSleepMinutes, 1, 120);
        _deepUnloadNum = CreateNumeric(s.DeepUnloadMinutes, 2, 240);
        _procLimitNum = CreateNumeric(s.RendererProcessLimit, 1, 8);
        _updateStartupToggle = CreateToggle(s.CheckUpdatesOnStartup);
        _updateStatusLbl = new Label
        {
            Text = $"Mevcut sürüm: v{s.Version}",
            Font = new Font("Segoe UI", 8.75f),
            ForeColor = _p.TextSecondary,
            AutoSize = true
        };

        _sections[0] = BuildGeneralSection();
        _sections[1] = BuildPrivacySection();
        _sections[2] = BuildPerformanceSection();
        _sections[3] = BuildSystemSection();

        foreach (var sec in _sections)
        {
            sec.Visible = false;
            _contentHost.Controls.Add(sec);
        }

        Controls.Add(_contentHost);
        Controls.Add(_sidebar);
        Controls.Add(_footer);

        UpdateWrapWidths();
        SelectSection(0);
    }

    private void UpdateWrapWidths()
    {
        int hostW = Math.Max(360, _contentHost.ClientSize.Width - _contentHost.Padding.Horizontal - 28);
        foreach (var (lbl, reserve) in _wrappingLabels)
        {
            int maxW = Math.Max(220, hostW - reserve);
            if (lbl.MaximumSize.Width != maxW)
                lbl.MaximumSize = new Size(maxW, 0);
        }
        foreach (var sec in _sections)
            sec?.PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeService.ApplyNativeTitleBarTheme(Handle, _ctx.Settings.Theme != BrowserTheme.Light);
    }

    private void AddNavItem(FlowLayoutPanel host, int idx, VectorIcon icon, string text)
    {
        var btn = new IconButton
        {
            IconType = icon,
            LabelText = text,
            Size = new Size(188, 38),
            CornerRadius = 8,
            Margin = new Padding(0, 2, 0, 2),
            NormalBg = _p.SurfaceBg,
            HoverBg = _p.HoverBg,
            IconColor = _p.TextSecondary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
        };
        btn.Click += (_, _) => SelectSection(idx);
        _navButtons.Add(btn);
        host.Controls.Add(btn);
    }

    public void SelectSection(int index)
    {
        for (int i = 0; i < _sections.Length; i++)
        {
            bool active = i == index;
            _sections[i].Visible = active;
            _navButtons[i].IsActive = active;
            _navButtons[i].NormalBg = active ? _p.ElevatedBg : _p.SurfaceBg;
            _navButtons[i].IconColor = active ? _p.Accent : _p.TextSecondary;
            _navButtons[i].BorderColor = active ? _p.Border : Color.Transparent;
            _navButtons[i].Invalidate();
        }
        UpdateWrapWidths();
        _contentHost.ScrollControlIntoView(_sections[index]);
    }

    private Panel CreateSectionContainer(string heading, string subtitle)
    {
        var sec = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 12)
        };

        var h = new Label
        {
            Text = heading,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        var sub = new Label
        {
            Text = subtitle,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 9.25f),
            ForeColor = _p.TextMuted,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 14)
        };
        _wrappingLabels.Add((sub, 0));

        sec.Controls.Add(sub);
        sec.Controls.Add(h);
        sec.Tag = sec;
        return sec;
    }

    private ModernCardPanel CreateCard(Panel sec, params Control[] rows)
    {
        var card = new ModernCardPanel
        {
            FillColor = _p.SurfaceBg,
            BorderColor = _p.Border,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 14, 18, 14)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].Dock = DockStyle.Top;
            rows[i].Margin = new Padding(0, i == 0 ? 0 : 10, 0, i == rows.Length - 1 ? 0 : 4);
            inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.Controls.Add(rows[i], 0, i);
        }

        card.Controls.Add(inner);

        var spacer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 14,
            BackColor = Color.Transparent
        };

        sec.Controls.Add(card);
        sec.Controls.SetChildIndex(card, 0);
        sec.Controls.Add(spacer);
        sec.Controls.SetChildIndex(spacer, 0);
        return card;
    }

    private Control MakeFieldRow(string title, string hint, Control input)
    {
        var tbl = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lbl = new Label
        {
            Text = title,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        };
        var desc = new Label
        {
            Text = hint,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = _p.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        _wrappingLabels.Add((desc, 48));
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 0, 0, 4);

        tbl.Controls.Add(lbl, 0, 0);
        tbl.Controls.Add(desc, 0, 1);
        tbl.Controls.Add(input, 0, 2);
        return tbl;
    }

    private Control MakeSwitchRow(string title, string subtitle, Control rightControl)
    {
        var tbl = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4)
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var textPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 16, 0)
        };
        var lbl = new Label
        {
            Text = title,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            Dock = DockStyle.Top,
            AutoSize = true
        };
        var sub = new Label
        {
            Text = subtitle,
            UseMnemonic = false,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = _p.TextMuted,
            Dock = DockStyle.Top,
            AutoSize = true
        };
        _wrappingLabels.Add((sub, 145));
        textPanel.Controls.Add(sub);
        textPanel.Controls.Add(lbl);

        rightControl.Anchor = AnchorStyles.Right;
        rightControl.Margin = new Padding(8, 6, 0, 6);

        tbl.Controls.Add(textPanel, 0, 0);
        tbl.Controls.Add(rightControl, 1, 0);
        return tbl;
    }

    private Panel BuildGeneralSection()
    {
        var sec = CreateSectionContainer("Genel", "Başlangıç sayfası, arama motoru ve görünüm temasını özelleştirin.");

        CreateCard(sec,
            MakeFieldRow("Ana Sayfa", "Yeni tarayıcı açıldığında veya Ana Sayfa butonuna basıldığında açılacak adres.", _homeBox),
            MakeFieldRow("Varsayılan Arama Motoru", "Adres çubuğuna URL yerine kelime yazıldığında kullanılır.", _searchCombo),
            MakeFieldRow("Tema ve Görünüm", "Karanlık ve aydınlık arayüz teması arasında geçiş yapın.", _themeCombo)
        );
        return sec;
    }

    private Panel BuildPrivacySection()
    {
        var sec = CreateSectionContainer("Gizlilik & Ağ", "Reklam engelleme, yer imi çubuğu, Cloudflare DoH ve proxy ayarları.");

        CreateCard(sec,
            MakeSwitchRow("Reklam & İzleyici Engelleyici", $"150+ reklam ve takipçi ağını sayfa yüklenmeden keser (bu oturum: {_ctx.AdBlocker.TotalBlockedCount} engelleme).", _adToggle),
            MakeSwitchRow("Yer İmi Çubuğunu Göster", "Üst araç çubuğunun hemen altında hızlı yer imi çubuğunu gösterir (Ctrl+Shift+B).", _bmBarToggle)
        );

        var testBtn = new ModernButton
        {
            Text = "IP / Bağlantıyı Test Et",
            Width = 175,
            Height = 32
        };
        testBtn.ApplyTheme(_p);
        testBtn.Click += async (_, _) =>
        {
            _ipStatusLbl.Text = "Test ediliyor…";
            var info = await ProxyService.FetchTraceAsync();
            _ipStatusLbl.Text = info.Success
                ? $"IP: {info.Ip}  •  Konum: {info.Location}"
                : "Bağlantı testi başarısız oldu.";
        };

        var testRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };
        _ipStatusLbl.Margin = new Padding(12, 7, 0, 0);
        testRow.Controls.Add(testBtn);
        testRow.Controls.Add(_ipStatusLbl);

        CreateCard(sec,
            MakeSwitchRow("Cloudflare DNS-over-HTTPS (1.1.1.1)", "DNS sorgularını şifreler (yeniden başlatmada uygulanır).", _dohToggle),
            MakeFieldRow("Proxy Modu (Yeniden başlatmada uygulanır)", "Tüm WebView2 ağ trafiğini seçilen proxy üzerinden geçirir.", _proxyCombo),
            MakeFieldRow("Özel Proxy Adresi", "Örnek: socks5://127.0.0.1:1080 veya http://ip:port", _customProxyBox),
            testRow
        );
        return sec;
    }

    private Panel BuildPerformanceSection()
    {
        var sec = CreateSectionContainer("Performans & RAM Optimizasyonu", "WebView2 süreç sınırları ve otomatik sekme uyutma ile belleği minimumda tutun.");

        CreateCard(sec,
            MakeSwitchRow("Donanım İvmesi (GPU)", "Video ve sayfa çizimini ekran kartına yükler (yeniden başlatmada uygulanır).", _hwaToggle),
            MakeSwitchRow("Arka Plan Sekme Bellek Kısıtlaması", "Pasif sekmelerde MemoryUsageTargetLevel.Low etkinleştirir.", _bgtToggle),
            MakeSwitchRow("Resimleri Yükle", "Kapalıyken görseller indirilmez; çok düşük RAM ve kota tasarrufu sağlar.", _imgToggle)
        );

        CreateCard(sec,
            MakeSwitchRow("Otomatik Sekme Uyutma (dakika)", "Pasif kalan sekmeleri TrySuspendAsync() ile bellekte dondurur.", _sleepNum),
            MakeSwitchRow("Derin Bellek Boşaltma (dakika)", "Uzun süre kullanılmayan sekmenin WebView2 örneğini tamamen kapatır (0 MB RAM).", _deepUnloadNum),
            MakeSwitchRow("Maksimum Renderer İşlemi (1–8)", "Chromium alt süreç sayısını sınırlar; düşük değer RAM'i azaltır (yeniden başlatmada uygulanır).", _procLimitNum)
        );
        return sec;
    }

    private Panel BuildSystemSection()
    {
        var sec = CreateSectionContainer("Sistem & Hakkında", "Varsayılan tarayıcı kaydı, veri aktarımı ve taşınabilir profil konumu.");

        var defBtn = new ModernButton { Text = "Windows Varsayılan Tarayıcı Yap", Primary = true, Height = 36 };
        defBtn.ApplyTheme(_p);
        defBtn.FitWidthToText(220, 34);
        defBtn.Click += (_, _) =>
        {
            var (ok, msg) = DefaultBrowserService.RegisterAndOpenSettings();
            MessageBox.Show(this, msg, ok ? "Başarılı" : "Hata", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        };

        var impBtn = new ModernButton { Text = "Chrome / Edge / Firefox'tan İçe Aktar", Height = 36 };
        impBtn.ApplyTheme(_p);
        impBtn.FitWidthToText(240, 34);
        impBtn.Click += (_, _) => _onImportClick();

        var openDataBtn = new ModernButton { Text = "Veri Klasörünü Aç", Height = 34, Dock = DockStyle.None };
        openDataBtn.ApplyTheme(_p);
        openDataBtn.FitWidthToText(150, 32);
        openDataBtn.Click += (_, _) =>
        {
            try { Process.Start("explorer.exe", _ctx.Storage.DataDirectory); } catch { }
        };
        var openRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 0) };
        openRow.Controls.Add(openDataBtn);

        var btnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent
        };
        defBtn.Margin = new Padding(0, 0, 10, 8);
        impBtn.Margin = new Padding(0, 0, 0, 8);
        btnRow.Controls.Add(defBtn);
        btnRow.Controls.Add(impBtn);

        var modeText = _ctx.Storage.IsInstalledMode ? "Kurulu Sürüm" : "Taşınabilir (Portable) Sürüm";
        var dataLbl = new Label
        {
            Text = $"Çalışma Modu: {modeText}\nVeri Klasörü: {_ctx.Storage.DataDirectory}",
            UseMnemonic = false,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = _p.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 8)
        };
        _wrappingLabels.Add((dataLbl, 48));

        CreateCard(sec, btnRow, dataLbl, openRow);

        var checkUpdateBtn = new ModernButton { Text = "Güncellemeleri Şimdi Denetle", Primary = true, Height = 34 };
        checkUpdateBtn.ApplyTheme(_p);
        checkUpdateBtn.FitWidthToText(210, 32);

        var dlUpdateBtn = new ModernButton { Text = "Yeni Sürümü İndir", Height = 34, Visible = false };
        dlUpdateBtn.ApplyTheme(_p);
        dlUpdateBtn.FitWidthToText(150, 28);
        string? latestReleaseLink = null;
        dlUpdateBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(latestReleaseLink))
                try { Process.Start(new ProcessStartInfo(latestReleaseLink) { UseShellExecute = true }); } catch { }
        };

        checkUpdateBtn.Click += async (_, _) =>
        {
            checkUpdateBtn.Enabled = false;
            _updateStatusLbl.ForeColor = _p.TextSecondary;
            _updateStatusLbl.Text = "GitHub üzerinden denetleniyor…";
            var res = await UpdateService.CheckForUpdatesAsync(_ctx.Settings.Version, _ctx.Storage.IsInstalledMode);
            checkUpdateBtn.Enabled = true;

            if (res.UpdateAvailable)
            {
                _updateStatusLbl.ForeColor = _p.Accent;
                _updateStatusLbl.Text = $"Yeni sürüm mevcut: v{res.LatestVersion}";
                latestReleaseLink = res.DirectDownloadUrl ?? res.ReleaseUrl;
                dlUpdateBtn.Visible = true;
            }
            else if (!string.IsNullOrEmpty(res.Error))
            {
                _updateStatusLbl.ForeColor = _p.Red;
                _updateStatusLbl.Text = "Denetleme hatası: " + res.Error;
            }
            else
            {
                _updateStatusLbl.ForeColor = _p.Green;
                _updateStatusLbl.Text = $"Güncelsiniz (v{res.CurrentVersion}).";
            }
        };

        var updRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };
        checkUpdateBtn.Margin = new Padding(0, 0, 10, 4);
        dlUpdateBtn.Margin = new Padding(0, 0, 10, 4);
        _updateStatusLbl.Margin = new Padding(2, 7, 0, 0);
        updRow.Controls.Add(checkUpdateBtn);
        updRow.Controls.Add(dlUpdateBtn);
        updRow.Controls.Add(_updateStatusLbl);

        CreateCard(sec,
            MakeSwitchRow("Başlangıçta Güncellemeleri Denetle", "Yeni bir ED Browser sürümü yayınlandığında tarayıcı içinde bildirim gösterir.", _updateStartupToggle),
            updRow);

        var aboutTitle = new Label
        {
            Text = $"ED Browser v{_ctx.Settings.Version}  —  Ultra Düşük RAM Sürümü",
            UseMnemonic = false,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = _p.Accent,
            AutoSize = true
        };
        var aboutDesc = new Label
        {
            Text = "Geliştirici: Erol Dizdar  •  Motor: Microsoft Edge WebView2 (Chromium) + Native WinForms GDI+",
            UseMnemonic = false,
            Font = new Font("Segoe UI", 9f),
            ForeColor = _p.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _wrappingLabels.Add((aboutDesc, 48));
        CreateCard(sec, aboutTitle, aboutDesc);
        return sec;
    }

    private RoundedTextBox CreateTextBox(string value, string placeholder)
    {
        var tb = new RoundedTextBox { Text = value, PlaceholderText = placeholder, Height = 36 };
        tb.ApplyTheme(_p, elevated: false);
        return tb;
    }

    private ComboBox CreateCombo(string[] items, int selectedIndex)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 26,
            BackColor = _p.BaseBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 9.5f),
            FlatStyle = FlatStyle.Flat,
            Height = 34
        };
        cb.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? _p.HoverBg : _p.BaseBg;
            using var br = new SolidBrush(bg);
            e.Graphics.FillRectangle(br, e.Bounds);
            TextRenderer.DrawText(e.Graphics, cb.Items[e.Index]?.ToString() ?? "", cb.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
                _p.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        cb.Items.AddRange(items);
        cb.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        return cb;
    }

    private ModernToggleSwitch CreateToggle(bool val)
    {
        return new ModernToggleSwitch
        {
            Checked = val,
            OnColorStart = _p.Accent,
            OnColorEnd = _p.AccentPurple,
            OffTrackColor = _p.Border
        };
    }

    private NumericUpDown CreateNumeric(int value, int min, int max)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 84,
            BackColor = _p.BaseBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private void SaveAndClose()
    {
        var s = _ctx.Settings;
        s.HomePage = string.IsNullOrWhiteSpace(_homeBox.Text) ? "https://www.google.com" : _homeBox.Text.Trim();
        s.SearchEngine = Engines[Math.Max(0, _searchCombo.SelectedIndex)].Url;
        s.Theme = _themeCombo.SelectedIndex == 1 ? BrowserTheme.Light : BrowserTheme.Dark;
        s.AdBlockEnabled = _adToggle.Checked;
        s.ShowBookmarkBar = _bmBarToggle.Checked;
        s.UseCloudflareDoH = _dohToggle.Checked;
        s.ProxyMode = (ProxyMode)Math.Clamp(_proxyCombo.SelectedIndex, 0, 3);
        s.CustomProxy = _customProxyBox.Text.Trim();
        s.HardwareAcceleration = _hwaToggle.Checked;
        s.BackgroundTabThrottle = _bgtToggle.Checked;
        s.LoadImages = _imgToggle.Checked;
        s.AutoSleepMinutes = (int)_sleepNum.Value;
        s.DeepUnloadMinutes = (int)_deepUnloadNum.Value;
        s.RendererProcessLimit = (int)_procLimitNum.Value;
        s.CheckUpdatesOnStartup = _updateStartupToggle.Checked;

        _ctx.Storage.SaveSettings(s);
        Saved = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
