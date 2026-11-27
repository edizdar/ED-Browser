using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

public sealed class MainForm : Form
{
    private readonly BrowserContext _ctx;
    private readonly bool _incognito;
    private readonly string? _incognitoDir;
    private readonly List<BrowserTab> _tabs = new();
    private BrowserTab? _activeTab;
    private ThemePalette _palette;

    // Layout chrome
    private readonly ModernTabBar _tabBar;
    private readonly Panel _toolbarPanel;
    private readonly IconButton _btnBack;
    private readonly IconButton _btnFwd;
    private readonly IconButton _btnReload;
    private readonly IconButton _btnHome;

    private readonly Panel _omnibox;
    private readonly IconButton _secBtn;
    private readonly TextBox _urlBox;
    private readonly IconButton _adShieldBtn;
    private readonly IconButton _bmStarBtn;
    private bool _omniboxFocused;

    private readonly IconButton _ramBtn;
    private readonly IconButton _pwBtn;
    private readonly IconButton _dlBtn;
    private readonly IconButton _bmBtn;
    private readonly IconButton _histBtn;
    private readonly IconButton _incBtn;
    private readonly IconButton _cfgBtn;
    private readonly IconButton _moreBtn;

    private readonly FlowLayoutPanel _bookmarkBar;
    private readonly Panel _contentHost;
    private readonly Panel _devToolsDockPanel;
    private readonly Splitter _devToolsSplitter;
    private readonly NewTabPanel _newTabPanel;
    private readonly SidePanelHost _sidePanel;

    private readonly ModernCardPanel _findBar;
    private readonly RoundedTextBox _findInput;

    private readonly Panel _pwBanner;
    private readonly Label _pwBannerText;
    private readonly ModernButton _pwBannerActionBtn;
    private readonly IconButton _pwDismissBtn;
    private Action? _pwBannerAction;

    private readonly Panel _updateBanner;
    private readonly Label _updateBannerText;
    private readonly ModernButton _updateDlBtn;
    private readonly ModernButton _updateNotesBtn;
    private readonly IconButton _updateDismissBtn;
    private string? _updateDownloadUrl;
    private string? _updateReleaseUrl;

    private NotifyIcon? _notifyIcon;
    private readonly System.Windows.Forms.Timer _memoryTimer;
    private System.Windows.Forms.Timer? _bmHoverTimer;
    private ContextMenuStrip? _activeBookmarkMenu;
    private Control? _activeBookmarkAnchor;
    private readonly System.Windows.Forms.Timer _devToolsSyncTimer;
    private IntPtr _devToolsHwnd;
    private bool _devToolsAutoDocked;
    private Point _bmDragStart;
    private bool _bmDragging;

    private bool _isFullscreen;
    private FormWindowState _preFsState;
    private FormBorderStyle _preFsBorder;
    private readonly List<IconButton> _toolbarButtons = new();

