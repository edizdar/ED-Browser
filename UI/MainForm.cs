using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using EDBrowser.Models;
using EDBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EDBrowser.UI;

public sealed class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly BrowserContext _ctx;
    private readonly bool _incognito;
    private readonly string? _incognitoDir;
    private readonly List<BrowserTab> _tabs = new();
    private readonly Stack<string> _closedTabUrls = new();
    private BrowserTab? _activeTab;
    private ThemePalette _palette;

    // Layout chrome
    private readonly ModernTabBar _tabBar;
    private readonly VerticalTabBarControl _verticalTabBar;
    private readonly Panel _toolbarPanel;
    private readonly IconButton _btnBack;
    private readonly IconButton _btnFwd;
    private readonly IconButton _btnReload;
    private readonly IconButton _btnHome;

    private readonly Panel _omnibox;
    private readonly IconButton _secBtn;
    private readonly IconButton _vpnBtn;
    private readonly TextBox _urlBox;
    private readonly IconButton _readerBtn;
    private readonly IconButton _adShieldBtn;
    private readonly IconButton _bmStarBtn;
    private readonly OmniboxAutocompletePopup _autocompletePopup;
    private bool _omniboxFocused;
    private bool _suppressAutocomplete;

    private readonly Panel _zoomPill;
    private readonly IconButton _zoomOutBtn;
    private readonly IconButton _zoomResetBtn;
    private readonly IconButton _zoomInBtn;
    private readonly IconButton _ramBtn;
    private readonly IconButton _mediaBtn;
    private readonly IconButton _splitBtn;
    private readonly IconButton _shotBtn;
    private readonly IconButton _fsBtn;
    private readonly FlowLayoutPanel _extIconsHost;
    private readonly IconButton _extBtn;
    private readonly IconButton _pwBtn;
    private readonly IconButton _dlBtn;
    private readonly IconButton _bmBtn;
    private readonly IconButton _histBtn;
    private readonly IconButton _incBtn;
    private readonly IconButton _profileBtn;
    private readonly IconButton _cfgBtn;
    private readonly IconButton _moreBtn;

    private readonly Panel _splitPane;
    private readonly Splitter _splitSplitter;
    private readonly Panel _splitHeader;
    private readonly TextBox _splitUrlBox;
    private readonly Panel _splitWebHost;
    private BrowserTab? _splitTab;

    private readonly Panel _leftSidebar;
    private readonly List<IconButton> _sidebarButtons = new();
    private readonly BookmarkBarControl _bookmarkBar;
    private readonly Panel _contentHost;
    private readonly DevToolsPanel _devToolsPanel;
    private readonly Splitter _devToolsSplitter;
    private readonly NewTabPanel _newTabPanel;
    private readonly SidePanelHost _sidePanel;

    private readonly Panel _extInstallBanner;
    private readonly Label _extBannerText;
    private readonly ModernButton _extInstallBtn;
    private readonly IconButton _extDismissBtn;
    private string? _detectedExtUrl;
    private string? _dismissedExtId;

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
    private Rectangle _activeBookmarkAnchorRect;
    private AppMenuPopup? _activeAppMenu;
    private VpnPrivacyPopup? _activeVpnPopup;

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

        // 1. Modern Tab Bar + Collapsible Vertical Tab Bar
        _tabBar = new ModernTabBar
        {
            IsIncognito = _incognito,
            Height = 40,
            Visible = !_ctx.Settings.UseVerticalTabs
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

        _verticalTabBar = new VerticalTabBarControl
        {
            Visible = _ctx.Settings.UseVerticalTabs
        };
        _verticalTabBar.TabSelected += id =>
        {
            var t = _tabs.FirstOrDefault(x => x.Id == id);
            if (t is not null) _ = SwitchToTabAsync(t);
        };
        _verticalTabBar.TabClosed += id =>
        {
            var t = _tabs.FirstOrDefault(x => x.Id == id);
            if (t is not null) _ = CloseTabAsync(t);
        };
        _verticalTabBar.TabRightClicked += (id, pt) =>
        {
            var t = _tabs.FirstOrDefault(x => x.Id == id);
            if (t is not null) ShowTabContextMenu(t, pt);
        };
        _verticalTabBar.NewTabClicked += () => _ = AddTabAsync();
        _verticalTabBar.HorizontalTabsRequested += ToggleVerticalTabs;

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
        _secBtn.SetTooltip("Site Güvenliği, Reklam & HTTPS İstisnaları");
        _secBtn.Click += (_, _) => ShowSiteSecurityMenu();

        _vpnBtn = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = "VPN",
            Dock = DockStyle.Left,
            Size = new Size(74, 30),
            CornerRadius = 12,
            Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
            Margin = new Padding(2, 0, 2, 0)
        };
        _vpnBtn.SetTooltip("VPN, Anlık Sunucu Seçici, DoH ve Gizlilik Kalkanı");
        _vpnBtn.Click += (_, _) => ShowVpnPrivacyPopup();

        _urlBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.75f),
            PlaceholderText = "Web'de ara veya adres yaz…",
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        _autocompletePopup = new OmniboxAutocompletePopup(_palette);
        _autocompletePopup.SuggestionChosen += chosenUrl =>
        {
            _autocompletePopup.Hide();
            _ = NavigateActiveAsync(chosenUrl);
            _contentHost.Focus();
        };

        _urlBox.GotFocus += (_, _) => { _omniboxFocused = true; _omnibox.Invalidate(); _urlBox.SelectAll(); };
        _urlBox.LostFocus += (_, _) =>
        {
            _omniboxFocused = false;
            _omnibox.Invalidate();
            BeginInvoke(() =>
            {
                if (!IsDisposed && !_urlBox.Focused && !_autocompletePopup.Bounds.Contains(Cursor.Position))
                    _autocompletePopup.Hide();
            });
        };
        _urlBox.TextChanged += (_, _) =>
        {
            if (_suppressAutocomplete || !_urlBox.Focused) return;
            var txt = _urlBox.Text;
            if (string.IsNullOrWhiteSpace(txt))
            {
                _autocompletePopup.Hide();
                return;
            }
            var screenPt = _omnibox.PointToScreen(new Point(0, _omnibox.Height + 3));
            _autocompletePopup.UpdateSuggestions(txt, _ctx, screenPt, _omnibox.Width);
        };
        _urlBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _autocompletePopup.Visible)
            {
                e.SuppressKeyPress = true;
                _autocompletePopup.MoveSelection(+1);
                return;
            }
            if (e.KeyCode == Keys.Up && _autocompletePopup.Visible)
            {
                e.SuppressKeyPress = true;
                _autocompletePopup.MoveSelection(-1);
                return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var target = _autocompletePopup.Visible && !string.IsNullOrEmpty(_autocompletePopup.SelectedUrl)
                    ? _autocompletePopup.SelectedUrl
                    : _urlBox.Text;
                _autocompletePopup.Hide();
                _ = NavigateActiveAsync(target);
                _contentHost.Focus();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _autocompletePopup.Hide();
                SyncAddressBar();
                _contentHost.Focus();
            }
        };
        BuildAddressBarContextMenu();

        _readerBtn = new IconButton
        {
            IconType = VectorIcon.Book,
            Dock = DockStyle.Right,
            Size = new Size(28, 30),
            IconSizePx = 14,
            CornerRadius = 14
        };
        _readerBtn.SetTooltip("Okuyucu Modu (Ctrl+Alt+R)");
        _readerBtn.Click += async (_, _) =>
        {
            if (_activeTab is not null)
                await _activeTab.ToggleReaderModeAsync();
        };

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
        _omnibox.Controls.Add(_vpnBtn);
        _omnibox.Controls.Add(_secBtn);
        _omnibox.Controls.Add(_readerBtn);
        _omnibox.Controls.Add(_adShieldBtn);
        _omnibox.Controls.Add(_bmStarBtn);

        _omnibox.Resize += (_, _) => LayoutOmniboxText();
        _urlBox.FontChanged += (_, _) => LayoutOmniboxText();

        // Right Toolbar: Zoom Pill (- 100% +) + RAM + Screenshot + Fullscreen + Utility Buttons + App Menu
        var rightTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 1, 0, 0)
        };

        _zoomPill = new Panel
        {
            Size = new Size(116, 30),
            Margin = new Padding(0, 1, 6, 0),
            Padding = new Padding(2)
        };
        _zoomPill.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1f, 1f, _zoomPill.Width - 2f, _zoomPill.Height - 2f);
            using var gp = GraphicsExt.CreateRoundedRect(r, r.Height / 2f);
            using (var bg = new SolidBrush(_palette.ElevatedBg))
                e.Graphics.FillPath(bg, gp);
            using var pen = new Pen(_palette.Border, 1f);
            e.Graphics.DrawPath(pen, gp);
        };

        _zoomOutBtn = new IconButton
        {
            IconType = VectorIcon.Minus,
            Location = new Point(4, 3),
            Size = new Size(24, 24),
            IconSizePx = 11,
            CornerRadius = 12
        };
        _zoomOutBtn.SetTooltip("Uzaklaştır (Ctrl+-)");
        _zoomOutBtn.Click += (_, _) => ChangeZoom(-0.10);

        _zoomResetBtn = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = "%100",
            Location = new Point(30, 3),
            Size = new Size(56, 24),
            CornerRadius = 10,
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
        };
        _zoomResetBtn.SetTooltip("Yakınlaştırmayı Sıfırla (%100)");
        _zoomResetBtn.Click += (_, _) => ResetZoom();

        _zoomInBtn = new IconButton
        {
            IconType = VectorIcon.Plus,
            Location = new Point(88, 3),
            Size = new Size(24, 24),
            IconSizePx = 11,
            CornerRadius = 12
        };
        _zoomInBtn.SetTooltip("Yakınlaştır (Ctrl++)");
        _zoomInBtn.Click += (_, _) => ChangeZoom(0.10);

        _zoomPill.Controls.AddRange(new Control[] { _zoomOutBtn, _zoomResetBtn, _zoomInBtn });

        _ramBtn = new IconButton
        {
            IconType = VectorIcon.Bolt,
            LabelText = "-- MB",
            AutoWidthFromText = true,
            PillBorder = true,
            Height = 30,
            IconSizePx = 13,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 1, 4, 0)
        };
        _ramBtn.Click += async (_, _) => await OptimizeRamNowAsync();

        _mediaBtn = MakeToolBtn(VectorIcon.MediaPlay, "Medya Merkezi & Resim İçinde Resim (PiP)", (_, _) => ShowMediaHubPopup());
        _splitBtn = MakeToolBtn(VectorIcon.SplitView, "Bölünmüş Ekran — Yan Yana İki Sayfa (Ctrl+Shift+X)", (_, _) => _ = ToggleSplitViewAsync());
        _shotBtn = MakeToolBtn(VectorIcon.Camera, "Ekran Görüntüsü Al (Ctrl+Shift+S)", (_, _) => ShowScreenshotMenu());
        _fsBtn = MakeToolBtn(VectorIcon.Fullscreen, "Tam Ekran (F11)", (_, _) => ToggleFullscreen());

        _extIconsHost = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 1, 0, 0),
            BackColor = Color.Transparent,
            Visible = false
        };

        _extBtn = MakeToolBtn(VectorIcon.Puzzle, "Eklentiler ve Uzantı Mağazası (Ctrl+Shift+E)", (_, _) => OpenExtensionsDialog());
        _dlBtn = MakeToolBtn(VectorIcon.Download, "İndirilenler (Ctrl+Shift+J)", (_, _) => _sidePanel.Toggle("dl"));
        _bmBtn = MakeToolBtn(VectorIcon.Bookmark, "Yer İmleri (Ctrl+Shift+O)", (_, _) => _sidePanel.Toggle("bm"));
        _histBtn = MakeToolBtn(VectorIcon.History, "Geçmiş (Ctrl+H)", (_, _) => _sidePanel.Toggle("hist"));
        _pwBtn = MakeToolBtn(VectorIcon.Key, "Şifre Yöneticisi", (_, _) => OpenPasswordManager());
        _incBtn = MakeToolBtn(VectorIcon.Incognito, "Gizli Pencere (Ctrl+Shift+P)", (_, _) => OpenIncognitoWindow());

        var curProf = _ctx.Profiles.GetCurrentProfile();
        _profileBtn = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = curProf.Name.Length > 0 ? curProf.Name[..1].ToUpperInvariant() : "V",
            Size = new Size(30, 30),
            CornerRadius = 15,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(2, 1, 2, 0)
        };
        _profileBtn.SetTooltip($"Profil: {curProf.Name} (Değiştir / Yeni Profil)");
        _profileBtn.Click += (_, _) => ShowProfileMenu();

        _cfgBtn = MakeToolBtn(VectorIcon.Settings, "Ayarlar (Ctrl+,)", (_, _) => OpenSettings());
        _moreBtn = MakeToolBtn(VectorIcon.MoreDots, "Menü ve Araçlar", (_, _) => ShowMoreMenu());

        rightTools.Controls.AddRange(new Control[]
        {
            _zoomPill, _ramBtn, _mediaBtn, _splitBtn, _shotBtn, _fsBtn, _extIconsHost, _extBtn, _dlBtn, _bmBtn, _histBtn, _pwBtn, _incBtn, _profileBtn, _cfgBtn, _moreBtn
        });

        _toolbarPanel.Controls.Add(_omnibox);
        _toolbarPanel.Controls.Add(leftNav);
        _toolbarPanel.Controls.Add(rightTools);

        // 3. Bookmarks Bar (Direct mouse-capture drag-and-drop)
        _bookmarkBar = new BookmarkBarControl
        {
            Visible = _ctx.Settings.ShowBookmarkBar,
            FaviconResolver = url => _ctx.Favicons.GetCached(url)
        };
        _ctx.Favicons.FaviconUpdated += _ =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    RenderTabs();
                    _bookmarkBar.Invalidate();
                    _newTabPanel.Invalidate(true);
                });
            }
            catch { }
        };
        _bookmarkBar.BookmarkClicked += item => _ = NavigateActiveAsync(item.Url);
        _bookmarkBar.BookmarkMiddleClicked += item => _ = AddTabAsync(item.Url);
        _bookmarkBar.FolderClicked += (folder, rect) => OpenBookmarkFolderDropdown(rect, folder);
        _bookmarkBar.FolderHovered += (folder, rect) =>
        {
            if (_activeBookmarkMenu is { Visible: true } && _activeBookmarkAnchorRect != rect)
                OpenBookmarkFolderDropdown(rect, folder);
        };
        _bookmarkBar.DragStarted += CloseBookmarkFolderDropdown;
        _bookmarkBar.BookmarksReordered += () =>
        {
            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
            _sidePanel.RefreshIfOpen("bm");
        };
        _bookmarkBar.ItemRightClicked += ShowBookmarkBarItemContextMenu;
        _bookmarkBar.EmptyAreaRightClicked += pt =>
        {
            var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
            menu.Items.Add("Bu Sayfayı Yer İmine Ekle", null, (_, _) => ToggleCurrentBookmark());
            menu.Items.Add("Yeni Klasör Ekle", null, (_, _) =>
            {
                var n = PromptDialog.Show(this, "Yeni Klasör", "Klasör adı:", "", _palette);
                if (!string.IsNullOrWhiteSpace(n))
                {
                    _ctx.Bookmarks.Add(new BookmarkItem { Type = "folder", Name = n.Trim() });
                    _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                    RenderBookmarksBar();
                    _sidePanel.RefreshIfOpen("bm");
                }
            });
            menu.Items.Add("Yer İmleri Panelini Aç", null, (_, _) => _sidePanel.ShowMode("bm"));
            menu.Show(pt);
        };
        _bookmarkBar.OverflowClicked += (hidden, rect) => OpenOverflowBookmarksDropdown(rect, hidden);

        // 4. Content Host + Docked DevTools + SidePanel + FindBar + Password Banner
        _contentHost = new Panel { Dock = DockStyle.Fill };
        _newTabPanel = new NewTabPanel
        {
            FaviconResolver = url => _ctx.Favicons.GetCached(url)
        };
        _newTabPanel.BindShortcuts(_ctx.Shortcuts);
        _newTabPanel.NavigateRequested += q => _ = NavigateActiveAsync(q);
        _newTabPanel.OpenNewTabRequested += u => _ = AddTabAsync(u);
        _newTabPanel.ShortcutsChanged += () => _ctx.Storage.SaveShortcuts(_ctx.Shortcuts);
        _contentHost.Controls.Add(_newTabPanel);

        // Right Split-View Pane (Edge/Vivaldi-style side-by-side browsing)
        _splitPane = new Panel
        {
            Dock = DockStyle.Right,
            Width = 540,
            Visible = false
        };
        _splitSplitter = new Splitter
        {
            Dock = DockStyle.Right,
            Width = 5,
            MinExtra = 260,
            MinSize = 260,
            Visible = false
        };
        _splitHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(8, 4, 8, 4)
        };
        var spClose = new IconButton { IconType = VectorIcon.Close, Dock = DockStyle.Right, Size = new Size(26, 26), IconSizePx = 11 };
        spClose.SetTooltip("Bölünmüş Ekranı Kapat (Ctrl+Shift+X)");
        spClose.Click += (_, _) => _ = ToggleSplitViewAsync();
        var spSwap = new IconButton { IconType = VectorIcon.SplitView, Dock = DockStyle.Right, Size = new Size(26, 26), IconSizePx = 13 };
        spSwap.SetTooltip("Sol ve Sağ Sayfayı Değiştir");
        spSwap.Click += async (_, _) => await SwapSplitViewsAsync();
        var spGo = new IconButton { IconType = VectorIcon.ArrowRight, Dock = DockStyle.Right, Size = new Size(26, 26), IconSizePx = 12 };
        spGo.Click += (_, _) => _ = NavigateSplitAsync(_splitUrlBox.Text);
        var spReload = new IconButton { IconType = VectorIcon.Reload, Dock = DockStyle.Left, Size = new Size(26, 26), IconSizePx = 12 };
        spReload.Click += (_, _) => _splitTab?.ReloadOrStop();
        _splitUrlBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 8.75f),
            PlaceholderText = "İkinci pencere için adres veya arama…"
        };
        _splitUrlBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = NavigateSplitAsync(_splitUrlBox.Text);
            }
        };
        _splitHeader.Controls.AddRange(new Control[] { _splitUrlBox, spGo, spSwap, spClose, spReload });
        _splitWebHost = new Panel { Dock = DockStyle.Fill };
        _splitPane.Controls.Add(_splitWebHost);
        _splitPane.Controls.Add(_splitHeader);

        _devToolsPanel = new DevToolsPanel
        {
            Dock = DockStyle.Right,
            Width = 540,
            Visible = false
        };
        _devToolsPanel.DockModeChanged += mode => ApplyDevToolsDockMode(mode);
        _devToolsPanel.OpenExternalDevToolsRequested += () => _activeTab?.OpenDevTools();
        _devToolsPanel.CloseRequested += CloseDevToolsPanel;

        _devToolsSplitter = new Splitter
        {
            Dock = DockStyle.Right,
            Width = 5,
            MinExtra = 300,
            MinSize = 240,
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
            Text = "Otomatik Güncelle",
            Primary = true,
            Dock = DockStyle.Right,
            Width = 148,
            Height = 30
        };
        _updateDlBtn.Click += async (_, _) =>
        {
            var target = _updateDownloadUrl ?? _updateReleaseUrl;
            if (string.IsNullOrEmpty(target)) return;
            _updateDlBtn.Enabled = false;
            var prog = new Progress<int>(pct => _updateDlBtn.Text = $"İndiriliyor %{pct}");
            var res = await UpdateService.DownloadAndApplyUpdateAsync(target, _ctx.Storage.IsInstalledMode, prog);
            if (!res.Ok)
            {
                _updateDlBtn.Enabled = true;
                _updateDlBtn.Text = "Otomatik Güncelle";
                _ = AddTabAsync(target);
                _updateBanner.Visible = false;
            }
            else
            {
                Close();
            }
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

        // Top 1-Click Extension Store Install Banner
        _extInstallBanner = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top,
            Visible = false,
            Padding = new Padding(14, 5, 14, 5)
        };
        _extBannerText = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            UseMnemonic = false
        };
        _extInstallBtn = new ModernButton
        {
            Text = "ED Browser'a Ekle",
            Primary = true,
            Dock = DockStyle.Right,
            Width = 148,
            Height = 28
        };
        _extInstallBtn.FitWidthToText(136, 24);
        _extInstallBtn.Click += async (_, _) => await InstallDetectedStoreExtensionAsync();
        _extDismissBtn = new IconButton
        {
            IconType = VectorIcon.Close,
            Dock = DockStyle.Right,
            Size = new Size(28, 28),
            IconSizePx = 12,
            Margin = new Padding(6, 0, 0, 0)
        };
        _extDismissBtn.Click += (_, _) =>
        {
            if (ExtensionService.TryExtractStoreExtension(_detectedExtUrl, out _, out var extId))
                _dismissedExtId = extId;
            _extInstallBanner.Visible = false;
        };
        _extInstallBanner.Controls.Add(_extBannerText);
        _extInstallBanner.Controls.Add(_extInstallBtn);
        _extInstallBanner.Controls.Add(_extDismissBtn);

        // Optional Opera/Yandex-Style Left Quick-Access Sidebar
        _leftSidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 42,
            Visible = _ctx.Settings.ShowLeftSidebar,
            Padding = new Padding(5, 8, 5, 8)
        };
        _leftSidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Border, 1f);
            e.Graphics.DrawLine(pen, _leftSidebar.Width - 1, 0, _leftSidebar.Width - 1, _leftSidebar.Height);
        };
        var sideStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        IconButton MakeSideBtn(VectorIcon ic, string tip, EventHandler onClick)
        {
            var sb = new IconButton { IconType = ic, Size = new Size(32, 32), IconSizePx = 15, CornerRadius = 8, Margin = new Padding(0, 0, 0, 6) };
            sb.SetTooltip(tip);
            sb.Click += onClick;
            _sidebarButtons.Add(sb);
            return sb;
        }
        sideStack.Controls.AddRange(new Control[]
        {
            MakeSideBtn(VectorIcon.Bookmark, "Yer İmleri (Ctrl+Shift+O)", (_, _) => _sidePanel.Toggle("bm")),
            MakeSideBtn(VectorIcon.History, "Geçmiş (Ctrl+H)", (_, _) => _sidePanel.Toggle("hist")),
            MakeSideBtn(VectorIcon.Download, "İndirilenler (Ctrl+Shift+J)", (_, _) => _sidePanel.Toggle("dl")),
            MakeSideBtn(VectorIcon.Puzzle, "Eklentiler ve Mağaza (Ctrl+Shift+E)", (_, _) => OpenExtensionsDialog()),
            MakeSideBtn(VectorIcon.Key, "Şifre Yöneticisi", (_, _) => OpenPasswordManager()),
            MakeSideBtn(VectorIcon.Camera, "Ekran Görüntüsü Al (Ctrl+Shift+S)", (_, _) => ShowScreenshotMenu()),
            MakeSideBtn(VectorIcon.Terminal, "Geliştirici Araçları (F12)", (_, _) => _ = ToggleDevToolsAsync()),
            MakeSideBtn(VectorIcon.Settings, "Ayarlar (Ctrl+,)", (_, _) => OpenSettings())
        });
        _leftSidebar.Controls.Add(sideStack);

        Controls.Add(_contentHost);
        Controls.Add(_splitSplitter);
        Controls.Add(_splitPane);
        Controls.Add(_verticalTabBar);
        Controls.Add(_leftSidebar);
        Controls.Add(_devToolsSplitter);
        Controls.Add(_devToolsPanel);
        Controls.Add(_sidePanel);
        Controls.Add(_pwBanner);
        Controls.Add(_extInstallBanner);
        Controls.Add(_updateBanner);
        Controls.Add(_bookmarkBar);
        Controls.Add(_toolbarPanel);
        Controls.Add(_tabBar);

        _contentHost.Controls.Add(_findBar);
        _contentHost.Resize += (_, _) =>
        {
            _findBar.Location = new Point(
                Math.Max(8, _contentHost.ClientSize.Width - _findBar.Width - 14),
                Math.Max(8, _contentHost.ClientSize.Height - _findBar.Height - 10));
        };

        ApplyTheme();
        RenderBookmarksBar();
        RefreshPinnedExtensionsStrip();
        LayoutOmniboxText();

        _memoryTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _memoryTimer.Tick += async (_, _) => await OnMemoryTimerTickAsync();
        _memoryTimer.Start();

        _ctx.VpnRouter.FailoverServerPromoted += OnVpnFailoverPromoted;

        Shown += async (_, _) =>
        {
            if (skipInitialNavigate)
            {
                await AddTabAsync(null);
            }
            else if (!_incognito && string.IsNullOrWhiteSpace(initialUrl) && _ctx.Settings.RestoreSessionOnStartup)
            {
                bool restored = await RestorePreviousSessionAsync();
                if (!restored)
                    await AddTabAsync(_ctx.Settings.HomePage);
                if (_ctx.Settings.VpnEnabled && _ctx.Settings.ProxyMode == ProxyMode.FreeCommunity)
                    _ = EnsureHealthyFreeServerOnStartupAsync();
            }
            else
            {
                var startUrl = !string.IsNullOrWhiteSpace(initialUrl)
                    ? initialUrl
                    : _ctx.Settings.HomePage;
                await AddTabAsync(startUrl);
                if (_ctx.Settings.VpnEnabled && _ctx.Settings.ProxyMode == ProxyMode.FreeCommunity)
                    _ = EnsureHealthyFreeServerOnStartupAsync();
            }
            RefreshMemoryBadge();
            if (!_incognito && !skipInitialNavigate && _ctx.Settings.CheckUpdatesOnStartup)
                _ = CheckUpdatesSilentlyAsync();
        };

        FormClosing += (_, _) => CleanupOnExit();
    }

    private void LayoutOmniboxText()
    {
        int left = _vpnBtn.Right + 6;
        int right = _readerBtn.Left - 4;
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
        if (_activeAppMenu is { IsDisposed: false, Visible: true })
        {
            _activeAppMenu.SafeClose();
            _activeAppMenu = null;
            return;
        }

        var btnScreen = _moreBtn.RectangleToScreen(_moreBtn.ClientRectangle);
        var anchor = new Point(btnScreen.Right, btnScreen.Bottom + 6);
        var snap = MemoryManagerService.CaptureProcessTreeSnapshot();
        var state = BuildAppMenuState(snap.TotalWorkingSetBytes / (1024 * 1024));

        var popup = new AppMenuPopup(this, state, _palette, anchor);
        popup.FormClosed += (_, _) =>
        {
            if (_activeAppMenu == popup)
                _activeAppMenu = null;
        };
        _activeAppMenu = popup;
        popup.Show(this);
    }

    private AppMenuState BuildAppMenuState(long totalRamMb) => new()
    {
        Version = _ctx.Settings.Version,
        TotalRamMb = totalRamMb,
        IsDarkTheme = _incognito || _ctx.Settings.Theme == BrowserTheme.Dark,
        AdBlockEnabled = _ctx.Settings.AdBlockEnabled,
        ShowBookmarkBar = _ctx.Settings.ShowBookmarkBar,
        ShowLeftSidebar = _ctx.Settings.ShowLeftSidebar,
        UseVerticalTabs = _ctx.Settings.UseVerticalTabs,
        IsSplitViewActive = _splitPane.Visible,
        IsActiveTabPinned = _activeTab?.IsPinned ?? false,
        IsActiveTabMuted = _activeTab?.IsMuted ?? false,
        CanReopenClosedTab = _closedTabUrls.Count > 0,
        HasNavigatedTab = _activeTab?.HasNavigated ?? false,
        ZoomPercent = (int)Math.Round((_activeTab?.ZoomFactor ?? 1.0) * 100),
        IsFullscreen = _isFullscreen,
        NewTab = () => _ = AddTabAsync(),
        NewWindow = OpenNewWindow,
        NewIncognito = OpenIncognitoWindow,
        ReopenClosedTab = () => _ = ReopenClosedTabAsync(),
        TogglePinTab = TogglePinActiveTab,
        ToggleMuteTab = () => _activeTab?.ToggleMute(),
        DuplicateTab = () => _ = AddTabAsync(_activeTab?.Url),
        OpenBookmarks = () => _sidePanel.Toggle("bm"),
        ToggleBookmarkBar = ToggleBookmarkBarVisibility,
        ToggleLeftSidebar = ToggleLeftSidebarVisibility,
        ToggleVerticalTabs = ToggleVerticalTabs,
        ToggleSplitView = () => _ = ToggleSplitViewAsync(),
        ToggleReaderMode = () => { if (_activeTab is not null) _ = _activeTab.ToggleReaderModeAsync(); },
        OpenTaskManager = OpenTaskManager,
        OpenProfiles = ShowProfileMenu,
        OpenHistory = () => _sidePanel.Toggle("hist"),
        OpenDownloads = () => _sidePanel.Toggle("dl"),
        OpenExtensions = OpenExtensionsDialog,
        OpenPasswords = OpenPasswordManager,
        ZoomOut = () => ChangeZoom(-0.10),
        ZoomReset = ResetZoom,
        ZoomIn = () => ChangeZoom(0.10),
        ToggleFullscreen = ToggleFullscreen,
        FindInPage = ToggleFindBar,
        SavePageAs = () => _ = SaveActivePageAsAsync(),
        PrintPage = () => _activeTab?.PrintPage(),
        PrintToPdf = () => _ = SaveActivePageAsPdfAsync(),
        ViewPageSource = () => _ = ViewPageSourceAsync(),
        TakeScreenshot = ShowScreenshotMenu,
        ClearBrowsingData = OpenClearBrowsingDataDialog,
        OpenVpnPopup = ShowVpnPrivacyPopup,
        OptimizeRam = () => _ = OptimizeRamNowAsync(),
        ImportData = OpenImportDialog,
        ToggleDevTools = () => _ = ToggleDevToolsAsync(),
        ToggleTheme = ToggleThemeQuick,
        ToggleAdBlock = ToggleAdBlockQuick,
        OpenSettings = OpenSettings,
        CheckUpdates = () => _ = CheckUpdatesInteractiveAsync(),
        ExitApp = Close
    };

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
        tab.ZoomChanged += (t, _) => BeginInvoke(() => { if (t == _activeTab) SyncZoomBadge(); });
        tab.InspectRequested += (x, y) => BeginInvoke(() => { _ = InspectElementInDevToolsAsync(x, y); });
        tab.ViewSourceRequested += () => BeginInvoke(() => { _ = ViewPageSourceAsync(); });
        tab.DevToolsElementPicked += json => BeginInvoke(() => { _ = HandleDevToolsElementPickedAsync(json); });

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
        SyncZoomBadge();
        UpdateAdShieldBadge();
        UpdateExtensionStoreBanner();
        if (_devToolsPanel.Visible)
            await _devToolsPanel.AttachToTabAsync(tab);
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        int idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        if (!_incognito && tab.HasNavigated && !string.IsNullOrWhiteSpace(tab.Url))
            _closedTabUrls.Push(tab.Url);

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
            UpdateExtensionStoreBanner();
        }
    }

    private void RenderTabs()
    {
        var items = _tabs.Select(t => new ModernTabItem
        {
            Id = t.Id,
            Title = string.IsNullOrWhiteSpace(t.Title) ? "Yeni Sekme" : t.Title,
            Active = t == _activeTab,
            Loading = t.IsLoading,
            Sleeping = t.IsSleeping,
            DeepUnloaded = t.IsDeepUnloaded,
            Pinned = t.IsPinned,
            PlayingAudio = t.IsPlayingAudio,
            Muted = t.IsMuted,
            GroupName = t.GroupName,
            GroupColor = t.GroupColor,
            Favicon = string.IsNullOrEmpty(t.Url) ? null : _ctx.Favicons.GetCached(t.Url)
        }).ToList();

        _tabBar.SetTabs(items);
        _verticalTabBar.SetTabs(items);
        UpdateMediaBadge();
    }

    private void ShowTabContextMenu(BrowserTab tab, Point screenPos)
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        menu.Items.Add("Yeni Sekme", null, (_, _) => _ = AddTabAsync());
        menu.Items.Add(tab.IsPinned ? "Sabitlemeyi Kaldır" : "Sekmeyi Sabitle", null, (_, _) =>
        {
            tab.IsPinned = !tab.IsPinned;
            ReorderPinnedTabs();
        });

        var grpSub = new ToolStripMenuItem("Sekme Grubu");
        var presetGroups = new (string Name, Color Color)[]
        {
            ("İş", Color.FromArgb(56, 189, 248)),
            ("Araştırma", Color.FromArgb(168, 85, 247)),
            ("Alışveriş", Color.FromArgb(16, 185, 129)),
            ("Eğlence", Color.FromArgb(245, 158, 11)),
            ("Önemli", Color.FromArgb(239, 68, 68))
        };
        foreach (var pg in presetGroups)
        {
            var gItem = pg;
            grpSub.DropDownItems.Add(gItem.Name, null, (_, _) =>
            {
                tab.GroupName = gItem.Name;
                tab.GroupColor = gItem.Color;
                RenderTabs();
            });
        }
        grpSub.DropDownItems.Add("Özel Grup Adı…", null, (_, _) =>
        {
            var name = PromptDialog.Show(this, "Sekme Grubu", "Grup adı:", tab.GroupName, _palette);
            if (!string.IsNullOrWhiteSpace(name))
            {
                tab.GroupName = name.Trim();
                if (tab.GroupColor.IsEmpty) tab.GroupColor = _palette.Accent;
                RenderTabs();
            }
        });
        if (!string.IsNullOrEmpty(tab.GroupName))
        {
            grpSub.DropDownItems.Add(new ToolStripSeparator());
            grpSub.DropDownItems.Add("Gruptan Çıkar", null, (_, _) =>
            {
                tab.GroupName = "";
                tab.GroupColor = Color.Empty;
                RenderTabs();
            });
        }
        menu.Items.Add(grpSub);

        if (tab.HasNavigated)
        {
            menu.Items.Add(tab.IsMuted ? "Sekme Sesini Aç" : "Sekme Sesini Kapat", null, (_, _) => tab.ToggleMute());
        }
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
            foreach (var other in _tabs.Where(t => t != tab && !t.IsPinned).ToList())
                await CloseTabAsync(other);
        });
        if (!tab.IsPinned)
            menu.Items.Add("Sekmeyi Kapat", null, (_, _) => _ = CloseTabAsync(tab));
        menu.Show(screenPos);
    }

    private void ReorderPinnedTabs()
    {
        var pinned = _tabs.Where(t => t.IsPinned).ToList();
        var normal = _tabs.Where(t => !t.IsPinned).ToList();
        _tabs.Clear();
        _tabs.AddRange(pinned);
        _tabs.AddRange(normal);
        RenderTabs();
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
        {
            _suppressAutocomplete = true;
            _urlBox.Text = _activeTab.Url;
            _suppressAutocomplete = false;
        }

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
        var curHost = TryGetHost(_activeTab?.Url);
        bool sitePaused = !string.IsNullOrEmpty(curHost) &&
                          _ctx.Settings.AdBlockWhitelistHosts.Contains(curHost, StringComparer.OrdinalIgnoreCase);
        bool enabled = _ctx.Settings.AdBlockEnabled && !sitePaused;
        _adShieldBtn.IconColor = sitePaused
            ? Color.FromArgb(245, 158, 11)
            : (enabled ? _palette.Green : _palette.TextMuted);
        _adShieldBtn.SetTooltip(sitePaused
            ? $"Reklam Engelleyici bu sitede duraklatıldı ({curHost})"
            : enabled
                ? $"Reklam Engelleyici Aktif ({_ctx.AdBlocker.TotalRuleCount:N0} kural • Bu sekme: {_activeTab?.BlockedOnTab ?? 0}) — Tıkla kapat"
                : "Reklam Engelleyici Kapalı — Tıkla aç");
        UpdateVpnBadge();
    }

    private void UpdateVpnBadge()
    {
        var s = _ctx.Settings;
        bool tunnelActive = s.VpnEnabled;
        if (!tunnelActive)
        {
            _vpnBtn.LabelText = "VPN";
            _vpnBtn.NormalBg = _palette.ElevatedBg;
            _vpnBtn.HoverBg = _palette.HoverBg;
            _vpnBtn.IconColor = _palette.TextMuted;
            _vpnBtn.BorderColor = _palette.Border;
            _vpnBtn.SetTooltip($"VPN Kapalı • DoH: {s.DohProvider}\nTek tıkla açmak ve sunucu seçmek için tıkla");
            return;
        }

        _vpnBtn.LabelText = s.ProxyMode switch
        {
            ProxyMode.FreeCommunity when !string.IsNullOrWhiteSpace(s.ActiveFreeServerCountry)
                => $"VPN • {s.ActiveFreeServerCountry}",
            ProxyMode.FreeCommunity => "VPN ✓",
            ProxyMode.CloudflareWarp => "WARP ✓",
            ProxyMode.Tor => "TOR ✓",
            ProxyMode.Custom => "VPN ✓",
            _ => "VPN ✓"
        };
        _vpnBtn.NormalBg = Color.FromArgb(38, _palette.Green);
        _vpnBtn.HoverBg = _palette.HoverBg;
        _vpnBtn.IconColor = _palette.Green;
        _vpnBtn.BorderColor = _palette.Green;
        _vpnBtn.SetTooltip($"Aktif VPN: {_ctx.VpnRouter.CurrentRouteSummary} (Router :{_ctx.VpnRouter.ListenPort})\nDoH: {s.DohProvider} • Tıkla: Yönet");
    }

    private void ShowVpnPrivacyPopup()
    {
        if (_activeVpnPopup is { IsDisposed: false, Visible: true })
        {
            _activeVpnPopup.SafeClose();
            _activeVpnPopup = null;
            return;
        }

        var btnRect = _vpnBtn.RectangleToScreen(_vpnBtn.ClientRectangle);
        var anchor = new Point(btnRect.Left, btnRect.Bottom + 6);
        var popup = new VpnPrivacyPopup(
            this,
            _ctx,
            _palette,
            anchor,
            () => UpdateAdShieldBadge(),
            RestartBrowserForNetworkChange,
            url => _ = AddTabAsync(url));
        popup.FormClosed += (_, _) =>
        {
            if (_activeVpnPopup == popup)
                _activeVpnPopup = null;
            UpdateVpnBadge();
        };
        _activeVpnPopup = popup;
        popup.Show(this);
    }

    private void RestartBrowserForNetworkChange()
    {
        try
        {
            _ctx.SaveAll();
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            Close();
        }
        catch { }
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
        if (_bookmarkBar.Visible)
            _bookmarkBar.SetBookmarks(_ctx.Bookmarks);
    }

    private void ShowBookmarkBarItemContextMenu(BookmarkItem item, Point screenPt)
    {
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

        int curIdx = _ctx.Bookmarks.IndexOf(item);
        if (curIdx > 0)
        {
            ctx.Items.Add("Sola Taşı", null, (_, _) =>
            {
                _ctx.Bookmarks.RemoveAt(curIdx);
                _ctx.Bookmarks.Insert(curIdx - 1, item);
                _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                RenderBookmarksBar();
                _sidePanel.RefreshIfOpen("bm");
            });
        }
        if (curIdx >= 0 && curIdx < _ctx.Bookmarks.Count - 1)
        {
            ctx.Items.Add("Sağa Taşı", null, (_, _) =>
            {
                _ctx.Bookmarks.RemoveAt(curIdx);
                _ctx.Bookmarks.Insert(curIdx + 1, item);
                _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                RenderBookmarksBar();
                _sidePanel.RefreshIfOpen("bm");
            });
        }

        var folders = _ctx.Bookmarks.Where(f => f.IsFolder && f.Id != item.Id && !(item.IsFolder && SidePanelHost.IsDescendant(item, f.Id))).ToList();
        if (folders.Count > 0)
        {
            var moveSub = new ToolStripMenuItem("Klasöre Taşı");
            foreach (var f in folders)
            {
                var targetFolder = f;
                moveSub.DropDownItems.Add($"📁 {targetFolder.Name}", null, (_, _) =>
                {
                    SidePanelHost.RemoveBookmarkById(_ctx.Bookmarks, item.Id);
                    targetFolder.Children.Add(item);
                    _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                    RenderBookmarksBar();
                    _sidePanel.RefreshIfOpen("bm");
                });
            }
            ctx.Items.Add(moveSub);
        }

        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add("Sil", null, (_, _) =>
        {
            SidePanelHost.RemoveBookmarkById(_ctx.Bookmarks, item.Id);
            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
            RenderBookmarksBar();
            SyncBookmarkStar();
            _sidePanel.RefreshIfOpen("bm");
        });
        ctx.Show(screenPt);
    }

    private void OpenBookmarkFolderDropdown(Rectangle anchorScreenRect, BookmarkItem folder)
    {
        if (_activeBookmarkMenu is { Visible: true } && _activeBookmarkAnchorRect == anchorScreenRect)
        {
            CloseBookmarkFolderDropdown();
            return;
        }

        CloseBookmarkFolderDropdown();
        var menu = BuildFolderDropdown(folder);
        _activeBookmarkMenu = menu;
        _activeBookmarkAnchorRect = anchorScreenRect;

        menu.Closed += (_, _) =>
        {
            if (_activeBookmarkMenu == menu)
            {
                _activeBookmarkMenu = null;
                _activeBookmarkAnchorRect = Rectangle.Empty;
                _bmHoverTimer?.Stop();
            }
        };

        menu.Show(new Point(anchorScreenRect.Left, anchorScreenRect.Bottom + 2));

        _bmHoverTimer ??= new System.Windows.Forms.Timer { Interval = 80 };
        _bmHoverTimer.Tick -= OnBookmarkHoverTick;
        _bmHoverTimer.Tick += OnBookmarkHoverTick;
        _bmHoverTimer.Start();
    }

    private void OpenOverflowBookmarksDropdown(Rectangle anchorScreenRect, List<BookmarkItem> hiddenItems)
    {
        CloseBookmarkFolderDropdown();
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        foreach (var n in hiddenItems)
        {
            if (n.IsFolder)
            {
                var sub = new ToolStripMenuItem($"📁 {n.Name}");
                var inner = BuildFolderDropdown(n);
                foreach (ToolStripItem subItem in inner.Items.Cast<ToolStripItem>().ToList())
                    sub.DropDownItems.Add(subItem);
                menu.Items.Add(sub);
            }
            else
            {
                var item = n;
                var mi = new ToolStripMenuItem(item.Name);
                mi.Click += (_, _) => _ = NavigateActiveAsync(item.Url);
                menu.Items.Add(mi);
            }
        }
        _activeBookmarkMenu = menu;
        _activeBookmarkAnchorRect = anchorScreenRect;
        menu.Closed += (_, _) =>
        {
            if (_activeBookmarkMenu == menu)
            {
                _activeBookmarkMenu = null;
                _activeBookmarkAnchorRect = Rectangle.Empty;
                _bmHoverTimer?.Stop();
            }
        };
        menu.Show(new Point(Math.Max(8, anchorScreenRect.Right - 180), anchorScreenRect.Bottom + 2));
        _bmHoverTimer ??= new System.Windows.Forms.Timer { Interval = 80 };
        _bmHoverTimer.Tick -= OnBookmarkHoverTick;
        _bmHoverTimer.Tick += OnBookmarkHoverTick;
        _bmHoverTimer.Start();
    }

    private void OnBookmarkHoverTick(object? sender, EventArgs e)
    {
        if (_activeBookmarkMenu is null || !_activeBookmarkMenu.Visible || _activeBookmarkAnchorRect.IsEmpty)
        {
            _bmHoverTimer?.Stop();
            return;
        }

        var pt = Cursor.Position;
        bool lDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        bool rDown = (GetAsyncKeyState(0x02) & 0x8000) != 0;
        if ((lDown || rDown) && !IsPointInsideMenuTree(_activeBookmarkMenu, pt) && !_activeBookmarkAnchorRect.Contains(pt))
        {
            CloseBookmarkFolderDropdown();
            return;
        }

        var anchorRect = _activeBookmarkAnchorRect;
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
        _activeBookmarkAnchorRect = Rectangle.Empty;
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

        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        menu.Items.Add("Tüm Sayfayı Kaydet", null, async (_, _) => await CaptureScreenshotAsync(areaSelect: false));
        menu.Items.Add("Alan Seçerek Kaydet", null, async (_, _) => await CaptureScreenshotAsync(areaSelect: true));

        var dismissTimer = new System.Windows.Forms.Timer { Interval = 75 };
        dismissTimer.Tick += (_, _) =>
        {
            if (menu.IsDisposed || !menu.Visible) { dismissTimer.Stop(); dismissTimer.Dispose(); return; }
            bool click = ((GetAsyncKeyState(0x01) & 0x8000) != 0) || ((GetAsyncKeyState(0x02) & 0x8000) != 0);
            if (click && !menu.Bounds.Contains(Cursor.Position))
                menu.Close();
        };
        menu.Closed += (_, _) => { dismissTimer.Stop(); dismissTimer.Dispose(); };
        dismissTimer.Start();

        var btnRect = _shotBtn.RectangleToScreen(_shotBtn.ClientRectangle);
        menu.Show(new Point(btnRect.Left, btnRect.Bottom + 4));
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
        SyncZoomBadge();
    }

    private void ResetZoom()
    {
        if (_activeTab is null) return;
        _activeTab.ApplyZoom(1.0);
        SyncZoomBadge();
    }

    private void SyncZoomBadge()
    {
        int pct = (int)Math.Round((_activeTab?.ZoomFactor ?? 1.0) * 100);
        _zoomResetBtn.LabelText = $"%{pct}";
        _zoomResetBtn.IconColor = pct == 100 ? _palette.TextPrimary : _palette.Accent;
    }

    private void OpenNewWindow()
    {
        var win = new MainForm(_ctx, _incognito, _ctx.Settings.HomePage);
        win.Show();
    }

    private async Task ReopenClosedTabAsync()
    {
        if (_closedTabUrls.Count == 0) return;
        var url = _closedTabUrls.Pop();
        await AddTabAsync(url);
    }

    private void TogglePinActiveTab()
    {
        if (_activeTab is null) return;
        _activeTab.IsPinned = !_activeTab.IsPinned;
        ReorderPinnedTabs();
    }

    private void ToggleBookmarkBarVisibility()
    {
        _ctx.Settings.ShowBookmarkBar = !_ctx.Settings.ShowBookmarkBar;
        _ctx.Storage.SaveSettings(_ctx.Settings);
        RenderBookmarksBar();
    }

    private void ToggleLeftSidebarVisibility()
    {
        _ctx.Settings.ShowLeftSidebar = !_ctx.Settings.ShowLeftSidebar;
        _ctx.Storage.SaveSettings(_ctx.Settings);
        _leftSidebar.Visible = _ctx.Settings.ShowLeftSidebar;
        _contentHost.BringToFront();
    }

    private void OpenExtensionsDialog()
    {
        using var dlg = new ExtensionsDialog(
            _ctx,
            _activeTab?.WebView?.CoreWebView2?.Profile,
            url => _ = AddTabAsync(url),
            _palette);
        dlg.ShowDialog(this);
        RefreshPinnedExtensionsStrip();
    }

    private void UpdateExtensionStoreBanner()
    {
        var url = _activeTab?.Url ?? "";
        if (!_incognito && ExtensionService.TryExtractStoreExtension(url, out var storeName, out var extId))
        {
            bool alreadyInstalled = _ctx.Extensions.Items.Any(x => x.StoreId.Equals(extId, StringComparison.OrdinalIgnoreCase));
            if (alreadyInstalled || string.Equals(_dismissedExtId, extId, StringComparison.OrdinalIgnoreCase))
            {
                _extInstallBanner.Visible = false;
                return;
            }
            _detectedExtUrl = url;
            _extBannerText.Text = $"{storeName} eklentisi tespit edildi — tek tıkla ED Browser'a kurabilirsin.";
            _extInstallBtn.Enabled = true;
            _extInstallBtn.Text = "ED Browser'a Ekle";
            _extInstallBanner.Visible = true;
        }
        else
        {
            _detectedExtUrl = null;
            _extInstallBanner.Visible = false;
        }
    }

    private async Task InstallDetectedStoreExtensionAsync()
    {
        if (string.IsNullOrEmpty(_detectedExtUrl)) return;
        _extInstallBtn.Enabled = false;
        _extInstallBtn.Text = "Kuruluyor…";
        try
        {
            var ext = await _ctx.Extensions.InstallFromStoreUrlOrIdAsync(_detectedExtUrl, _activeTab?.WebView?.CoreWebView2?.Profile);
            _extInstallBanner.Visible = false;
            ShowToast("Eklenti Kuruldu", $"{ext.Name} (v{ext.Version}) eklendi.");
        }
        catch (Exception ex)
        {
            _extInstallBtn.Enabled = true;
            _extInstallBtn.Text = "ED Browser'a Ekle";
            MessageBox.Show(this, "Eklenti indirilemedi:\n" + ex.Message, "Eklenti Kurulumu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleThemeQuick()
    {
        if (_incognito) return;
        _ctx.Settings.Theme = _ctx.Settings.Theme == BrowserTheme.Dark ? BrowserTheme.Light : BrowserTheme.Dark;
        _ctx.Storage.SaveSettings(_ctx.Settings);
        _palette = ThemeService.GetPalette(_ctx.Settings.Theme, _incognito);
        ApplyTheme();
        RenderBookmarksBar();
    }

    private void ToggleAdBlockQuick()
    {
        _ctx.Settings.AdBlockEnabled = !_ctx.Settings.AdBlockEnabled;
        _ctx.Storage.SaveSettings(_ctx.Settings);
        UpdateAdShieldBadge();
    }

    private async Task SaveActivePageAsAsync()
    {
        if (_activeTab is not null)
            await _activeTab.SavePageAsAsync();
    }

    private async Task SaveActivePageAsPdfAsync()
    {
        if (_activeTab is null || !_activeTab.HasNavigated) return;
        using var sfd = new SaveFileDialog
        {
            Title = "Sayfayı PDF Olarak Kaydet",
            InitialDirectory = _ctx.Storage.DownloadsDirectory,
            FileName = $"sayfa_{DateTime.Now:yyyy-MM-dd_HH-mm}.pdf",
            Filter = "PDF Dosyası|*.pdf"
        };
        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            bool ok = await _activeTab.PrintToPdfAsync(sfd.FileName);
            ShowToast(ok ? "PDF Kaydedildi" : "PDF Hatası", Path.GetFileName(sfd.FileName));
        }
    }

    private void OpenClearBrowsingDataDialog()
    {
        using var dlg = new ClearBrowsingDataDialog(_palette);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.ClearHistory)
        {
            _ctx.History.Clear();
            _ctx.Storage.SaveHistory(_ctx.History);
            _sidePanel.RefreshIfOpen("hist");
        }
        if (dlg.ClearDownloads)
        {
            _ctx.Downloads.Clear();
            _ctx.Storage.SaveDownloads(_ctx.Downloads);
            _sidePanel.RefreshIfOpen("dl");
        }

        CoreWebView2BrowsingDataKinds kinds = 0;
        if (dlg.ClearCache) kinds |= CoreWebView2BrowsingDataKinds.DiskCache;
        if (dlg.ClearCookies) kinds |= CoreWebView2BrowsingDataKinds.Cookies;
        if (dlg.ClearSiteStorage)
        {
            kinds |= CoreWebView2BrowsingDataKinds.LocalStorage |
                     CoreWebView2BrowsingDataKinds.IndexedDb |
                     CoreWebView2BrowsingDataKinds.WebSql |
                     CoreWebView2BrowsingDataKinds.CacheStorage;
        }
        if (kinds != 0 && _activeTab is not null)
            _ = _activeTab.ClearBrowsingDataAsync(kinds);

        ShowToast("Veriler Temizlendi", "Seçili tarama verileri başarıyla temizlendi.");
    }

    private async Task CheckUpdatesInteractiveAsync()
    {
        var res = await UpdateService.CheckForUpdatesAsync(_ctx.Settings.Version, _ctx.Storage.IsInstalledMode);
        if (!string.IsNullOrEmpty(res.Error))
        {
            MessageBox.Show(this, "Güncelleme sunucusuna şu anda ulaşılamadı.\n" + res.Error, "Güncelleme Denetimi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (res.UpdateAvailable)
        {
            _updateDownloadUrl = res.DirectDownloadUrl;
            _updateReleaseUrl = res.ReleaseUrl;
            _updateBannerText.Text = $"Yeni ED Browser sürümü yayınlandı: v{res.LatestVersion} (Mevcut: v{res.CurrentVersion})";
            _updateBanner.Visible = true;
        }
        else
        {
            MessageBox.Show(this, $"ED Browser güncel! (v{res.CurrentVersion})", "Güncelleme Denetimi", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
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
            _ctx.VpnRouter.ApplySettings(_ctx.Settings);
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
        if (mods == Keys.Control && key == Keys.N) { OpenNewWindow(); return true; }
        if (mods == Keys.Control && key == Keys.W) { if (_activeTab is not null && !_activeTab.IsPinned) _ = CloseTabAsync(_activeTab); return true; }
        if (mods == Keys.Control && key == Keys.L) { _urlBox.Focus(); _urlBox.SelectAll(); return true; }
        if ((mods == Keys.Control && key == Keys.R) || keyData == Keys.F5) { _activeTab?.ReloadOrStop(); return true; }
        if (mods == Keys.Alt && key == Keys.Left) { _activeTab?.GoBack(); return true; }
        if (mods == Keys.Alt && key == Keys.Right) { _activeTab?.GoForward(); return true; }
        if (mods == Keys.Control && key == Keys.D) { ToggleCurrentBookmark(); return true; }
        if (mods == Keys.Control && key == Keys.F) { ToggleFindBar(); return true; }
        if (mods == Keys.Control && key == Keys.H) { _sidePanel.Toggle("hist"); return true; }
        if (mods == Keys.Control && key == Keys.S) { _ = SaveActivePageAsAsync(); return true; }
        if (mods == Keys.Control && key == Keys.U) { _ = ViewPageSourceAsync(); return true; }
        if (mods == Keys.Control && key == Keys.Oemcomma) { OpenSettings(); return true; }
        if (mods == Keys.Control && key == Keys.P) { _activeTab?.PrintPage(); return true; }
        if (mods == Keys.Control && (key == Keys.Oemplus || key == Keys.Add)) { ChangeZoom(0.10); return true; }
        if (mods == Keys.Control && (key == Keys.OemMinus || key == Keys.Subtract)) { ChangeZoom(-0.10); return true; }
        if (mods == Keys.Control && (key == Keys.D0 || key == Keys.NumPad0)) { ResetZoom(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.P) { OpenIncognitoWindow(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.T) { _ = ReopenClosedTabAsync(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.O) { _sidePanel.Toggle("bm"); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.B)
        {
            ToggleBookmarkBarVisibility();
            return true;
        }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.L) { ToggleLeftSidebarVisibility(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.V) { ToggleVerticalTabs(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.X) { _ = ToggleSplitViewAsync(); return true; }
        if (mods == (Keys.Control | Keys.Alt) && key == Keys.R) { if (_activeTab is not null) _ = _activeTab.ToggleReaderModeAsync(); return true; }
        if (mods == Keys.Shift && key == Keys.Escape) { OpenTaskManager(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.E) { OpenExtensionsDialog(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.J) { _sidePanel.Toggle("dl"); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.S) { ShowScreenshotMenu(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.Delete) { OpenClearBrowsingDataDialog(); return true; }
        if (mods == (Keys.Control | Keys.Shift) && key == Keys.C) { _ = StartElementPickerAsync(); return true; }
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
            if (_devToolsPanel.Visible) { CloseDevToolsPanel(); return true; }
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
        _verticalTabBar.ApplyTheme(_palette);
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
        UpdateVpnBadge();
        _readerBtn.NormalBg = _palette.BaseBg;
        _readerBtn.HoverBg = _palette.ElevatedBg;
        _readerBtn.IconColor = (_activeTab?.IsReaderMode ?? false) ? _palette.Accent : _palette.TextSecondary;
        _adShieldBtn.NormalBg = _palette.BaseBg;
        _adShieldBtn.HoverBg = _palette.ElevatedBg;
        _bmStarBtn.NormalBg = _palette.BaseBg;
        _bmStarBtn.HoverBg = _palette.ElevatedBg;

        _profileBtn.NormalBg = _palette.ElevatedBg;
        _profileBtn.HoverBg = _palette.HoverBg;
        _profileBtn.IconColor = _palette.Accent;
        _profileBtn.BorderColor = _palette.Border;

        _splitPane.BackColor = _palette.BaseBg;
        _splitHeader.BackColor = _palette.SurfaceBg;
        _splitSplitter.BackColor = _palette.Border;
        _splitUrlBox.BackColor = _palette.BaseBg;
        _splitUrlBox.ForeColor = _palette.TextPrimary;

        _ramBtn.NormalBg = _palette.ElevatedBg;
        _ramBtn.HoverBg = _palette.HoverBg;
        _ramBtn.IconColor = _palette.Green;
        _ramBtn.BorderColor = _palette.Border;

        _zoomPill.BackColor = _palette.SurfaceBg;
        _zoomOutBtn.NormalBg = _palette.ElevatedBg;
        _zoomOutBtn.HoverBg = _palette.HoverBg;
        _zoomOutBtn.IconColor = _palette.TextSecondary;
        _zoomInBtn.NormalBg = _palette.ElevatedBg;
        _zoomInBtn.HoverBg = _palette.HoverBg;
        _zoomInBtn.IconColor = _palette.TextSecondary;
        _zoomResetBtn.NormalBg = _palette.ElevatedBg;
        _zoomResetBtn.HoverBg = _palette.HoverBg;
        _zoomResetBtn.IconColor = _palette.TextPrimary;
        _zoomPill.Invalidate();

        _bookmarkBar.BackColor = _palette.SurfaceBg;
        _bookmarkBar.ApplyTheme(_palette);
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

        _extInstallBanner.BackColor = _palette.ElevatedBg;
        _extBannerText.ForeColor = _palette.Accent;
        _extInstallBtn.ApplyTheme(_palette);
        _extDismissBtn.NormalBg = _palette.ElevatedBg;
        _extDismissBtn.HoverBg = _palette.HoverBg;
        _extDismissBtn.IconColor = _palette.TextMuted;

        _leftSidebar.BackColor = _palette.SurfaceBg;
        foreach (var sb in _sidebarButtons)
        {
            sb.NormalBg = _palette.SurfaceBg;
            sb.HoverBg = _palette.HoverBg;
            sb.IconColor = _palette.TextSecondary;
        }

        _newTabPanel.ApplyTheme(_palette);
        _sidePanel.ApplyTheme(_palette);
        _devToolsPanel.ApplyTheme(_palette);
        _devToolsSplitter.BackColor = _palette.Border;
        RenderTabs();
        SyncAddressBar();
        SyncBookmarkStar();
        SyncZoomBadge();
        UpdateAdShieldBadge();
    }

    // ── CHROME-LIKE DOCKED DEVTOOLS (CDP) ───────────────────────────────────────
    private async Task ToggleDevToolsAsync()
    {
        if (_devToolsPanel.Visible)
        {
            CloseDevToolsPanel();
            return;
        }

        await OpenDevToolsPanelAsync();
    }

    private async Task OpenDevToolsPanelAsync()
    {
        _devToolsPanel.Visible = true;
        _devToolsSplitter.Visible = true;
        ApplyDevToolsDockMode(_devToolsPanel.DockMode);
        await _devToolsPanel.AttachToTabAsync(_activeTab);
    }

    private async Task InspectElementInDevToolsAsync(int clientX, int clientY)
    {
        await OpenDevToolsPanelAsync();
        await _devToolsPanel.InspectAtPointAsync(clientX, clientY);
    }

    private async Task StartElementPickerAsync()
    {
        await OpenDevToolsPanelAsync();
        await _devToolsPanel.ToggleElementPickerAsync();
    }

    private async Task ViewPageSourceAsync()
    {
        await OpenDevToolsPanelAsync();
        _devToolsPanel.SelectTab(4);
        await _devToolsPanel.RefreshSourcesListAsync();
    }

    private async Task HandleDevToolsElementPickedAsync(string json)
    {
        if (!_devToolsPanel.Visible)
            await OpenDevToolsPanelAsync();
        await _devToolsPanel.HandlePickedElementJsonAsync(json);
    }

    private void ApplyDevToolsDockMode(DevToolsDockMode mode)
    {
        _devToolsPanel.DockMode = mode;
        if (mode == DevToolsDockMode.Bottom)
        {
            _devToolsPanel.Dock = DockStyle.Bottom;
            _devToolsPanel.Height = 300;
            _devToolsSplitter.Dock = DockStyle.Bottom;
            _devToolsSplitter.Height = 5;
        }
        else
        {
            _devToolsPanel.Dock = DockStyle.Right;
            _devToolsPanel.Width = 540;
            _devToolsSplitter.Dock = DockStyle.Right;
            _devToolsSplitter.Width = 5;
        }
        _contentHost.BringToFront();
        PerformLayout();
    }

    private void CloseDevToolsPanel()
    {
        _devToolsPanel.Visible = false;
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

    private void OnVpnFailoverPromoted(FreeVpnServer srv)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _ctx.Settings.ActiveFreeServerUrl = srv.ProxyUrl;
                _ctx.Settings.ActiveFreeServerLabel = srv.DisplayText;
                _ctx.Settings.ActiveFreeServerCountry = srv.CountryCode;
                _ctx.Storage.SaveSettings(_ctx.Settings);
                UpdateVpnBadge();
            });
        }
        catch { }
    }

    private async Task EnsureHealthyFreeServerOnStartupAsync()
    {
        try
        {
            var curUrl = _ctx.Settings.ActiveFreeServerUrl;
            bool ok = !string.IsNullOrWhiteSpace(curUrl) && await ProxyService.VerifyRelayMultiSiteAsync(curUrl, 3500);
            if (!ok || !_ctx.VpnRouter.HasFailoverRoutes)
            {
                var found = await ProxyService.DiscoverVerifiedFreeRelaysAsync(8, _ctx.Settings.VpnPreferredRegion);
                if (IsDisposed || found.Count == 0) return;

                _ctx.VpnRouter.SetFreeFailoverPool(found);
                if (!ok)
                {
                    var best = found[0];
                    BeginInvoke(() =>
                    {
                        if (IsDisposed) return;
                        _ctx.Settings.ActiveFreeServerUrl = best.ProxyUrl;
                        _ctx.Settings.ActiveFreeServerLabel = best.DisplayText;
                        _ctx.Settings.ActiveFreeServerCountry = best.CountryCode;
                        _ctx.VpnRouter.ApplySettings(_ctx.Settings);
                        _ctx.Storage.SaveSettings(_ctx.Settings);
                        UpdateVpnBadge();
                    });
                }
            }
        }
        catch { }
    }

    // ── v5 ADVANCED FEATURES (SITE EXCEPTIONS, SPLIT VIEW, PROFILES, MEDIA, SESSION, TASK MGR) ──
    private static string TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : string.Empty;
    }

    private void ShowSiteSecurityMenu()
    {
        var host = TryGetHost(_activeTab?.Url);
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        var titleItem = new ToolStripMenuItem(string.IsNullOrEmpty(host) ? "Yeni Sekme (Yerel)" : $"Site: {host}") { Enabled = false };
        menu.Items.Add(titleItem);

        if (!string.IsNullOrEmpty(host))
        {
            bool adPaused = _ctx.Settings.AdBlockWhitelistHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
            menu.Items.Add(adPaused ? $"Bu Sitede Reklam Engelleyiciyi Aç ({host})" : $"Bu Sitede Reklam Engelleyiciyi Duraklat ({host})", null, (_, _) =>
            {
                if (adPaused)
                    _ctx.Settings.AdBlockWhitelistHosts.RemoveAll(x => x.Equals(host, StringComparison.OrdinalIgnoreCase));
                else
                    _ctx.Settings.AdBlockWhitelistHosts.Add(host);
                _ctx.Storage.SaveSettings(_ctx.Settings);
                UpdateAdShieldBadge();
                _activeTab?.ReloadOrStop();
            });

            bool httpsBypass = _ctx.Settings.HttpsOnlyBypassHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
            menu.Items.Add(httpsBypass ? "HTTPS-Only Zorlamasını Aç" : "Bu Sitede HTTP Bağlantısına İzin Ver", null, (_, _) =>
            {
                if (httpsBypass)
                    _ctx.Settings.HttpsOnlyBypassHosts.RemoveAll(x => x.Equals(host, StringComparison.OrdinalIgnoreCase));
                else
                    _ctx.Settings.HttpsOnlyBypassHosts.Add(host);
                _ctx.Storage.SaveSettings(_ctx.Settings);
            });
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("VPN & Gizlilik Kalkanını Aç", null, (_, _) => ShowVpnPrivacyPopup());
        menu.Items.Add("Okuyucu Modu (Ctrl+Alt+R)", null, async (_, _) =>
        {
            if (_activeTab is not null) await _activeTab.ToggleReaderModeAsync();
        });

        var r = _secBtn.RectangleToScreen(_secBtn.ClientRectangle);
        menu.Show(new Point(r.Left, r.Bottom + 4));
    }

    private void UpdateMediaBadge()
    {
        bool anyAudio = _tabs.Any(t => t.IsPlayingAudio);
        _mediaBtn.IconColor = anyAudio ? _palette.Accent : _palette.TextSecondary;
    }

    private void ShowMediaHubPopup()
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        var audioTabs = _tabs.Where(t => t.IsPlayingAudio || t.IsMuted || t.HasNavigated).Take(8).ToList();
        if (audioTabs.Count == 0)
        {
            menu.Items.Add("Oynatılan medya yok").Enabled = false;
        }
        else
        {
            foreach (var tab in audioTabs)
            {
                var t = tab;
                var label = string.IsNullOrWhiteSpace(t.Title) ? t.Url : t.Title;
                if (label.Length > 34) label = label[..34] + "…";
                var sub = new ToolStripMenuItem($"{(t.IsMuted ? "Sessiz" : t.IsPlayingAudio ? "Çalıyor" : "Sekme")} • {label}");
                sub.DropDownItems.Add("Oynat / Duraklat", null, async (_, _) => await t.ToggleMediaPlayPauseAsync());
                sub.DropDownItems.Add(t.IsMuted ? "Sesi Aç" : "Sesi Kapat", null, (_, _) => t.ToggleMute());
                sub.DropDownItems.Add("Resim İçinde Resim (PiP)", null, async (_, _) => await t.TogglePictureInPictureAsync());
                sub.DropDownItems.Add("Sekmeye Git", null, async (_, _) => await SwitchToTabAsync(t));
                menu.Items.Add(sub);
            }
        }
        var r = _mediaBtn.RectangleToScreen(_mediaBtn.ClientRectangle);
        menu.Show(new Point(r.Left, r.Bottom + 4));
    }

    private void RefreshPinnedExtensionsStrip()
    {
        _extIconsHost.Controls.Clear();
        var pinned = _ctx.Extensions.Items.Where(e => e.Enabled && !string.IsNullOrEmpty(e.PopupPath)).Take(3).ToList();
        _extIconsHost.Visible = pinned.Count > 0;
        foreach (var ext in pinned)
        {
            var eItem = ext;
            var btn = new IconButton
            {
                IconType = VectorIcon.Puzzle,
                Size = new Size(28, 30),
                IconSizePx = 14,
                CornerRadius = 7,
                NormalBg = _palette.SurfaceBg,
                HoverBg = _palette.HoverBg,
                IconColor = _palette.Accent,
                Margin = new Padding(1, 0, 1, 0)
            };
            btn.SetTooltip($"{eItem.Name} (Aç)");
            btn.Click += (_, _) =>
            {
                var pt = btn.RectangleToScreen(btn.ClientRectangle);
                var pop = new ExtensionPopupForm(_ctx, eItem, _palette, new Point(pt.Right, pt.Bottom + 6));
                pop.Show(this);
            };
            _extIconsHost.Controls.Add(btn);
        }
    }

    private void ShowProfileMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
        var profiles = _ctx.Profiles.LoadProfiles();
        var curId = _ctx.Storage.ActiveProfileId;

        foreach (var prof in profiles)
        {
            var p = prof;
            bool isCur = p.Id.Equals(curId, StringComparison.OrdinalIgnoreCase);
            var mi = new ToolStripMenuItem($"{(isCur ? "✓ " : "")}{p.Name}") { Enabled = !isCur };
            mi.Click += (_, _) => ProfileManagerService.LaunchProfileWindow(p.Id);
            menu.Items.Add(mi);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("+ Yeni Profil Oluştur…", null, (_, _) =>
        {
            var name = PromptDialog.Show(this, "Yeni Kullanıcı Profili", "Profil adı (ör: İş, Kişisel, Test):", "", _palette);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var created = _ctx.Profiles.CreateProfile(name.Trim());
                ProfileManagerService.LaunchProfileWindow(created.Id);
            }
        });
        if (!curId.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add("Bu Profili Sil", null, (_, _) =>
            {
                if (_ctx.Profiles.DeleteProfile(curId))
                    ShowToast("Profil Silindi", "Bu pencere kapatılabilir.");
            });
        }

        var r = _profileBtn.RectangleToScreen(_profileBtn.ClientRectangle);
        menu.Show(new Point(r.Right - 180, r.Bottom + 4));
    }

    private void OpenTaskManager()
    {
        using var dlg = new TaskManagerDialog(
            _palette,
            () => _tabs.AsReadOnly(),
            CloseTabAsync,
            RefreshMemoryBadge);
        dlg.ShowDialog(this);
    }

    private void ToggleVerticalTabs()
    {
        _ctx.Settings.UseVerticalTabs = !_ctx.Settings.UseVerticalTabs;
        _ctx.Storage.SaveSettings(_ctx.Settings);
        _verticalTabBar.Visible = _ctx.Settings.UseVerticalTabs;
        _tabBar.Visible = !_ctx.Settings.UseVerticalTabs;
        RenderTabs();
    }

    private async Task ToggleSplitViewAsync()
    {
        if (_splitPane.Visible)
        {
            _splitPane.Visible = false;
            _splitSplitter.Visible = false;
            _splitBtn.IsActive = false;
            return;
        }

        _splitPane.Width = Math.Max(340, Width / 2 - 40);
        _splitSplitter.Visible = true;
        _splitPane.Visible = true;
        _splitBtn.IsActive = true;
        _contentHost.BringToFront();

        if (_splitTab is null)
        {
            _splitTab = new BrowserTab(_ctx, _splitWebHost, _incognito, _incognitoDir);
            _splitTab.StateChanged += t => BeginInvoke(() =>
            {
                if (!_splitUrlBox.Focused && !string.IsNullOrWhiteSpace(t.Url))
                    _splitUrlBox.Text = t.Url;
            });
            _splitTab.OpenNewTabRequested += (_t, u) => BeginInvoke(() => { _ = AddTabAsync(u); });
            var startUrl = !string.IsNullOrWhiteSpace(_activeTab?.Url) ? _activeTab.Url : _ctx.Settings.HomePage;
            _splitUrlBox.Text = startUrl;
            await _splitTab.NavigateAsync(startUrl);
        }
        else
        {
            await _splitTab.ActivateAsync();
        }
    }

    private async Task NavigateSplitAsync(string input)
    {
        if (_splitTab is null) return;
        var target = _ctx.NormalizeUrlOrSearch(input);
        if (string.IsNullOrWhiteSpace(target)) return;
        await _splitTab.NavigateAsync(target);
    }

    private async Task SwapSplitViewsAsync()
    {
        if (_activeTab is null || _splitTab is null) return;
        var leftUrl = _activeTab.Url;
        var rightUrl = _splitTab.Url;
        if (!string.IsNullOrWhiteSpace(rightUrl))
            await NavigateActiveAsync(rightUrl);
        if (!string.IsNullOrWhiteSpace(leftUrl))
            await NavigateSplitAsync(leftUrl);
    }

    private void SaveCurrentSession()
    {
        if (_incognito) return;
        try
        {
            var sess = new SavedSession
            {
                ActiveIndex = _activeTab is null ? 0 : Math.Max(0, _tabs.IndexOf(_activeTab))
            };
            foreach (var t in _tabs)
            {
                sess.Tabs.Add(new SessionTabState
                {
                    Url = t.Url,
                    Title = t.Title,
                    IsPinned = t.IsPinned,
                    GroupName = t.GroupName,
                    GroupColorHex = t.GroupColor.IsEmpty ? "" : ColorTranslator.ToHtml(t.GroupColor)
                });
            }
            _ctx.Storage.SaveSession(sess);
        }
        catch { }
    }

    private async Task<bool> RestorePreviousSessionAsync()
    {
        var sess = _ctx.Storage.LoadSession();
        if (sess.Tabs.Count == 0 || !sess.Tabs.Any(x => !string.IsNullOrWhiteSpace(x.Url)))
            return false;

        int activeIdx = Math.Clamp(sess.ActiveIndex, 0, sess.Tabs.Count - 1);
        for (int i = 0; i < sess.Tabs.Count; i++)
        {
            var st = sess.Tabs[i];
            var tab = new BrowserTab(_ctx, _contentHost, _incognito, _incognitoDir)
            {
                IsPinned = st.IsPinned,
                GroupName = st.GroupName
            };
            if (!string.IsNullOrWhiteSpace(st.GroupColorHex))
            {
                try { tab.GroupColor = ColorTranslator.FromHtml(st.GroupColorHex); } catch { }
            }
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
            tab.ZoomChanged += (t, _) => BeginInvoke(() => { if (t == _activeTab) SyncZoomBadge(); });
            tab.InspectRequested += (x, y) => BeginInvoke(() => { _ = InspectElementInDevToolsAsync(x, y); });
            tab.ViewSourceRequested += () => BeginInvoke(() => { _ = ViewPageSourceAsync(); });
            tab.DevToolsElementPicked += json => BeginInvoke(() => { _ = HandleDevToolsElementPickedAsync(json); });

            _tabs.Add(tab);
            if (i == activeIdx && !string.IsNullOrWhiteSpace(st.Url))
                await tab.NavigateAsync(st.Url);
            else if (!string.IsNullOrWhiteSpace(st.Url))
                tab.SetRestoredStub(st.Title, st.Url);
        }

        if (_tabs.Count > 0)
        {
            await SwitchToTabAsync(_tabs[Math.Clamp(activeIdx, 0, _tabs.Count - 1)]);
            return true;
        }
        return false;
    }

    private void CleanupOnExit()
    {
        SaveCurrentSession();
        _ctx.VpnRouter.FailoverServerPromoted -= OnVpnFailoverPromoted;
        _memoryTimer.Stop();
        _autocompletePopup.Dispose();
        _activeAppMenu?.SafeClose();
        _activeVpnPopup?.SafeClose();
        _bmHoverTimer?.Stop();
        _bmHoverTimer?.Dispose();
        _splitTab?.Dispose();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_ctx.Settings.ClearDataOnExit && !_incognito)
            _ctx.History.Clear();

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

    public static int RunSelfTests(string? logPath = null)
    {
        int failed = 0;
        var logLines = new List<string>();
        void Log(string line)
        {
            Console.WriteLine(line);
            logLines.Add(line);
        }
        void Assert(bool cond, string name)
        {
            Log($"{(cond ? "[PASS]" : "[FAIL]")} {name}");
            if (!cond) failed++;
        }

        try
        {
            // 1. Test BookmarkBarControl direct mouse-capture drag-and-drop (reorder + folder drop)
            using (var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(1000, 100), ShowInTaskbar = false })
            using (var bar = new BookmarkBarControl { Width = 960 })
            {
                var bms = new List<BookmarkItem>
                {
                    new() { Type = "bookmark", Name = "Google", Url = "https://google.com" },
                    new() { Type = "bookmark", Name = "GitHub", Url = "https://github.com" },
                    new() { Type = "bookmark", Name = "YouTube", Url = "https://youtube.com" },
                    new() { Type = "folder", Name = "Araçlar", Children = new List<BookmarkItem>() }
                };
                bar.SetBookmarks(bms);
                host.Controls.Add(bar);
                host.Show();
                Application.DoEvents();

                Assert(bar.VisibleCount == 4, "BookmarkBarControl renders all 4 sample items");

                var rect2 = bar.GetItemClientRect(2);
                var dropRight = new Point(rect2.Right + 6, rect2.Top + rect2.Height / 2);
                bool moved1 = bar.PerformDragDropSimulation(0, dropRight);
                Assert(moved1 && bms[0].Name == "GitHub" && bms[1].Name == "YouTube" && bms[2].Name == "Google",
                    $"Drag forward reorder [Google -> after YouTube] => [{string.Join(", ", bms.Select(x => x.Name))}]");

                var rect0 = bar.GetItemClientRect(0);
                var dropLeft = new Point(rect0.Left - 2, rect0.Top + rect0.Height / 2);
                bool moved2 = bar.PerformDragDropSimulation(2, dropLeft);
                Assert(moved2 && bms[0].Name == "Google" && bms[1].Name == "GitHub" && bms[2].Name == "YouTube",
                    $"Drag backward reorder [Google -> before GitHub] => [{string.Join(", ", bms.Select(x => x.Name))}]");

                var folderRect = bar.GetItemClientRect(3);
                var folderCenter = new Point(folderRect.Left + folderRect.Width / 2, folderRect.Top + folderRect.Height / 2);
                bool moved3 = bar.PerformDragDropSimulation(0, folderCenter);
                Assert(moved3 && bms.Count == 3 && bms[2].Children.Count == 1 && bms[2].Children[0].Name == "Google",
                    "Drag bookmark onto folder moves item into Folder.Children");

                host.Close();
            }

            // 2. Test DevToolsPanel tabs, dock mode, and theme switch
            using (var dt = new DevToolsPanel { Size = new Size(500, 700) })
            {
                dt.PopulatePreviewSampleData();
                for (int t = 0; t <= 4; t++) dt.SelectTab(t);
                dt.SelectTab(0);
                dt.DockMode = DevToolsDockMode.Bottom;
                dt.ApplyTheme(ThemeService.Light);
                dt.ApplyTheme(ThemeService.Dark);
                Assert(dt.ActiveTabIndex == 0 && dt.DockMode == DevToolsDockMode.Bottom, "DevToolsPanel tabs, dock toggle & theme switch");
            }

            // 3. Test AppMenuPopup bounds and item count
            using (var owner = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(400, 400), ShowInTaskbar = false })
            {
                owner.Show();
                using var popup = new AppMenuPopup(owner, new AppMenuState { Version = "4.2.0", TotalRamMb = 49, CanReopenClosedTab = true, HasNavigatedTab = true },
                    ThemeService.Dark, new Point(1200, 120), enableOutsideDismiss: false);
                popup.Show();
                Application.DoEvents();
                Assert(popup.MenuItemCount >= 22 && popup.MaxItemRightEdge <= popup.Width,
                    $"AppMenuPopup renders {popup.MenuItemCount} rows cleanly inside {popup.Width}px width");
                popup.SafeClose();
                owner.Close();
            }

            // 4. Test ClearBrowsingDataDialog
            using (var clr = new ClearBrowsingDataDialog(ThemeService.Dark))
            {
                clr.StartPosition = FormStartPosition.Manual;
                clr.Location = new Point(-32000, -32000);
                clr.Show();
                Application.DoEvents();
                Assert(clr.Visible, "ClearBrowsingDataDialog renders cleanly");
                clr.Close();
            }

            // 5. Test ExtensionService URL detection & CRX3 unpacking + ExtensionsDialog
            bool cwsOk = ExtensionService.TryExtractStoreExtension("https://chromewebstore.google.com/detail/ublock-origin/cjpalhdlnbpafiamejdnhcphjbkeiagm", out var s1, out var id1);
            bool edgeOk = ExtensionService.TryExtractStoreExtension("https://microsoftedge.microsoft.com/addons/detail/ublock-origin/odfafepnkmbhccpbejgmiehpchacaeak", out var s2, out var id2);
            bool rawOk = ExtensionService.TryExtractStoreExtension("cjpalhdlnbpafiamejdnhcphjbkeiagm", out var s3, out var id3);
            Assert(cwsOk && s1 == "Chrome Web Store" && id1 == "cjpalhdlnbpafiamejdnhcphjbkeiagm" &&
                   edgeOk && s2 == "Edge Add-ons" && id2 == "odfafepnkmbhccpbejgmiehpchacaeak" &&
                   rawOk && s3 == "Chrome Web Store" && id3 == "cjpalhdlnbpafiamejdnhcphjbkeiagm",
                "ExtensionService parses Chrome Web Store, Edge Add-ons & raw IDs");

            var tmpExtDir = Path.Combine(Path.GetTempPath(), $"ed_crx3_test_{Guid.NewGuid():N}");
            try
            {
                using var zipMs = new MemoryStream();
                using (var za = new System.IO.Compression.ZipArchive(zipMs, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    var entry = za.CreateEntry("manifest.json");
                    using var sw = new StreamWriter(entry.Open());
                    sw.Write("{\"manifest_version\":3,\"name\":\"Test Ext\",\"version\":\"1.0.0\"}");
                }
                var zipBytes = zipMs.ToArray();
                var crxBytes = new byte[12 + 4 + zipBytes.Length];
                crxBytes[0] = (byte)'C'; crxBytes[1] = (byte)'r'; crxBytes[2] = (byte)'2'; crxBytes[3] = (byte)'4';
                BitConverter.GetBytes((uint)3).CopyTo(crxBytes, 4);
                BitConverter.GetBytes((uint)4).CopyTo(crxBytes, 8);
                Buffer.BlockCopy(zipBytes, 0, crxBytes, 16, zipBytes.Length);
                ExtensionService.UnpackCrxOrZipBytes(crxBytes, tmpExtDir);
                Assert(File.Exists(Path.Combine(tmpExtDir, "manifest.json")), "ExtensionService unpacks CRX3 payload safely");
            }
            finally
            {
                try { if (Directory.Exists(tmpExtDir)) Directory.Delete(tmpExtDir, true); } catch { }
            }

            var dummyCtx = new BrowserContext();
            using (var extDlg = new ExtensionsDialog(dummyCtx, null, _ => { }, ThemeService.Dark))
            {
                extDlg.StartPosition = FormStartPosition.Manual;
                extDlg.Location = new Point(-32000, -32000);
                extDlg.Show();
                Application.DoEvents();
                Assert(extDlg.Visible, "ExtensionsDialog renders cleanly");
                extDlg.Close();
            }

            // 6. Test Privacy, HTTPS-Only, Tracking Stripper, Proxy/DoH Args & VpnPrivacyPopup
            var dirtyUrl = "https://example.com/path?utm_source=x&fbclid=abc123&id=42&gclid=999&tab=2#sec";
            var cleanUrl = ProxyService.CleanTrackingParameters(dirtyUrl);
            Assert(cleanUrl == "https://example.com/path?id=42&tab=2#sec",
                $"Tracking param stripper => {cleanUrl}");

            bool upPub = ProxyService.TryUpgradeToHttps("http://example.com/page", out var upRes);
            bool upLoc = ProxyService.TryUpgradeToHttps("http://localhost:8080/api", out _);
            Assert(upPub && upRes == "https://example.com/page" && !upLoc,
                "HTTPS-Only upgrades public HTTP URLs and skips localhost");

            var warpSettings = new BrowserSettings { ProxyMode = ProxyMode.CloudflareWarp, DohProvider = DohProvider.Quad9, WebRtcLeakProtection = true };
            var warpArgs = ProxyService.BuildChromiumProxyArgs(warpSettings);
            var browserArgs = BrowserContext.BuildBrowserArguments(warpSettings);
            Assert(warpArgs.Contains("127.0.0.1:40000") && warpArgs.Contains("dns.quad9.net") && browserArgs.Contains("disable_non_proxied_udp"),
                "ProxyService & BrowserContext emit WARP, Quad9 DoH & WebRTC leak flags");

            using (var vpnPop = new VpnPrivacyPopup(null, dummyCtx, ThemeService.Dark, new Point(-31800, -31800), () => { }, () => { }, _ => { }, enableOutsideDismiss: false))
            {
                vpnPop.Location = new Point(-32000, -32000);
                vpnPop.Show();
                Application.DoEvents();
                Assert(vpnPop.Visible, "VpnPrivacyPopup renders cleanly");

                bool initialVpn = dummyCtx.Settings.VpnEnabled;
                vpnPop.SimulateVpnToggleForTest(true);
                bool enabledOk = dummyCtx.Settings.VpnEnabled && vpnPop.Visible;
                vpnPop.SimulateVpnToggleForTest(false);
                bool disabledOk = !dummyCtx.Settings.VpnEnabled && vpnPop.Visible;
                dummyCtx.Settings.VpnEnabled = initialVpn;
                Assert(enabledOk && disabledOk, "VpnPrivacyPopup instant ON/OFF toggle works without closing popup or restarting");

                vpnPop.SafeClose();
            }

            // 7. Test LocalVpnRouterService zero-restart HTTP loopback proxy & live tunnel switching
            using (var echoListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
            using (var router = new LocalVpnRouterService())
            {
                echoListener.Start();
                int echoPort = ((System.Net.IPEndPoint)echoListener.LocalEndpoint).Port;
                using var echoCts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    while (!echoCts.IsCancellationRequested)
                    {
                        try
                        {
                            using var c = await echoListener.AcceptTcpClientAsync(echoCts.Token);
                            using var st = c.GetStream();
                            var buf = new byte[1024];
                            _ = await st.ReadAsync(buf, echoCts.Token);
                            var resp = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 7\r\nConnection: close\r\n\r\nPONG_OK");
                            await st.WriteAsync(resp, echoCts.Token);
                        }
                        catch { break; }
                    }
                });

                router.Start();
                router.ApplySettings(new BrowserSettings { VpnEnabled = false, ProxyMode = ProxyMode.Direct });

                using var handler = new System.Net.Http.SocketsHttpHandler
                {
                    Proxy = new System.Net.WebProxy($"http://127.0.0.1:{router.ListenPort}"),
                    UseProxy = true
                };
                using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                var body1 = http.GetStringAsync($"http://127.0.0.1:{echoPort}/ping1").GetAwaiter().GetResult();

                router.ApplySettings(new BrowserSettings { VpnEnabled = true, ProxyMode = ProxyMode.Custom, CustomProxy = "socks5://127.0.0.1:40000" });
                bool switchedSummaryOk = router.CurrentRouteSummary.Contains("SOCKS5", StringComparison.OrdinalIgnoreCase);

                router.ApplySettings(new BrowserSettings { VpnEnabled = false, ProxyMode = ProxyMode.Direct });
                var body2 = http.GetStringAsync($"http://127.0.0.1:{echoPort}/ping2").GetAwaiter().GetResult();

                echoCts.Cancel();
                Assert(router.IsRunning && router.ListenPort > 0 && body1 == "PONG_OK" && switchedSummaryOk && body2 == "PONG_OK",
                    $"LocalVpnRouterService loopback proxy & live tunnel switching verified (port={router.ListenPort})");

                // 8. Test LocalVpnRouterService automatic failover promotion on dead primary FreeCommunity relay
                using var backupProxy = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                using var targetEcho = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                backupProxy.Start();
                targetEcho.Start();
                int backupPort = ((System.Net.IPEndPoint)backupProxy.LocalEndpoint).Port;
                int targetPort = ((System.Net.IPEndPoint)targetEcho.LocalEndpoint).Port;
                using var foCts = new CancellationTokenSource();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var c = await targetEcho.AcceptTcpClientAsync(foCts.Token);
                        using var st = c.GetStream();
                        var b = new byte[64];
                        _ = await st.ReadAsync(b, foCts.Token);
                        await st.WriteAsync(System.Text.Encoding.ASCII.GetBytes("TUNNEL_FAILOVER_OK"), foCts.Token);
                    }
                    catch { }
                });
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var c = await backupProxy.AcceptTcpClientAsync(foCts.Token);
                        using var st = c.GetStream();
                        var b = new byte[1024];
                        _ = await st.ReadAsync(b, foCts.Token);
                        await st.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), foCts.Token);
                        using var up = new System.Net.Sockets.TcpClient();
                        await up.ConnectAsync(System.Net.IPAddress.Loopback, targetPort, foCts.Token);
                        using var ups = up.GetStream();
                        var t1 = st.CopyToAsync(ups, foCts.Token);
                        var t2 = ups.CopyToAsync(st, foCts.Token);
                        await Task.WhenAny(t1, t2);
                    }
                    catch { }
                });

                FreeVpnServer? promoted = null;
                router.FailoverServerPromoted += s => promoted = s;
                var deadSrv = new FreeVpnServer("http://127.0.0.1:1", "http", "XX", "Dead", 999, "127.0.0.1");
                var goodSrv = new FreeVpnServer($"http://127.0.0.1:{backupPort}", "http", "DE", "Almanya", 12, "127.0.0.1");
                router.SetFreeFailoverPool(new[] { deadSrv, goodSrv });
                router.ApplySettings(new BrowserSettings
                {
                    VpnEnabled = true,
                    ProxyMode = ProxyMode.FreeCommunity,
                    ActiveFreeServerUrl = deadSrv.ProxyUrl,
                    ActiveFreeServerCountry = "XX",
                    ActiveFreeServerLabel = "Dead"
                });

                using var clientViaRouter = new System.Net.Sockets.TcpClient();
                clientViaRouter.Connect(System.Net.IPAddress.Loopback, router.ListenPort);
                using var rStream = clientViaRouter.GetStream();
                var connectReq = System.Text.Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{targetPort} HTTP/1.1\r\nHost: 127.0.0.1:{targetPort}\r\n\r\n");
                rStream.Write(connectReq, 0, connectReq.Length);
                var respBuf = new byte[512];
                int n = rStream.Read(respBuf, 0, respBuf.Length);
                var respHead = System.Text.Encoding.ASCII.GetString(respBuf, 0, n);
                rStream.Write(System.Text.Encoding.ASCII.GetBytes("HI"), 0, 2);
                int n2 = rStream.Read(respBuf, 0, respBuf.Length);
                var tunnelPayload = System.Text.Encoding.ASCII.GetString(respBuf, 0, n2);
                foCts.Cancel();

                Assert(respHead.Contains("200") && tunnelPayload == "TUNNEL_FAILOVER_OK" && promoted?.CountryCode == "DE",
                    "LocalVpnRouterService automatically fails over dead FreeCommunity relay and promotes healthy backup");

                // 9. Test Kill Switch (503 leak prevention) & Live Bandwidth Counter
                router.ApplySettings(new BrowserSettings { VpnEnabled = true, VpnKillSwitch = true, ProxyMode = ProxyMode.FreeCommunity, ActiveFreeServerUrl = "" });
                bool ksFlag = router.IsKillSwitchBlocking;
                using var ksClient = new System.Net.Sockets.TcpClient();
                ksClient.Connect(System.Net.IPAddress.Loopback, router.ListenPort);
                using var ksStream = ksClient.GetStream();
                var ksReq = System.Text.Encoding.ASCII.GetBytes("CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n");
                ksStream.Write(ksReq, 0, ksReq.Length);
                var ksRespBuf = new byte[256];
                int ksLen = ksStream.Read(ksRespBuf, 0, ksRespBuf.Length);
                var ksResp = System.Text.Encoding.ASCII.GetString(ksRespBuf, 0, ksLen);
                router.RecordTrafficForTest(4096, 1024);
                var bw = router.SampleBandwidth();
                Assert(ksFlag && ksResp.Contains("503") && bw.TotalDown >= 4096 && bw.TotalUp >= 1024,
                    "LocalVpnRouterService Kill Switch blocks direct leak (503) & bandwidth meter counts bytes");
            }

            // 10. Test Session Restore roundtrip + Multi-Profile Isolation + AdBlock Whitelist & EasyList parser
            var testProfId = "testprof_" + Guid.NewGuid().ToString("N")[..6];
            var profStorage = new StorageService(testProfId);
            profStorage.SaveSession(new SavedSession
            {
                ActiveIndex = 1,
                Tabs = new()
                {
                    new() { Url = "https://github.com", Title = "GitHub", GroupName = "İş", GroupColorHex = "#38BDF8" },
                    new() { Url = "https://google.com", Title = "Google", IsPinned = true }
                }
            });
            var loadedSess = profStorage.LoadSession();
            Assert(profStorage.ActiveProfileId == StorageService.SanitizeProfileId(testProfId) &&
                   loadedSess.Tabs.Count == 2 && loadedSess.ActiveIndex == 1 && loadedSess.Tabs[0].GroupName == "İş",
                "Multi-profile isolated StorageService & Session Restore roundtrip verified");
            try { if (Directory.Exists(profStorage.DataDirectory)) Directory.Delete(profStorage.DataDirectory, true); } catch { }

            var ab = new AdBlockerService();
            var parsedRules = AdBlockerService.ParseDomainFilterLines(new[] { "||ads.testdomain.org^", "0.0.0.0 tracker.badsite.net", "# comment" });
            bool wlSkip = ab.ShouldBlock("https://doubleclick.net/ad.js", "example.com", new[] { "example.com" });
            bool wlBlock = ab.ShouldBlock("https://doubleclick.net/ad.js", "other.com", new[] { "example.com" });
            Assert(parsedRules.Contains("ads.testdomain.org") && parsedRules.Contains("tracker.badsite.net") && !wlSkip && wlBlock &&
                   AdBlockerService.CosmeticHideCss.Contains("adsbygoogle"),
                "AdBlockerService EasyList parser, per-site whitelist & cosmetic CSS verified");

            // 11. Test v5 UI controls (VerticalTabBarControl, OmniboxAutocompletePopup, TaskManagerDialog)
            using (var vHost = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(400, 500), ShowInTaskbar = false })
            using (var vBar = new VerticalTabBarControl())
            {
                vBar.SetTabs(new[] { new ModernTabItem { Id = "1", Title = "Test", Active = true, GroupName = "İş", GroupColor = ThemeService.Dark.Accent } });
                vHost.Controls.Add(vBar);
                vHost.Show();
                vBar.ToggleCollapsed();
                vBar.ToggleCollapsed();
                Assert(vBar.Width >= 200, "VerticalTabBarControl renders, supports groups & toggles collapsed state");
                vHost.Close();
            }

            var acList = OmniboxAutocompletePopup.BuildLocalSuggestions("git", dummyCtx);
            Assert(acList.Count >= 1, "OmniboxAutocompletePopup builds merged local + search suggestions");

            using (var tm = new TaskManagerDialog(ThemeService.Dark, () => Array.Empty<BrowserTab>(), _ => Task.CompletedTask, () => { }))
            {
                tm.StartPosition = FormStartPosition.Manual;
                tm.Location = new Point(-32000, -32000);
                tm.Show();
                Application.DoEvents();
                Assert(tm.Visible, "TaskManagerDialog renders cleanly");
                tm.Close();
            }

            dummyCtx.Favicons.Dispose();
            dummyCtx.VpnRouter.Dispose();
        }
        catch (Exception ex)
        {
            failed++;
            Log("[EXCEPTION] " + ex);
        }

        Log(failed == 0 ? "ALL SELF-TESTS PASSED" : $"SELF-TESTS FAILED: {failed}");
        if (!string.IsNullOrEmpty(logPath))
        {
            try { File.WriteAllLines(logPath, logLines); } catch { }
        }
        return failed == 0 ? 0 : 1;
    }

    public static int VerifyLiveVpnEndToEnd(string? logPath = null)
    {
        var lines = new List<string>();
        void Log(string m) { Console.WriteLine(m); lines.Add(m); }

        int exit = 1;
        using var router = new LocalVpnRouterService();
        try
        {
            var found = ProxyService.DiscoverVerifiedFreeRelaysAsync(6).GetAwaiter().GetResult();
            Log($"Verified multi-site relays found: {found.Count}");
            foreach (var s in found)
                Log($" - {s.DisplayText} | IP={s.ExitIp} | {s.ProxyUrl}");

            if (found.Count > 0)
            {
                router.Start();
                router.SetFreeFailoverPool(found);
                var cfg = new BrowserSettings
                {
                    VpnEnabled = true,
                    ProxyMode = ProxyMode.FreeCommunity,
                    ActiveFreeServerUrl = found[0].ProxyUrl,
                    ActiveFreeServerCountry = found[0].CountryCode,
                    ActiveFreeServerLabel = found[0].DisplayText
                };
                router.ApplySettings(cfg);

                using var h = new System.Net.Http.SocketsHttpHandler
                {
                    Proxy = new System.Net.WebProxy($"http://127.0.0.1:{router.ListenPort}"),
                    UseProxy = true,
                    ConnectTimeout = TimeSpan.FromSeconds(6)
                };
                using var http = new System.Net.Http.HttpClient(h) { Timeout = TimeSpan.FromSeconds(10) };

                var gResp = http.GetAsync("https://www.google.com/generate_204").GetAwaiter().GetResult();
                var ipResp = http.GetAsync("https://ip-adresim.net/").GetAwaiter().GetResult();
                Log($"Live Router -> google/generate_204: {(int)gResp.StatusCode}");
                Log($"Live Router -> https://ip-adresim.net/: {(int)ipResp.StatusCode}");

                if ((int)gResp.StatusCode is 204 or 200 && (int)ipResp.StatusCode is >= 200 and < 400)
                {
                    Log("LIVE VPN END-TO-END PASS");
                    exit = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Log("[LIVE EXCEPTION] " + ex.Message);
        }

        if (!string.IsNullOrEmpty(logPath))
        {
            try { File.WriteAllLines(logPath, lines); } catch { }
        }
        return exit;
    }

    public static void RenderUiPreview(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var ctx = new BrowserContext();
        bool prevBmBar = ctx.Settings.ShowBookmarkBar;
        ctx.Settings.ShowBookmarkBar = true;
        bool addedSampleBm = false;
        if (ctx.Bookmarks.Count == 0)
        {
            addedSampleBm = true;
            ctx.Bookmarks.Add(new BookmarkItem { Type = "folder", Name = "Çalışma", Children = new() { new() { Type = "bookmark", Name = "GitHub", Url = "https://github.com" } } });
            ctx.Bookmarks.Add(new BookmarkItem { Type = "bookmark", Name = "Google", Url = "https://google.com" });
            ctx.Bookmarks.Add(new BookmarkItem { Type = "bookmark", Name = "YouTube", Url = "https://youtube.com" });
            ctx.Bookmarks.Add(new BookmarkItem { Type = "bookmark", Name = "ED Browser", Url = "https://github.com/edizdar/ED-Browser" });
        }

        using var main = new MainForm(ctx, incognito: false, initialUrl: null, skipInitialNavigate: true);
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-32000, -32000);
        main.Show();
        main._bookmarkBar.SetBookmarks(ctx.Bookmarks);
        Application.DoEvents();
        using (var bmp = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(bmp, new Rectangle(0, 0, main.Width, main.Height));
            bmp.Save(Path.Combine(outDir, "main_dark.png"), ImageFormat.Png);
        }

        // Capture Vertical Tabs preview
        bool prevVert = ctx.Settings.UseVerticalTabs;
        main._verticalTabBar.Visible = true;
        main._tabBar.Visible = false;
        main.RenderTabs();
        Application.DoEvents();
        using (var bmp = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(bmp, new Rectangle(0, 0, main.Width, main.Height));
            bmp.Save(Path.Combine(outDir, "vertical_tabs.png"), ImageFormat.Png);
        }
        main._verticalTabBar.Visible = prevVert;
        main._tabBar.Visible = !prevVert;

        // Capture Split View preview
        main._splitPane.Width = 560;
        main._splitSplitter.Visible = true;
        main._splitPane.Visible = true;
        main._splitUrlBox.Text = "https://github.com/edizdar/ED-Browser";
        Application.DoEvents();
        using (var bmp = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(bmp, new Rectangle(0, 0, main.Width, main.Height));
            bmp.Save(Path.Combine(outDir, "split_view.png"), ImageFormat.Png);
        }
        main._splitPane.Visible = false;
        main._splitSplitter.Visible = false;

        // Capture Task Manager preview
        using (var tmDlg = new TaskManagerDialog(ThemeService.GetPalette(ctx.Settings.Theme, false), () => main._tabs.AsReadOnly(), _ => Task.CompletedTask, () => { }))
        {
            tmDlg.StartPosition = FormStartPosition.Manual;
            tmDlg.Location = new Point(-32000, -32000);
            tmDlg.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(tmDlg.Width, tmDlg.Height);
            tmDlg.DrawToBitmap(bmp, new Rectangle(0, 0, tmDlg.Width, tmDlg.Height));
            bmp.Save(Path.Combine(outDir, "task_manager.png"), ImageFormat.Png);
            tmDlg.Close();
        }

        var palette = ThemeService.GetPalette(ctx.Settings.Theme, false);

        // Capture AppMenuPopup preview
        var menuState = main.BuildAppMenuState(49);
        using (var popup = new AppMenuPopup(main, new AppMenuState
        {
            Version = menuState.Version,
            TotalRamMb = 49,
            IsDarkTheme = true,
            AdBlockEnabled = true,
            ShowBookmarkBar = ctx.Settings.ShowBookmarkBar,
            CanReopenClosedTab = true,
            HasNavigatedTab = true,
            ZoomPercent = 100
        }, palette, new Point(-31000, -31800), enableOutsideDismiss: false))
        {
            popup.Location = new Point(-32000, -32000);
            popup.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(bmp, new Rectangle(0, 0, popup.Width, popup.Height));
            bmp.Save(Path.Combine(outDir, "app_menu.png"), ImageFormat.Png);
            popup.SafeClose();
        }

        // Capture Docked DevTools preview
        main._devToolsPanel.Visible = true;
        main._devToolsSplitter.Visible = true;
        main.ApplyDevToolsDockMode(DevToolsDockMode.Right);
        main._devToolsPanel.PopulatePreviewSampleData();
        Application.DoEvents();
        using (var bmp = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(bmp, new Rectangle(0, 0, main.Width, main.Height));
            bmp.Save(Path.Combine(outDir, "devtools_docked.png"), ImageFormat.Png);
        }
        main.CloseDevToolsPanel();

        // Capture ClearBrowsingDataDialog preview
        using (var clrDlg = new ClearBrowsingDataDialog(palette))
        {
            clrDlg.StartPosition = FormStartPosition.Manual;
            clrDlg.Location = new Point(-32000, -32000);
            clrDlg.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(clrDlg.Width, clrDlg.Height);
            clrDlg.DrawToBitmap(bmp, new Rectangle(0, 0, clrDlg.Width, clrDlg.Height));
            bmp.Save(Path.Combine(outDir, "clear_data_dialog.png"), ImageFormat.Png);
            clrDlg.Close();
        }

        // Capture ExtensionsDialog preview
        using (var extDlg = new ExtensionsDialog(ctx, null, _u => { }, palette))
        {
            extDlg.StartPosition = FormStartPosition.Manual;
            extDlg.Location = new Point(-32000, -32000);
            extDlg.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(extDlg.Width, extDlg.Height);
            extDlg.DrawToBitmap(bmp, new Rectangle(0, 0, extDlg.Width, extDlg.Height));
            bmp.Save(Path.Combine(outDir, "extensions_dialog.png"), ImageFormat.Png);
            extDlg.Close();
        }

        // Capture VpnPrivacyPopup preview with verified sample servers & live connected badge
        bool prevVpn = ctx.Settings.VpnEnabled;
        var prevMode = ctx.Settings.ProxyMode;
        using (var vpnPop = new VpnPrivacyPopup(main, ctx, palette, new Point(-31800, -31800), () => { }, () => { }, _u => { }, enableOutsideDismiss: false))
        {
            vpnPop.Location = new Point(-32000, -32000);
            vpnPop.Show();
            vpnPop.PopulatePreviewSample();
            Application.DoEvents();
            using var bmp = new Bitmap(vpnPop.Width, vpnPop.Height);
            vpnPop.DrawToBitmap(bmp, new Rectangle(0, 0, vpnPop.Width, vpnPop.Height));
            bmp.Save(Path.Combine(outDir, "vpn_privacy_popup.png"), ImageFormat.Png);
            vpnPop.SafeClose();
        }
        ctx.Settings.VpnEnabled = prevVpn;
        ctx.Settings.ProxyMode = prevMode;

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
        if (addedSampleBm) ctx.Bookmarks.Clear();
        ctx.Settings.ShowBookmarkBar = prevBmBar;
        main.Close();
        ctx.Favicons.Dispose();
        ctx.VpnRouter.Dispose();
    }
}