    public MainForm(BrowserContext ctx, bool incognito = false, string? initialUrl = null, bool skipInitialNavigate = false)
    {
        _ctx = ctx;
        _incognito = incognito;
        _incognitoDir = incognito
            ? Path.Combine(Path.GetTempPath(), $"edbrowser4_incog_{Guid.NewGuid():N}")
            : null;
        if (_incognitoDir is not null)
            Directory.CreateDirectory(_incognitoDir);

        _palette = ThemeService.GetPalette(_ctx.Settings.Theme, _incognito);

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = _incognito ? "ED Browser — Gizli Mod" : "ED Browser";
        Size = new Size(1360, 860);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        Icon = LoadEmbeddedIcon();

        // 1. Modern Tab Bar
        _tabBar = new ModernTabBar
        {
            IsIncognito = _incognito,
            Height = 40
        };
        _tabBar.TabSelected += id =>
        {
            var t = _tabs.FirstOrDefault(x => x.Id == id);
            if (t is not null) _ = SwitchToTabAsync(t);
        };
        _tabBar.TabClosed += id =>
        {
            var t = _tabs.FirstOrDefault(x => x.Id == id);
            if (t is not null) _ = CloseTabAsync(t);
        };
        _tabBar.TabRightClicked += (id, pt) =>
        {
            var t = _tabs.FirstOrDefault(x => x.Id == id);
            if (t is not null) ShowTabContextMenu(t, pt);
        };
        _tabBar.NewTabClicked += () => _ = AddTabAsync();

        // 2. Navigation Toolbar
        _toolbarPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 7, 10, 7)
        };
        _toolbarPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Border, 1f);
            e.Graphics.DrawLine(pen, 0, _toolbarPanel.Height - 1, _toolbarPanel.Width, _toolbarPanel.Height - 1);
        };

        var leftNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 1, 6, 0)
        };
        _btnBack = MakeNavBtn(VectorIcon.Back, "Geri (Alt+Sol)", (_, _) => _activeTab?.GoBack());
        _btnFwd = MakeNavBtn(VectorIcon.Forward, "İleri (Alt+Sağ)", (_, _) => _activeTab?.GoForward());
        _btnReload = MakeNavBtn(VectorIcon.Reload, "Yenile (F5)", (_, _) => _activeTab?.ReloadOrStop());
        _btnHome = MakeNavBtn(VectorIcon.Home, "Ana Sayfa", (_, _) => _ = NavigateActiveAsync(_ctx.Settings.HomePage));
        leftNav.Controls.AddRange(new Control[] { _btnBack, _btnFwd, _btnReload, _btnHome });

        // Center Pill Omnibox
        _omnibox = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 34,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(6, 0, 8, 0)
        };
        _omnibox.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1f, 1f, _omnibox.Width - 2f, _omnibox.Height - 2f);
            using var gp = GraphicsExt.CreateRoundedRect(r, r.Height / 2f);
            using (var bg = new SolidBrush(_palette.BaseBg))
                g.FillPath(bg, gp);
            if (_omniboxFocused)
            {
                using var glow = new Pen(Color.FromArgb(65, _palette.Accent), 3f);
                g.DrawPath(glow, gp);
            }
            using var pen = new Pen(_omniboxFocused ? _palette.Accent : _palette.Border, 1.2f);
            g.DrawPath(pen, gp);
        };

        _secBtn = new IconButton
        {
            IconType = VectorIcon.Globe,
            Dock = DockStyle.Left,
            Size = new Size(28, 30),
            IconSizePx = 14,
            CornerRadius = 14
        };
        _secBtn.SetTooltip("Bağlantı Güvenliği");

        _urlBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.75f),
            PlaceholderText = "Web'de ara veya adres yaz…",
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        _urlBox.GotFocus += (_, _) => { _omniboxFocused = true; _omnibox.Invalidate(); _urlBox.SelectAll(); };
        _urlBox.LostFocus += (_, _) => { _omniboxFocused = false; _omnibox.Invalidate(); };
        _urlBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = NavigateActiveAsync(_urlBox.Text);
                _contentHost.Focus();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                SyncAddressBar();
                _contentHost.Focus();
            }
        };
        BuildAddressBarContextMenu();

        _adShieldBtn = new IconButton
        {
            IconType = VectorIcon.Shield,
            Dock = DockStyle.Right,
            Size = new Size(30, 30),
            IconSizePx = 14,
            CornerRadius = 14
        };
        _adShieldBtn.Click += (_, _) =>
        {
            _ctx.Settings.AdBlockEnabled = !_ctx.Settings.AdBlockEnabled;
            _ctx.Storage.SaveSettings(_ctx.Settings);
            UpdateAdShieldBadge();
        };

        _bmStarBtn = new IconButton
        {
            IconType = VectorIcon.Star,
            Dock = DockStyle.Right,
            Size = new Size(30, 30),
            IconSizePx = 14,
            CornerRadius = 14
        };
        _bmStarBtn.SetTooltip("Yer İmi Ekle / Kaldır (Ctrl+D)");
        _bmStarBtn.Click += (_, _) => ToggleCurrentBookmark();

        _omnibox.Controls.Add(_urlBox);
        _omnibox.Controls.Add(_secBtn);
        _omnibox.Controls.Add(_adShieldBtn);
        _omnibox.Controls.Add(_bmStarBtn);

        _omnibox.Resize += (_, _) => LayoutOmniboxText();
        _urlBox.FontChanged += (_, _) => LayoutOmniboxText();

        // Right Toolbar
        var rightTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 1, 0, 0)
        };

        _ramBtn = new IconButton
        {
            IconType = VectorIcon.Bolt,
            LabelText = "-- MB",
            AutoWidthFromText = true,
            PillBorder = true,
            Height = 30,
            IconSizePx = 13,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 1, 6, 0)
        };
        _ramBtn.Click += async (_, _) => await OptimizeRamNowAsync();

        _pwBtn = MakeToolBtn(VectorIcon.Key, "Şifre Yöneticisi", (_, _) => OpenPasswordManager());
        _dlBtn = MakeToolBtn(VectorIcon.Download, "İndirilenler (Ctrl+Shift+J)", (_, _) => _sidePanel.Toggle("dl"));
        _bmBtn = MakeToolBtn(VectorIcon.Bookmark, "Yer İmleri", (_, _) => _sidePanel.Toggle("bm"));
        _histBtn = MakeToolBtn(VectorIcon.History, "Geçmiş", (_, _) => _sidePanel.Toggle("hist"));
        _incBtn = MakeToolBtn(VectorIcon.Incognito, "Gizli Mod (Ctrl+Shift+P)", (_, _) => OpenIncognitoWindow());
        _cfgBtn = MakeToolBtn(VectorIcon.Settings, "Ayarlar", (_, _) => OpenSettings());
        _moreBtn = MakeToolBtn(VectorIcon.MoreDots, "Diğer Araçlar ve Yakınlaştırma", (_, _) => ShowMoreMenu());

        rightTools.Controls.AddRange(new Control[]
        {
            _ramBtn, _pwBtn, _dlBtn, _bmBtn, _histBtn, _incBtn, _cfgBtn, _moreBtn
        });

        _toolbarPanel.Controls.Add(_omnibox);
        _toolbarPanel.Controls.Add(leftNav);
        _toolbarPanel.Controls.Add(rightTools);

        // 3. Bookmarks Bar
        _bookmarkBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(10, 3, 10, 3),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AllowDrop = true,
            Visible = _ctx.Settings.ShowBookmarkBar
        };
        _bookmarkBar.DragEnter += (_, e) =>
        {
            if (e.Data?.GetData(typeof(BookmarkItem)) is BookmarkItem)
                e.Effect = DragDropEffects.Move;
        };
        _bookmarkBar.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(BookmarkItem)) is not BookmarkItem dragged) return;
            SidePanelHost.RemoveBookmarkById(_ctx.Bookmarks, dragged.Id);
            _ctx.Bookmarks.Add(dragged);
            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
            RenderBookmarksBar();
            _sidePanel.RefreshIfOpen("bm");
        };

        // 4. Content Host + DevTools Dock + SidePanel + FindBar + Password Banner
        _contentHost = new Panel { Dock = DockStyle.Fill };
        _newTabPanel = new NewTabPanel();
        _newTabPanel.BindShortcuts(_ctx.Shortcuts);
        _newTabPanel.NavigateRequested += q => _ = NavigateActiveAsync(q);
        _newTabPanel.OpenNewTabRequested += u => _ = AddTabAsync(u);
        _newTabPanel.ShortcutsChanged += () => _ctx.Storage.SaveShortcuts(_ctx.Shortcuts);
        _contentHost.Controls.Add(_newTabPanel);

        _devToolsDockPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 460,
            Visible = false
        };
        _devToolsDockPanel.Resize += (_, _) => SyncDockedDevToolsBounds();
        _devToolsSplitter = new Splitter
        {
            Dock = DockStyle.Right,
            Width = 5,
            MinExtra = 320,
            MinSize = 260,
            Visible = false
        };

        _sidePanel = new SidePanelHost(_ctx);
        _sidePanel.NavigateRequested += url => _ = NavigateActiveAsync(url);
        _sidePanel.OpenNewTabRequested += url => _ = AddTabAsync(url);
        _sidePanel.BookmarksChanged += () => { RenderBookmarksBar(); SyncBookmarkStar(); };

        // Bottom Find Bar
        _findBar = new ModernCardPanel
        {
            Size = new Size(370, 48),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Visible = false,
            Padding = new Padding(8, 6, 8, 6)
        };
        _findInput = new RoundedTextBox
        {
            Location = new Point(10, 7),
            Size = new Size(236, 34),
            PlaceholderText = "Sayfada ara…"
        };
        _findInput.Inner.TextChanged += (_, _) => _ = _activeTab?.FindInPageAsync(_findInput.Text, true);
        _findInput.Inner.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = _activeTab?.FindInPageAsync(_findInput.Text, !e.Shift);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CloseFindBar();
            }
        };
        var findPrev = new IconButton { IconType = VectorIcon.Back, Location = new Point(254, 8), Size = new Size(32, 32), IconSizePx = 13 };
        findPrev.SetTooltip("Önceki");
        findPrev.Click += (_, _) => _ = _activeTab?.FindInPageAsync(_findInput.Text, false);
        var findNext = new IconButton { IconType = VectorIcon.Forward, Location = new Point(288, 8), Size = new Size(32, 32), IconSizePx = 13 };
        findNext.SetTooltip("Sonraki");
        findNext.Click += (_, _) => _ = _activeTab?.FindInPageAsync(_findInput.Text, true);
        var findClose = new IconButton { IconType = VectorIcon.Close, Location = new Point(324, 8), Size = new Size(32, 32), IconSizePx = 12 };
        findClose.SetTooltip("Kapat (ESC)");
        findClose.Click += (_, _) => CloseFindBar();
        _findBar.Controls.AddRange(new Control[] { _findInput, findPrev, findNext, findClose });

        // Bottom Password Autofill / Save Banner
        _pwBanner = new Panel
        {
            Height = 46,
            Dock = DockStyle.Bottom,
            Visible = false,
            Padding = new Padding(14, 7, 14, 7)
        };
        _pwBannerText = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.25f)
        };
        _pwBannerActionBtn = new ModernButton
        {
            Primary = true,
            Dock = DockStyle.Right,
            Width = 96,
            Height = 32
        };
        _pwBannerActionBtn.Click += (_, _) =>
        {
            _pwBanner.Visible = false;
            _pwBannerAction?.Invoke();
        };
        _pwDismissBtn = new IconButton
        {
            IconType = VectorIcon.Close,
            Dock = DockStyle.Right,
            Size = new Size(32, 32),
            IconSizePx = 12,
            Margin = new Padding(6, 0, 0, 0)
        };
        _pwDismissBtn.Click += (_, _) => _pwBanner.Visible = false;
        _pwBanner.Controls.Add(_pwBannerText);
        _pwBanner.Controls.Add(_pwBannerActionBtn);
        _pwBanner.Controls.Add(_pwDismissBtn);

        // Top Update Notification Banner
        _updateBanner = new Panel
        {
            Height = 42,
            Dock = DockStyle.Top,
            Visible = false,
            Padding = new Padding(14, 5, 14, 5)
        };
        _updateBannerText = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold)
        };
        _updateDlBtn = new ModernButton
        {
            Text = "İndir",
            Primary = true,
            Dock = DockStyle.Right,
            Width = 88,
            Height = 30
        };
        _updateDlBtn.Click += (_, _) =>
        {
            var target = _updateDownloadUrl ?? _updateReleaseUrl;
            if (!string.IsNullOrEmpty(target))
                _ = AddTabAsync(target);
            _updateBanner.Visible = false;
        };
        _updateNotesBtn = new ModernButton
        {
            Text = "Sürüm Notları",
            Dock = DockStyle.Right,
            Width = 114,
            Height = 30,
            Margin = new Padding(6, 0, 0, 0)
        };
        _updateNotesBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_updateReleaseUrl))
                _ = AddTabAsync(_updateReleaseUrl);
        };
        _updateDismissBtn = new IconButton
        {
            IconType = VectorIcon.Close,
            Dock = DockStyle.Right,
            Size = new Size(30, 30),
            IconSizePx = 12,
            Margin = new Padding(6, 0, 0, 0)
        };
        _updateDismissBtn.Click += (_, _) => _updateBanner.Visible = false;

        _updateBanner.Controls.Add(_updateBannerText);
        _updateBanner.Controls.Add(_updateNotesBtn);
        _updateBanner.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        _updateBanner.Controls.Add(_updateDlBtn);
        _updateBanner.Controls.Add(_updateDismissBtn);

        Controls.Add(_contentHost);
        Controls.Add(_devToolsSplitter);
        Controls.Add(_devToolsDockPanel);
        Controls.Add(_sidePanel);
        Controls.Add(_pwBanner);
        Controls.Add(_updateBanner);
        Controls.Add(_bookmarkBar);
        Controls.Add(_toolbarPanel);
        Controls.Add(_tabBar);

        Move += (_, _) => SyncDockedDevToolsBounds();
        Resize += (_, _) => SyncDockedDevToolsBounds();

        _devToolsSyncTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _devToolsSyncTimer.Tick += (_, _) => MonitorDevToolsWindow();
        _devToolsSyncTimer.Start();

        _contentHost.Controls.Add(_findBar);
        _contentHost.Resize += (_, _) =>
        {
            _findBar.Location = new Point(
                Math.Max(8, _contentHost.ClientSize.Width - _findBar.Width - 14),
                Math.Max(8, _contentHost.ClientSize.Height - _findBar.Height - 10));
        };

        ApplyTheme();
        RenderBookmarksBar();
        LayoutOmniboxText();

        _memoryTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _memoryTimer.Tick += async (_, _) => await OnMemoryTimerTickAsync();
        _memoryTimer.Start();

        Shown += async (_, _) =>
        {
            if (skipInitialNavigate)
            {
                await AddTabAsync(null);
            }
            else
            {
                var startUrl = !string.IsNullOrWhiteSpace(initialUrl)
                    ? initialUrl
                    : _ctx.Settings.HomePage;
                await AddTabAsync(startUrl);
            }
            RefreshMemoryBadge();
            if (!_incognito && !skipInitialNavigate && _ctx.Settings.CheckUpdatesOnStartup)
                _ = CheckUpdatesSilentlyAsync();
        };

        FormClosing += (_, _) => CleanupOnExit();
    }

    private void LayoutOmniboxText()
    {
        int left = _secBtn.Right + 4;
        int right = _adShieldBtn.Left - 4;
        int h = _urlBox.PreferredHeight;
        _urlBox.Location = new Point(left, Math.Max(2, (_omnibox.Height - h) / 2));
        _urlBox.Width = Math.Max(60, right - left);
    }

    private IconButton MakeNavBtn(VectorIcon icon, string tip, EventHandler onClick)
    {
        var b = new IconButton
        {
            IconType = icon,
            Size = new Size(32, 32),
            IconSizePx = 15,
            Margin = new Padding(1, 0, 1, 0)
        };
        b.SetTooltip(tip);
        b.Click += onClick;
        _toolbarButtons.Add(b);
        return b;
    }

    private IconButton MakeToolBtn(VectorIcon icon, string tip, EventHandler onClick)
    {
        var b = new IconButton
        {
            IconType = icon,
            Size = new Size(32, 32),
            IconSizePx = 15,
            Margin = new Padding(2, 0, 2, 0)
        };
        b.SetTooltip(tip);
        b.Click += onClick;
        _toolbarButtons.Add(b);
        return b;
    }

    private void BuildAddressBarContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            bool hasSel = _urlBox.SelectionLength > 0;
            if (hasSel)
            {
                menu.Items.Add("Kes", null, (_, _) => _urlBox.Cut());
                menu.Items.Add("Kopyala", null, (_, _) => _urlBox.Copy());
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("Yapıştır", null, (_, _) => _urlBox.Paste());
            menu.Items.Add("Yapıştır ve Git", null, (_, _) =>
            {
                var txt = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(txt))
                    _ = NavigateActiveAsync(txt);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Tümünü Seç", null, (_, _) => { _urlBox.Focus(); _urlBox.SelectAll(); });
            menu.Items.Add("Temizle", null, (_, _) => { _urlBox.Clear(); _urlBox.Focus(); });
        };
        _urlBox.ContextMenuStrip = menu;
    }

    private void ShowMoreMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        int zoomPct = (int)Math.Round((_activeTab?.ZoomFactor ?? 1.0) * 100);

        menu.Items.Add($"Yakınlaştır (+10%)   [{zoomPct}%]", null, (_, _) => ChangeZoom(0.10));
        menu.Items.Add("Uzaklaştır (-10%)", null, (_, _) => ChangeZoom(-0.10));
        menu.Items.Add("Yakınlaştırmayı Sıfırla (%100)", null, (_, _) => ResetZoom());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ekran Görüntüsü Al (Ctrl+Shift+S)", null, (_, _) => ShowScreenshotMenu());
        menu.Items.Add("Diğer Tarayıcıdan İçe Aktar", null, (_, _) => OpenImportDialog());
        menu.Items.Add("Sayfada Ara (Ctrl+F)", null, (_, _) => ToggleFindBar());
        menu.Items.Add("Yazdır (Ctrl+P)", null, (_, _) => _activeTab?.PrintPage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tam Ekran (F11)", null, (_, _) => ToggleFullscreen());
        menu.Items.Add("Geliştirici Araçları (F12 / Ctrl+Shift+I)", null, (_, _) => _ = ToggleDevToolsAsync());
        menu.Items.Add("Ayarlar", null, (_, _) => OpenSettings());

        menu.Show(_moreBtn, new Point(_moreBtn.Width - 220, _moreBtn.Height + 4));
    }

    // ── TAB MANAGEMENT ──────────────────────────────────────────────────────────
    public async Task AddTabAsync(string? url = null)
    {
        var tab = new BrowserTab(_ctx, _contentHost, _incognito, _incognitoDir);
        tab.StateChanged += t => BeginInvoke(() => OnTabStateChanged(t));
        tab.OpenNewTabRequested += (_t, u) => BeginInvoke(() => { _ = AddTabAsync(u); });
        tab.UrlNavigated += (t, u) => BeginInvoke(() => { _ = CheckPasswordFillAsync(t, u); });
        tab.DownloadStarted += (_, _) => BeginInvoke(() => _sidePanel.RefreshIfOpen("dl"));
        tab.DownloadUpdated += (_, _) => BeginInvoke(() => _sidePanel.RefreshIfOpen("dl"));
        tab.DownloadCompleted += (_, rec) => BeginInvoke(() => OnDownloadCompleted(rec));
        tab.PasswordOfferRequested += (_, payload) => BeginInvoke(() => ShowPasswordSaveOffer(payload));
        tab.AcceleratorForwarded += combo => BeginInvoke(() => HandleShortcut(combo));
        tab.FindRequested += () => BeginInvoke(ToggleFindBar);
        tab.BookmarkRequested += () => BeginInvoke(ToggleCurrentBookmark);

        _tabs.Add(tab);
        await SwitchToTabAsync(tab);

        if (!string.IsNullOrWhiteSpace(url))
        {
            _newTabPanel.Visible = false;
            await tab.NavigateAsync(_ctx.NormalizeUrlOrSearch(url));
        }
        else
        {
            _newTabPanel.FocusSearch();
        }
    }

    private async Task SwitchToTabAsync(BrowserTab tab)
    {
        if (_activeTab is not null && _activeTab != tab)
            _activeTab.Deactivate();

        _activeTab = tab;
        _pwBanner.Visible = false;

        if (tab.HasNavigated)
        {
            _newTabPanel.Visible = false;
            await tab.ActivateAsync();
        }
        else
        {
            _newTabPanel.Visible = true;
            _newTabPanel.BringToFront();
            _newTabPanel.FocusSearch();
        }

        _findBar.BringToFront();
        RenderTabs();
        SyncAddressBar();
        SyncNavButtons();
        SyncBookmarkStar();
        UpdateAdShieldBadge();
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        int idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        _tabs.RemoveAt(idx);
        tab.Dispose();

        if (_tabs.Count == 0)
        {
            await AddTabAsync();
            return;
        }

        if (_activeTab == tab)
        {
            var next = _tabs[Math.Min(idx, _tabs.Count - 1)];
            await SwitchToTabAsync(next);
        }
        else
        {
            RenderTabs();
        }
    }

    private void OnTabStateChanged(BrowserTab tab)
    {
        RenderTabs();
        if (tab == _activeTab)
        {
            SyncAddressBar();
            SyncNavButtons();
            SyncBookmarkStar();
            UpdateAdShieldBadge();
        }
    }

    private void RenderTabs()
    {
        _tabBar.SetTabs(_tabs.Select(t => new ModernTabItem
        {
            Id = t.Id,
            Title = string.IsNullOrWhiteSpace(t.Title) ? "Yeni Sekme" : t.Title,
            Active = t == _activeTab,
            Loading = t.IsLoading,
            Sleeping = t.IsSleeping,
            DeepUnloaded = t.IsDeepUnloaded
        }));
    }

    private void ShowTabContextMenu(BrowserTab tab, Point screenPos)
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        menu.Items.Add("Yeni Sekme", null, (_, _) => _ = AddTabAsync());
        if (tab != _activeTab && tab.HasNavigated)
        {
            menu.Items.Add(tab.IsSleeping ? "Sekmeyi Uyandır" : "Sekmeyi Uyut (RAM Tasarrufu)", null, async (_, _) =>
            {
                if (tab.IsSleeping) await SwitchToTabAsync(tab);
                else { await tab.TrySleepAsync(); RefreshMemoryBadge(); }
            });
        }
        menu.Items.Add("Sekmeyi Çoğalt", null, (_, _) => _ = AddTabAsync(tab.Url));
        menu.Items.Add("Diğer Sekmeleri Kapat", null, async (_, _) =>
        {
            foreach (var other in _tabs.Where(t => t != tab).ToList())
                await CloseTabAsync(other);
        });
        menu.Items.Add("Sekmeyi Kapat", null, (_, _) => _ = CloseTabAsync(tab));
        menu.Show(screenPos);
    }

    // ── NAVIGATION & TOOLBAR SYNC ───────────────────────────────────────────────
    private async Task NavigateActiveAsync(string input)
    {
        if (_activeTab is null) return;
        var target = _ctx.NormalizeUrlOrSearch(input);
        if (string.IsNullOrEmpty(target)) return;

        _newTabPanel.Visible = false;
        _pwBanner.Visible = false;
        await _activeTab.NavigateAsync(target);
        SyncAddressBar();
    }

    private void SyncAddressBar()
    {
        if (_activeTab is null) return;
        if (!_urlBox.Focused)
            _urlBox.Text = _activeTab.Url;

        var u = _activeTab.Url;
        if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _secBtn.IconType = VectorIcon.Lock;
            _secBtn.IconColor = _palette.Green;
        }
        else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            _secBtn.IconType = VectorIcon.Warning;
            _secBtn.IconColor = Color.FromArgb(245, 158, 11);
        }
        else
        {
            _secBtn.IconType = VectorIcon.Globe;
            _secBtn.IconColor = _palette.TextMuted;
        }
    }

    private void SyncNavButtons()
    {
        _btnBack.Enabled = _activeTab?.CanGoBack ?? false;
        _btnFwd.Enabled = _activeTab?.CanGoForward ?? false;
        _btnReload.IconType = (_activeTab?.IsLoading ?? false) ? VectorIcon.Stop : VectorIcon.Reload;
    }

    private void UpdateAdShieldBadge()
    {
        bool enabled = _ctx.Settings.AdBlockEnabled;
        _adShieldBtn.IconColor = enabled ? _palette.Green : _palette.TextMuted;
        _adShieldBtn.SetTooltip(enabled
            ? $"Reklam Engelleyici Aktif (Bu sekme: {_activeTab?.BlockedOnTab ?? 0} | Toplam: {_ctx.AdBlocker.TotalBlockedCount}) — Tıkla kapat"
            : "Reklam Engelleyici Kapalı — Tıkla aç");
    }

    // ── BOOKMARKS ───────────────────────────────────────────────────────────────
    private void SyncBookmarkStar()
    {
        var url = _activeTab?.Url ?? "";
        bool has = !string.IsNullOrEmpty(url) && ContainsBookmarkUrl(_ctx.Bookmarks, url);
        _bmStarBtn.IconType = has ? VectorIcon.StarFilled : VectorIcon.Star;
        _bmStarBtn.IconColor = has ? Color.FromArgb(250, 204, 21) : _palette.TextMuted;
    }

    private static bool ContainsBookmarkUrl(List<BookmarkItem> items, string url) =>
        items.Any(i => i.IsFolder
            ? ContainsBookmarkUrl(i.Children, url)
            : i.Url.Equals(url, StringComparison.OrdinalIgnoreCase));

    private static bool RemoveBookmarkByUrl(List<BookmarkItem> items, string url)
    {
        int idx = items.FindIndex(i => !i.IsFolder && i.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) { items.RemoveAt(idx); return true; }
        foreach (var item in items)
        {
            if (item.IsFolder && RemoveBookmarkByUrl(item.Children, url))
                return true;
        }
        return false;
    }

    private static BookmarkItem? FindBookmarkByUrl(List<BookmarkItem> items, string url)
    {
        foreach (var item in items)
        {
            if (!item.IsFolder && item.Url.Equals(url, StringComparison.OrdinalIgnoreCase))
                return item;
            if (item.IsFolder)
            {
                var found = FindBookmarkByUrl(item.Children, url);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private void ToggleCurrentBookmark()
    {
        var url = _activeTab?.Url ?? "";
        if (string.IsNullOrWhiteSpace(url)) return;

        var existing = FindBookmarkByUrl(_ctx.Bookmarks, url);
        using var dlg = new BookmarkEditDialog(
            _ctx.Bookmarks,
            existing,
            existing?.Name ?? _activeTab?.Title ?? url,
            existing?.Url ?? url,
            _palette,
            () =>
            {
                _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                RenderBookmarksBar();
                _sidePanel.RefreshIfOpen("bm");
            });

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
            SyncBookmarkStar();
            RenderBookmarksBar();
            _sidePanel.RefreshIfOpen("bm");
        }
    }

    private void RenderBookmarksBar()
    {
        _bookmarkBar.Visible = _ctx.Settings.ShowBookmarkBar;
        _bookmarkBar.Controls.Clear();
        if (!_bookmarkBar.Visible) return;

        foreach (var item in _ctx.Bookmarks)
        {
            var btn = new IconButton
            {
                IconType = item.IsFolder ? VectorIcon.Folder : VectorIcon.Bookmark,
                AutoWidthFromText = true,
                CornerRadius = 7,
                Height = 26,
                IconSizePx = 12,
                NormalBg = _palette.SurfaceBg,
                HoverBg = _palette.HoverBg,
                IconColor = item.IsFolder ? _palette.Accent : _palette.TextSecondary,
                BorderColor = item.IsFolder ? _palette.Border : Color.Transparent,
                Font = new Font("Segoe UI", 8.5f),
                Margin = new Padding(0, 0, 4, 0),
                AllowDrop = true,
                LabelText = string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name
            };
            btn.UpdateAutoWidth(170);

            if (item.IsFolder)
            {
                btn.Click += (_, _) =>
                {
                    if (_bmDragging) { _bmDragging = false; return; }
                    OpenBookmarkFolderDropdown(btn, item);
                };
                btn.MouseEnter += (_, _) =>
                {
                    if (_activeBookmarkMenu is { Visible: true } && _activeBookmarkAnchor != btn)
                        OpenBookmarkFolderDropdown(btn, item);
                };
            }
            else
            {
                btn.Click += (_, _) =>
                {
                    if (_bmDragging) { _bmDragging = false; return; }
                    _ = NavigateActiveAsync(item.Url);
                };
            }

            btn.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _bmDragStart = e.Location;
                    _bmDragging = false;
                }
            };
            btn.MouseMove += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && !_bmDragging &&
                    (Math.Abs(e.X - _bmDragStart.X) >= 6 || Math.Abs(e.Y - _bmDragStart.Y) >= 6))
                {
                    _bmDragging = true;
                    CloseBookmarkFolderDropdown();
                    btn.DoDragDrop(item, DragDropEffects.Move);
                    _bmDragging = false;
                }
            };
            btn.DragEnter += (_, e) =>
            {
                if (e.Data?.GetData(typeof(BookmarkItem)) is BookmarkItem dragged && dragged.Id != item.Id)
                {
                    e.Effect = DragDropEffects.Move;
                    btn.BorderColor = _palette.Accent;
                    btn.Invalidate();
                }
            };
            btn.DragOver += (_, e) =>
            {
                if (e.Data?.GetData(typeof(BookmarkItem)) is BookmarkItem dragged && dragged.Id != item.Id)
                    e.Effect = DragDropEffects.Move;
            };
            btn.DragLeave += (_, _) =>
            {
                btn.BorderColor = item.IsFolder ? _palette.Border : Color.Transparent;
                btn.Invalidate();
            };
            btn.DragDrop += (_, e) =>
            {
                btn.BorderColor = item.IsFolder ? _palette.Border : Color.Transparent;
                btn.Invalidate();
                if (e.Data?.GetData(typeof(BookmarkItem)) is not BookmarkItem dragged) return;
                if (dragged.Id == item.Id || (dragged.IsFolder && SidePanelHost.IsDescendant(dragged, item.Id)))
                    return;

                SidePanelHost.RemoveBookmarkById(_ctx.Bookmarks, dragged.Id);
                if (item.IsFolder)
                {
                    item.Children.Add(dragged);
                }
                else
                {
                    int idx = _ctx.Bookmarks.FindIndex(x => x.Id == item.Id);
                    _ctx.Bookmarks.Insert(Math.Max(0, idx), dragged);
                }
                _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                RenderBookmarksBar();
                _sidePanel.RefreshIfOpen("bm");
            };

            btn.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var ctx = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
                if (!item.IsFolder)
                {
                    ctx.Items.Add("Aç", null, (_, _) => _ = NavigateActiveAsync(item.Url));
                    ctx.Items.Add("Yeni Sekmede Aç", null, (_, _) => _ = AddTabAsync(item.Url));
                    ctx.Items.Add("URL Kopyala", null, (_, _) => Clipboard.SetText(item.Url));
                    ctx.Items.Add("Düzenle", null, (_, _) =>
                    {
                        using var dlg = new BookmarkEditDialog(_ctx.Bookmarks, item, palette: _palette);
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                            RenderBookmarksBar();
                            _sidePanel.RefreshIfOpen("bm");
                        }
                    });
                }
                else
                {
                    ctx.Items.Add("Yeniden Adlandır", null, (_, _) =>
                    {
                        var n = PromptDialog.Show(this, "Yeniden Adlandır", "Yeni klasör adı:", item.Name, _palette);
                        if (!string.IsNullOrWhiteSpace(n))
                        {
                            item.Name = n.Trim();
                            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                            RenderBookmarksBar();
                            _sidePanel.RefreshIfOpen("bm");
                        }
                    });
                }
                ctx.Items.Add("Sil", null, (_, _) =>
                {
                    SidePanelHost.RemoveBookmarkById(_ctx.Bookmarks, item.Id);
                    _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                    RenderBookmarksBar();
                    SyncBookmarkStar();
                    _sidePanel.RefreshIfOpen("bm");
                });
                ctx.Show(btn, e.Location);
            };

            _bookmarkBar.Controls.Add(btn);
        }
    }

    private void OpenBookmarkFolderDropdown(IconButton anchor, BookmarkItem folder)
    {
        if (_activeBookmarkMenu is { Visible: true } && _activeBookmarkAnchor == anchor)
        {
            CloseBookmarkFolderDropdown();
            return;
        }

        CloseBookmarkFolderDropdown();
        var menu = BuildFolderDropdown(folder);
        _activeBookmarkMenu = menu;
        _activeBookmarkAnchor = anchor;

        menu.Closed += (_, _) =>
        {
            if (_activeBookmarkMenu == menu)
            {
                _activeBookmarkMenu = null;
                _activeBookmarkAnchor = null;
                _bmHoverTimer?.Stop();
            }
        };

        menu.Show(anchor, new Point(0, anchor.Height + 2));

        _bmHoverTimer ??= new System.Windows.Forms.Timer { Interval = 120 };
        _bmHoverTimer.Tick -= OnBookmarkHoverTick;
        _bmHoverTimer.Tick += OnBookmarkHoverTick;
        _bmHoverTimer.Start();
    }

    private void OnBookmarkHoverTick(object? sender, EventArgs e)
    {
        if (_activeBookmarkMenu is null || !_activeBookmarkMenu.Visible || _activeBookmarkAnchor is null)
        {
            _bmHoverTimer?.Stop();
            return;
        }

        var pt = Cursor.Position;
        var anchorRect = _activeBookmarkAnchor.RectangleToScreen(_activeBookmarkAnchor.ClientRectangle);
        anchorRect.Inflate(4, 6);
        if (anchorRect.Contains(pt) || IsPointInsideMenuTree(_activeBookmarkMenu, pt))
            return;

        CloseBookmarkFolderDropdown();
    }

    private void CloseBookmarkFolderDropdown()
    {
        _bmHoverTimer?.Stop();
        if (_activeBookmarkMenu is { IsDisposed: false, Visible: true })
            _activeBookmarkMenu.Close();
        _activeBookmarkMenu = null;
        _activeBookmarkAnchor = null;
    }

    private static bool IsPointInsideMenuTree(ToolStripDropDown menu, Point screenPt)
    {
        if (!menu.Visible) return false;
        var r = menu.Bounds;
        r.Inflate(4, 4);
        if (r.Contains(screenPt)) return true;

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripDropDownItem ddi && ddi.HasDropDownItems && ddi.DropDown.Visible)
            {
                if (IsPointInsideMenuTree(ddi.DropDown, screenPt))
                    return true;
            }
        }
        return false;
    }

    private ContextMenuStrip BuildFolderDropdown(BookmarkItem folder)
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        void Fill(ToolStripItemCollection target, List<BookmarkItem> nodes)
        {
            if (nodes.Count == 0)
            {
                target.Add("(Boş klasör)").Enabled = false;
                return;
            }
            foreach (var n in nodes)
            {
                if (n.IsFolder)
                {
                    var sub = new ToolStripMenuItem($"📁 {n.Name}");
                    Fill(sub.DropDownItems, n.Children);
                    target.Add(sub);
                }
                else
                {
                    var mi = new ToolStripMenuItem(n.Name);
                    mi.Click += (_, _) => _ = NavigateActiveAsync(n.Url);
                    target.Add(mi);
                }
            }
        }
        Fill(menu.Items, folder.Children);
        return menu;
    }

    // ── PASSWORDS & AUTOFILL ────────────────────────────────────────────────────
    private async Task CheckPasswordFillAsync(BrowserTab tab, string url)
    {
        if (tab != _activeTab || string.IsNullOrWhiteSpace(url)) return;
        var suggestions = _ctx.Vault.GetSuggestionsForUrl(url);
        if (suggestions.Count == 0) return;
        if (!await tab.HasPasswordInputAsync()) return;

        var entry = suggestions[0];
        ShowPasswordBanner(
            $"🔐 {entry.Domain} için kayıtlı şifre: {entry.Username}",
            "Doldur",
            () => _ = tab.FillPasswordAsync(entry.Username, entry.Password));
    }

    private void ShowPasswordSaveOffer(string json)
    {
        if (_incognito) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var url = doc.RootElement.GetProperty("url").GetString() ?? "";
            var user = doc.RootElement.GetProperty("user").GetString() ?? "";
            var pass = doc.RootElement.GetProperty("pass").GetString() ?? "";
            var domain = PasswordVaultService.NormalizeDomain(url);
            if (string.IsNullOrEmpty(domain)) return;

            ShowPasswordBanner(
                $"💾 {domain} ({user}) şifresi yerel kasaya kaydedilsin mi?",
                "Kaydet",
                () => _ctx.Vault.SaveOrUpdate(null, domain, user, pass));
        }
        catch { }
    }

    private void ShowPasswordBanner(string text, string buttonText, Action onAction)
    {
        _pwBannerText.Text = text;
        _pwBannerActionBtn.Text = buttonText;
        _pwBannerAction = onAction;
        _pwBanner.Visible = true;
    }

    // ── SCREENSHOT ──────────────────────────────────────────────────────────────
    private void ShowScreenshotMenu()
    {
        if (_activeTab is null || !_activeTab.HasNavigated)
        {
            MessageBox.Show(this, "Ekran görüntüsü almak için önce bir web sayfası açın.", "Ekran Görüntüsü", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette) };
        menu.Items.Add("Tüm Sayfa", null, async (_, _) => await CaptureScreenshotAsync(areaSelect: false));
        menu.Items.Add("Alan Seç", null, async (_, _) => await CaptureScreenshotAsync(areaSelect: true));
        menu.Show(Cursor.Position);
    }

    private async Task CaptureScreenshotAsync(bool areaSelect)
    {
        if (_activeTab is null) return;
        var pngBytes = await _activeTab.CapturePreviewPngAsync();
        if (pngBytes is null || pngBytes.Length == 0) return;

        using var ms = new MemoryStream(pngBytes);
        using var fullBmp = new Bitmap(ms);
        Bitmap toSave = fullBmp;
        Bitmap? cropped = null;

        try
        {
            if (areaSelect && _activeTab.WebView is not null)
            {
                var screenBounds = _activeTab.WebView.RectangleToScreen(_activeTab.WebView.ClientRectangle);
                using var overlay = new ScreenshotOverlayForm(fullBmp, screenBounds);
                if (overlay.ShowDialog(this) != DialogResult.OK)
                    return;

                if (overlay.SelectedArea is Rectangle sel && !overlay.FullPageSelected)
                {
                    var scaleX = (double)fullBmp.Width / Math.Max(1, screenBounds.Width);
                    var scaleY = (double)fullBmp.Height / Math.Max(1, screenBounds.Height);
                    var srcRect = new Rectangle(
                        Math.Max(0, (int)(sel.X * scaleX)),
                        Math.Max(0, (int)(sel.Y * scaleY)),
                        Math.Max(1, (int)(sel.Width * scaleX)),
                        Math.Max(1, (int)(sel.Height * scaleY)));
                    srcRect.Intersect(new Rectangle(0, 0, fullBmp.Width, fullBmp.Height));
                    cropped = fullBmp.Clone(srcRect, fullBmp.PixelFormat);
                    toSave = cropped;
                }
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Ekran Görüntüsünü Kaydet",
                InitialDirectory = _ctx.Storage.DownloadsDirectory,
                FileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png",
                Filter = "PNG Görüntüsü|*.png"
            };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                toSave.Save(sfd.FileName, ImageFormat.Png);
                ShowToast("Ekran Görüntüsü Kaydedildi", Path.GetFileName(sfd.FileName));
            }
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    // ── RAM OPTIMIZER & MEMORY MONITOR ──────────────────────────────────────────
    private async Task OnMemoryTimerTickAsync()
    {
        var now = DateTime.UtcNow;
        var sleepThreshold = TimeSpan.FromMinutes(_ctx.Settings.AutoSleepMinutes);
        var deepThreshold = TimeSpan.FromMinutes(_ctx.Settings.DeepUnloadMinutes);

        foreach (var tab in _tabs.ToList())
        {
            if (tab == _activeTab || !tab.HasNavigated || tab.IsLoading)
                continue;

            var idle = now - tab.LastActiveUtc;
            if (idle >= deepThreshold && !tab.IsDeepUnloaded)
                tab.DeepUnload();
            else if (idle >= sleepThreshold && !tab.IsSleeping && !tab.IsDeepUnloaded)
                await tab.TrySleepAsync();
        }

        RefreshMemoryBadge();
    }

    private async Task OptimizeRamNowAsync()
    {
        int slept = 0;
        foreach (var tab in _tabs.ToList())
        {
            if (tab == _activeTab || !tab.HasNavigated || tab.IsSleeping || tab.IsDeepUnloaded)
                continue;
            if (await tab.TrySleepAsync())
                slept++;
        }
        MemoryManagerService.TrimHostWorkingSet();
        RefreshMemoryBadge();
        ShowToast("RAM Optimize Edildi", slept > 0
            ? $"{slept} pasif sekme uyutuldu ve bellek sıkıştırıldı."
            : "Bellek çalışma kümesi sıkıştırıldı.");
    }

    private void RefreshMemoryBadge()
    {
        var snap = MemoryManagerService.CaptureProcessTreeSnapshot();
        long totalMb = snap.TotalWorkingSetBytes / (1024 * 1024);
        _ramBtn.LabelText = $"{totalMb} MB";
        _ramBtn.SetTooltip($"Toplam RAM: {totalMb} MB ({snap.ProcessCount} işlem) | Host: {snap.HostWorkingSetBytes / (1024 * 1024)} MB\nTıkla: pasif sekmeleri uyut ve RAM temizle");
        _newTabPanel.UpdateStats(_ctx.AdBlocker.TotalBlockedCount, totalMb);
    }

    // ── ZOOM / FIND / FULLSCREEN / DIALOGS ──────────────────────────────────────
    private void ChangeZoom(double delta)
    {
        if (_activeTab is null) return;
        _activeTab.ApplyZoom(_activeTab.ZoomFactor + delta);
    }

    private void ResetZoom()
    {
        if (_activeTab is null) return;
        _activeTab.ApplyZoom(1.0);
    }

    private void ToggleFindBar()
    {
        _findBar.Visible = !_findBar.Visible;
        if (_findBar.Visible)
        {
            _findBar.BringToFront();
            _findInput.Inner.Focus();
            _findInput.Inner.SelectAll();
        }
        else
        {
            _ = _activeTab?.ClearFindSelectionAsync();
        }
    }

    private void CloseFindBar()
    {
        _findBar.Visible = false;
        _ = _activeTab?.ClearFindSelectionAsync();
    }

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _preFsState = WindowState;
            _preFsBorder = FormBorderStyle;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _tabBar.Visible = false;
            _toolbarPanel.Visible = false;
            _bookmarkBar.Visible = false;
        }
        else
        {
            ExitFullscreen();
        }
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        _isFullscreen = false;
        FormBorderStyle = _preFsBorder;
        WindowState = _preFsState;
        _tabBar.Visible = true;
        _toolbarPanel.Visible = true;
        _bookmarkBar.Visible = _ctx.Settings.ShowBookmarkBar;
    }

    private void OpenPasswordManager()
    {
        using var dlg = new PasswordManagerDialog(_ctx, _palette);
        dlg.ShowDialog(this);
    }

    private void OpenImportDialog()
    {
        using var dlg = new ImportDialog(_ctx, () =>
        {
            RenderBookmarksBar();
            _sidePanel.RefreshIfOpen("bm");
        }, _palette);
        dlg.ShowDialog(this);
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsDialog(_ctx, _palette, OpenImportDialog);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Saved)
        {
            _palette = ThemeService.GetPalette(_ctx.Settings.Theme, _incognito);
            ApplyTheme();
            RenderBookmarksBar();
            UpdateAdShieldBadge();
        }
    }

    private void OpenIncognitoWindow()
    {
        var incog = new MainForm(_ctx, incognito: true, initialUrl: _ctx.Settings.HomePage);
        incog.Show();
    }

    private void OnDownloadCompleted(DownloadRecord rec)
    {
        _sidePanel.RefreshIfOpen("dl");
        if (rec.State == "Completed" && !_incognito)
            ShowToast("İndirme Tamamlandı", rec.FileName);
    }

    private void ShowToast(string title, string body)
    {
        try
        {
            _notifyIcon ??= new NotifyIcon
            {
                Icon = Icon,
                Text = "ED Browser",
                Visible = false
            };
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(3500, title, body, ToolTipIcon.Info);
        }
        catch { }
    }

    // ── KEYBOARD SHORTCUTS ──────────────────────────────────────────────────────
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleShortcut(keyData))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool HandleShortcut(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;

        if (mods == Keys.Control && key == Keys.T) { _ = AddTabAsync(); return true; }
        if (mods == Keys.Control && key == Keys.W) { if (_activeTab is not null) _ = CloseTabAsync(_activeTab); return true; }
        if (mods == Keys.Control && key == Keys.L) { _urlBox.Focus(); _urlBox.SelectAll(); return true; }
        if ((mods == Keys.Control && key == Keys.R) || keyData == Keys.F5) { _activeTab?.ReloadOrStop(); return true; }
        if (mods == Keys.Alt && key == Keys.Left) { _activeTab?.GoBack(); return true; }
        if (mods == Keys.Alt && key == Keys.Right) { _activeTab?.GoForward(); return true; }
        if (mods == Keys.Control && key == Keys.D) { ToggleCurrentBookmark(); return true; }
        if (mods == Keys.Control && key == Keys.F) { ToggleFindBar(); return true; }
        if (mods == Keys.Control && key == Keys.P) { _activeTab?.PrintPage(); return true; }
        if (mods == Keys.Control && (key == Keys.Oemplus || key == Keys.Add)) { ChangeZoom(0.10); return true; }
        if (mods == Keys.Control && (key == Keys.OemMinus || key == Keys.Subtract)) { ChangeZoom(-0.10); return true; }
        if (mods == Keys.Control && (key == Keys.D0 || key == Keys.NumPad0)) { ResetZoom(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.P) { OpenIncognitoWindow(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.B)
        {
            _ctx.Settings.ShowBookmarkBar = !_ctx.Settings.ShowBookmarkBar;
            _ctx.Storage.SaveSettings(_ctx.Settings);
            RenderBookmarksBar();
            return true;
        }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.J) { _sidePanel.Toggle("dl"); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.S) { ShowScreenshotMenu(); return true; }
        if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
        if (keyData == Keys.F12 || (mods == (Keys.Control | Keys.Shift) && key == Keys.I))
        {
            _ = ToggleDevToolsAsync();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_isFullscreen) { ExitFullscreen(); return true; }
            if (_findBar.Visible) { CloseFindBar(); return true; }
            if (_sidePanel.Visible) { _sidePanel.HidePanel(); return true; }
        }
        if (mods == Keys.Control && key >= Keys.D1 && key <= Keys.D9)
        {
            int idx = key - Keys.D1;
            if (idx < _tabs.Count) _ = SwitchToTabAsync(_tabs[idx]);
            return true;
        }
        return false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool isDark = _incognito || _ctx.Settings.Theme == BrowserTheme.Dark;
        ThemeService.ApplyNativeTitleBarTheme(Handle, isDark);
    }

    // ── THEME & CLEANUP ─────────────────────────────────────────────────────────
    private void ApplyTheme()
    {
        bool isDark = _incognito || _ctx.Settings.Theme == BrowserTheme.Dark;
        if (IsHandleCreated)
            ThemeService.ApplyNativeTitleBarTheme(Handle, isDark);

        BackColor = _palette.BaseBg;
        ForeColor = _palette.TextPrimary;

        _tabBar.ApplyTheme(_palette);
        _toolbarPanel.BackColor = _palette.SurfaceBg;
        _omnibox.BackColor = _palette.SurfaceBg;
        _urlBox.BackColor = _palette.BaseBg;
        _urlBox.ForeColor = _palette.TextPrimary;

        foreach (var b in _toolbarButtons)
        {
            b.NormalBg = _palette.SurfaceBg;
            b.HoverBg = _palette.HoverBg;
            b.IconColor = _palette.TextSecondary;
        }

        _secBtn.NormalBg = _palette.BaseBg;
        _secBtn.HoverBg = _palette.ElevatedBg;
        _adShieldBtn.NormalBg = _palette.BaseBg;
        _adShieldBtn.HoverBg = _palette.ElevatedBg;
        _bmStarBtn.NormalBg = _palette.BaseBg;
        _bmStarBtn.HoverBg = _palette.ElevatedBg;

        _ramBtn.NormalBg = _palette.ElevatedBg;
        _ramBtn.HoverBg = _palette.HoverBg;
        _ramBtn.IconColor = _palette.Green;
        _ramBtn.BorderColor = _palette.Border;

        _bookmarkBar.BackColor = _palette.SurfaceBg;
        _contentHost.BackColor = _palette.BaseBg;
        _findBar.FillColor = _palette.SurfaceBg;
        _findBar.BorderColor = _palette.Border;
        _findInput.ApplyTheme(_palette, elevated: true);

        _pwBanner.BackColor = _palette.ElevatedBg;
        _pwBannerText.ForeColor = _palette.TextPrimary;
        _pwBannerActionBtn.ApplyTheme(_palette);
        _pwDismissBtn.NormalBg = _palette.ElevatedBg;
        _pwDismissBtn.HoverBg = _palette.HoverBg;
        _pwDismissBtn.IconColor = _palette.TextMuted;

        _updateBanner.BackColor = _palette.ElevatedBg;
        _updateBannerText.ForeColor = _palette.Accent;
        _updateDlBtn.ApplyTheme(_palette);
        _updateNotesBtn.ApplyTheme(_palette);
        _updateDismissBtn.NormalBg = _palette.ElevatedBg;
        _updateDismissBtn.HoverBg = _palette.HoverBg;
        _updateDismissBtn.IconColor = _palette.TextMuted;

        _newTabPanel.ApplyTheme(_palette);
        _sidePanel.ApplyTheme(_palette);
        _devToolsDockPanel.BackColor = _palette.SurfaceBg;
        _devToolsSplitter.BackColor = _palette.Border;
        RenderTabs();
        SyncAddressBar();
        SyncBookmarkStar();
        UpdateAdShieldBadge();
    }

    // ── CHROME-LIKE DOCKED DEVTOOLS ─────────────────────────────────────────────
    private async Task ToggleDevToolsAsync()
    {
        if (_activeTab is null || !_activeTab.HasNavigated)
        {
            ShowToast("Geliştirici Araçları", "Geliştirici araçlarını açmak için önce bir web sayfası açın.");
            return;
        }

        if (_devToolsHwnd != IntPtr.Zero && DevToolsManager.IsOpen(_devToolsHwnd))
        {
            DevToolsManager.Close(_devToolsHwnd);
            CollapseDevToolsDock();
            return;
        }

        var existing = DevToolsManager.SnapshotVisibleDevToolsHwnds();
        _devToolsDockPanel.Visible = true;
        _devToolsSplitter.Visible = true;
        _contentHost.BringToFront();
        PerformLayout();

        _activeTab.OpenDevTools();

        IntPtr found = IntPtr.Zero;
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(80);
            found = DevToolsManager.FindDevToolsWindow(existing);
            if (found != IntPtr.Zero) break;
        }

        if (found != IntPtr.Zero)
        {
            _devToolsHwnd = found;
            _devToolsAutoDocked = true;
            var dockRect = _devToolsDockPanel.RectangleToScreen(_devToolsDockPanel.ClientRectangle);
            DevToolsManager.AttachAsDockedOwner(_devToolsHwnd, Handle, dockRect);
        }
        else
        {
            CollapseDevToolsDock();
        }
    }

    private void MonitorDevToolsWindow()
    {
        if (_devToolsHwnd != IntPtr.Zero)
        {
            if (!DevToolsManager.IsOpen(_devToolsHwnd))
                CollapseDevToolsDock();
            return;
        }

        // Catch DevTools opened via right-click "İncele"
        if (_activeTab is { HasNavigated: true } && Visible)
        {
            var found = DevToolsManager.FindDevToolsWindow();
            if (found != IntPtr.Zero)
            {
                _devToolsHwnd = found;
                _devToolsAutoDocked = true;
                _devToolsDockPanel.Visible = true;
                _devToolsSplitter.Visible = true;
                _contentHost.BringToFront();
                PerformLayout();
                var dockRect = _devToolsDockPanel.RectangleToScreen(_devToolsDockPanel.ClientRectangle);
                DevToolsManager.AttachAsDockedOwner(_devToolsHwnd, Handle, dockRect);
            }
        }
    }

    private void SyncDockedDevToolsBounds()
    {
        if (!_devToolsAutoDocked || !_devToolsDockPanel.Visible || _devToolsHwnd == IntPtr.Zero)
            return;
        if (!DevToolsManager.IsOpen(_devToolsHwnd))
        {
            CollapseDevToolsDock();
            return;
        }
        var dockRect = _devToolsDockPanel.RectangleToScreen(_devToolsDockPanel.ClientRectangle);
        DevToolsManager.UpdateDockBounds(_devToolsHwnd, dockRect);
    }

    private void CollapseDevToolsDock()
    {
        _devToolsHwnd = IntPtr.Zero;
        _devToolsAutoDocked = false;
        _devToolsDockPanel.Visible = false;
        _devToolsSplitter.Visible = false;
    }

    private async Task CheckUpdatesSilentlyAsync()
    {
        try
        {
            await Task.Delay(3500);
            if (IsDisposed) return;
            var res = await UpdateService.CheckForUpdatesAsync(_ctx.Settings.Version, _ctx.Storage.IsInstalledMode);
            if (IsDisposed || !res.UpdateAvailable) return;

            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _updateDownloadUrl = res.DirectDownloadUrl;
                _updateReleaseUrl = res.ReleaseUrl;
                _updateBannerText.Text = $"Yeni ED Browser sürümü yayınlandı: v{res.LatestVersion} (Mevcut: v{res.CurrentVersion})";
                _updateBanner.Visible = true;
                ShowToast("ED Browser Güncellemesi", $"Yeni sürüm hazır: v{res.LatestVersion}");
            });
        }
        catch { }
    }

    private void CleanupOnExit()
    {
        _memoryTimer.Stop();
        _devToolsSyncTimer.Stop();
        _devToolsSyncTimer.Dispose();
        if (_devToolsHwnd != IntPtr.Zero)
            DevToolsManager.Close(_devToolsHwnd);
        _bmHoverTimer?.Stop();
        _bmHoverTimer?.Dispose();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        foreach (var tab in _tabs.ToList())
            tab.Dispose();
        _tabs.Clear();

        if (!_incognito)
            _ctx.SaveAll();
        else if (_incognitoDir is not null)
        {
            try
            {
                if (Directory.Exists(_incognitoDir))
                    Directory.Delete(_incognitoDir, recursive: true);
            }
            catch { }
        }
    }

    private static Icon LoadEmbeddedIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("edbrowser.ico", StringComparison.OrdinalIgnoreCase));
            if (resName is not null)
            {
                using var s = asm.GetManifestResourceStream(resName);
                if (s is not null) return new Icon(s);
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public static void RenderUiPreview(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var ctx = new BrowserContext();
        using var main = new MainForm(ctx, incognito: false, initialUrl: null, skipInitialNavigate: true);
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-32000, -32000);
        main.Show();
        Application.DoEvents();
        using (var bmp = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(bmp, new Rectangle(0, 0, main.Width, main.Height));
            bmp.Save(Path.Combine(outDir, "main_dark.png"), ImageFormat.Png);
        }

        // Capture History Panel preview with sample entries if empty
        bool addedSampleHist = false;
        if (ctx.History.Count == 0)
        {
            addedSampleHist = true;
            ctx.History.Add(new HistoryEntry { Title = "Örnek Sayfa", Url = "https://example.com" });
            ctx.History.Add(new HistoryEntry { Title = "GitHub — edizdar/ED-Browser", Url = "https://github.com/edizdar/ED-Browser" });
        }
        main._sidePanel.ShowMode("hist");
        Application.DoEvents();
        using (var bmp = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(bmp, new Rectangle(0, 0, main.Width, main.Height));
            bmp.Save(Path.Combine(outDir, "history_panel.png"), ImageFormat.Png);
        }
        if (addedSampleHist) ctx.History.Clear();
        main._sidePanel.HidePanel();

        var palette = ThemeService.GetPalette(ctx.Settings.Theme, false);

        var sampleBms = new List<BookmarkItem>
        {
            new() { Type = "folder", Name = "İş" },
            new() { Type = "folder", Name = "Araçlar" }
        };
        using (var bmDlg = new BookmarkEditDialog(sampleBms, null, "Örnek Site", "https://example.com", palette))
        {
            bmDlg.StartPosition = FormStartPosition.Manual;
            bmDlg.Location = new Point(-32000, -32000);
            bmDlg.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(bmDlg.Width, bmDlg.Height);
            bmDlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmDlg.Width, bmDlg.Height));
            bmp.Save(Path.Combine(outDir, "bookmark_dialog.png"), ImageFormat.Png);
            bmDlg.Close();
        }

        using var dlg = new SettingsDialog(ctx, palette, () => { });
        dlg.StartPosition = FormStartPosition.Manual;
        dlg.Location = new Point(-32000, -32000);
        dlg.Show();
        string[] names = { "settings_general.png", "settings_privacy.png", "settings_performance.png", "settings_system.png" };
        for (int i = 0; i < names.Length; i++)
        {
            dlg.SelectSection(i);
            Application.DoEvents();
            using var bmp = new Bitmap(dlg.Width, dlg.Height);
            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
            bmp.Save(Path.Combine(outDir, names[i]), ImageFormat.Png);
        }
        dlg.Close();
        main.Close();
    }
}
