using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EDBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EDBrowser.UI;

public enum DevToolsDockMode
{
    Right,
    Bottom
}

/// <summary>
/// True Chrome-style docked Developer Tools powered by CoreWebView2 Chrome DevTools Protocol (CDP),
/// with Elements, Console + JS REPL, Network, Application Storage, Sources, Element Picker, and Device Emulation.
/// </summary>
public sealed class DevToolsPanel : Panel
{
    private enum ConsoleLevel { Log, Info, Warn, Error, Result }

    private sealed class ConsoleEntry
    {
        public string Time { get; init; } = "";
        public ConsoleLevel Level { get; init; }
        public string Text { get; init; } = "";
    }

    private sealed class NetworkItem
    {
        public string RequestId { get; init; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Method { get; set; } = "GET";
        public string Type { get; set; } = "Other";
        public string Status { get; set; } = "Bekliyor";
        public long SizeBytes { get; set; }
        public double StartWallTime { get; set; }
        public double ElapsedMs { get; set; }
        public string RequestHeaders { get; set; } = "";
        public string ResponseHeaders { get; set; } = "";
    }

    private sealed class DomNodeTag
    {
        public string Selector { get; init; } = "";
        public string Tag { get; init; } = "";
        public string OuterHtml { get; init; } = "";
    }

    private ThemePalette _p = ThemeService.Dark;
    private BrowserTab? _currentTab;
    private CoreWebView2? _currentCore;
    private readonly HashSet<CoreWebView2> _subscribedCores = new();

    private bool _pickerActive;
    private int _emulationMode; // 0=Desktop, 1=Mobile (390x844), 2=Tablet (768x1024)
    private string _selectedCssSelector = string.Empty;
    private int _errorCount;

    // Header chrome
    private readonly Panel _header;
    private readonly IconButton _pickerBtn;
    private readonly IconButton _deviceBtn;
    private readonly FlowLayoutPanel _tabStrip;
    private readonly Button[] _tabButtons;
    private readonly IconButton _dockRightBtn;
    private readonly IconButton _dockBottomBtn;
    private readonly IconButton _externalBtn;
    private readonly IconButton _closeBtn;

    // Body + Empty state
    private readonly Panel _bodyHost;
    private readonly Label _emptyLabel;

    // Tab 0: Elements
    private readonly Panel _elementsView;
    private readonly RoundedTextBox _domSearchBox;
    private readonly ModernButton _domRefreshBtn;
    private readonly Splitter _domSplitter;
    private readonly TreeView _domTree;
    private readonly Label _selSelectorLbl;
    private readonly ListView _stylesList;
    private readonly RoundedTextBox _inlineStyleBox;
    private readonly ModernButton _applyStyleBtn;

    // Tab 1: Console
    private readonly Panel _consoleView;
    private readonly List<ConsoleEntry> _consoleEntries = new();
    private readonly List<ConsoleEntry> _filteredConsole = new();
    private readonly ListBox _consoleList;
    private readonly RoundedTextBox _consoleFilterBox;
    private readonly RoundedTextBox _replInput;
    private readonly ModernButton _replRunBtn;
    private readonly List<string> _replHistory = new();
    private int _replHistIdx = -1;
    private string _consoleLevelFilter = "ALL";
    private readonly Button[] _consoleFilterBtns;

    // Tab 2: Network
    private readonly Panel _networkView;
    private readonly List<NetworkItem> _networkItems = new();
    private readonly Dictionary<string, NetworkItem> _networkMap = new();
    private readonly ListView _netList;
    private readonly TextBox _netDetailBox;
    private readonly CheckBox _cacheDisabledCheck;
    private string _netTypeFilter = "ALL";
    private readonly Button[] _netFilterBtns;

    // Tab 3: Application
    private readonly Panel _appView;
    private readonly ListView _storageList;
    private string _storageMode = "cookies";
    private readonly Button[] _storageModeBtns;

    // Tab 4: Sources
    private readonly Panel _sourcesView;
    private readonly ListBox _sourceFilesList;
    private readonly TextBox _sourceViewer;
    private readonly List<(string Label, string Url, bool IsDocument)> _sourceEntries = new();

    public DevToolsDockMode DockMode { get; set; } = DevToolsDockMode.Right;
    public int ActiveTabIndex { get; private set; }

    public event Action<DevToolsDockMode>? DockModeChanged;
    public event Action? OpenExternalDevToolsRequested;
    public event Action? CloseRequested;

    public DevToolsPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // ── Header Bar ────────────────────────────────────────────────────────────
        _header = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 4) };
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(_p.Border, 1f);
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };

        var leftTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 1, 4, 0)
        };

        _pickerBtn = new IconButton
        {
            IconType = VectorIcon.InspectCursor,
            Size = new Size(28, 28),
            IconSizePx = 14,
            CornerRadius = 6,
            Margin = new Padding(0, 0, 2, 0)
        };
        _pickerBtn.SetTooltip("Sayfadan Öğe Seç (Ctrl+Shift+C)");
        _pickerBtn.Click += async (_, _) => await ToggleElementPickerAsync();

        _deviceBtn = new IconButton
        {
            IconType = VectorIcon.DeviceMobile,
            Size = new Size(28, 28),
            IconSizePx = 14,
            CornerRadius = 6,
            Margin = new Padding(0, 0, 6, 0)
        };
        _deviceBtn.SetTooltip("Cihaz Görünümü (Masaüstü / Mobil / Tablet)");
        _deviceBtn.Click += async (_, _) => await CycleDeviceEmulationAsync();

        leftTools.Controls.Add(_pickerBtn);
        leftTools.Controls.Add(_deviceBtn);

        _tabStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(2, 2, 2, 0)
        };

        string[] tabNames = { "Öğeler", "Konsol", "Ağ", "Depo", "Kaynak" };
        _tabButtons = new Button[tabNames.Length];
        for (int i = 0; i < tabNames.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = tabNames[i],
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = 28,
                Padding = new Padding(5, 0, 5, 0),
                Margin = new Padding(0, 0, 2, 0),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, _) => SelectTab(idx);
            _tabButtons[i] = btn;
            _tabStrip.Controls.Add(btn);
        }

        var rightControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(4, 1, 0, 0)
        };

        _dockRightBtn = MakeHeaderBtn(VectorIcon.DockRight, "Sağa Sabitle", (_, _) =>
        {
            DockMode = DevToolsDockMode.Right;
            UpdateDockButtons();
            DockModeChanged?.Invoke(DevToolsDockMode.Right);
        });
        _dockBottomBtn = MakeHeaderBtn(VectorIcon.DockBottom, "Alta Sabitle", (_, _) =>
        {
            DockMode = DevToolsDockMode.Bottom;
            UpdateDockButtons();
            DockModeChanged?.Invoke(DevToolsDockMode.Bottom);
        });
        _externalBtn = MakeHeaderBtn(VectorIcon.ExternalWindow, "Tam Chromium DevTools (Ayrı Pencere)", (_, _) =>
            OpenExternalDevToolsRequested?.Invoke());
        _closeBtn = MakeHeaderBtn(VectorIcon.Close, "Geliştirici Araçlarını Kapat (F12)", (_, _) =>
            CloseRequested?.Invoke());

        rightControls.Controls.AddRange(new Control[] { _dockRightBtn, _dockBottomBtn, _externalBtn, _closeBtn });

        _header.Controls.Add(_tabStrip);
        _header.Controls.Add(leftTools);
        _header.Controls.Add(rightControls);

        // ── Body Host ─────────────────────────────────────────────────────────────
        _bodyHost = new Panel { Dock = DockStyle.Fill };

        _emptyLabel = new Label
        {
            Text = "Geliştirici araçlarını kullanmak için önce bir web sayfası açın.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5f),
            UseMnemonic = false,
            Visible = false
        };

        // ── Tab 0: Elements ───────────────────────────────────────────────────────
        _elementsView = new Panel { Dock = DockStyle.Fill };
        var domTop = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 5, 8, 5) };
        _domSearchBox = new RoundedTextBox { Dock = DockStyle.Fill, PlaceholderText = "DOM içinde seçici veya etiket ara (Enter)…", Height = 30 };
        _domSearchBox.Inner.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SearchAndSelectDomAsync(_domSearchBox.Text.Trim());
            }
        };
        _domRefreshBtn = new ModernButton { Text = "Yenile", Dock = DockStyle.Right, Width = 78, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        _domRefreshBtn.Click += async (_, _) => await RefreshDomTreeAsync();
        domTop.Controls.Add(_domSearchBox);
        domTop.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        domTop.Controls.Add(_domRefreshBtn);

        _domSplitter = new Splitter { Dock = DockStyle.Bottom, Height = 4, MinExtra = 100, MinSize = 100 };
        _domTree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
            ShowLines = true,
            FullRowSelect = true,
            HideSelection = false
        };
        _domTree.AfterSelect += async (_, e) =>
        {
            if (e.Node?.Tag is DomNodeTag tag)
                await LoadComputedStylesAsync(tag.Selector);
        };

        var domCtx = new ContextMenuStrip { ShowImageMargin = false, Padding = new Padding(4) };
        domCtx.Items.Add("CSS Seçiciyi Kopyala", null, (_, _) =>
        {
            if (_domTree.SelectedNode?.Tag is DomNodeTag tag && !string.IsNullOrEmpty(tag.Selector))
                Clipboard.SetText(tag.Selector);
        });
        domCtx.Items.Add("OuterHTML Kopyala", null, async (_, _) =>
        {
            if (_domTree.SelectedNode?.Tag is DomNodeTag tag)
                await CopyOuterHtmlAsync(tag.Selector);
        });
        domCtx.Items.Add("Sayfada Vurgula", null, async (_, _) =>
        {
            if (_domTree.SelectedNode?.Tag is DomNodeTag tag)
                await HighlightSelectorInPageAsync(tag.Selector);
        });
        domCtx.Items.Add("Öğeyi Sil", null, async (_, _) =>
        {
            if (_domTree.SelectedNode?.Tag is DomNodeTag tag)
            {
                await ExecuteDomDeleteAsync(tag.Selector);
                await RefreshDomTreeAsync();
            }
        });
        _domTree.ContextMenuStrip = domCtx;
        _domTree.NodeMouseClick += (_, e) => { if (e.Node is not null) _domTree.SelectedNode = e.Node; };

        var styleHost = new Panel { Dock = DockStyle.Bottom, Height = 230, Padding = new Padding(8, 6, 8, 6) };
        _selSelectorLbl = new Label
        {
            Text = "Seçili öğe yok",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Consolas", 8.5f, FontStyle.Bold),
            AutoEllipsis = true,
            UseMnemonic = false
        };

        var inlineEditor = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
        _inlineStyleBox = new RoundedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Satır içi stil (ör: color: #38bdf8; border: 1px solid red)", Height = 30 };
        _applyStyleBtn = new ModernButton { Text = "Uygula", Primary = true, Dock = DockStyle.Right, Width = 78, Height = 30 };
        _applyStyleBtn.Click += async (_, _) => await ApplyInlineStyleAsync();
        _inlineStyleBox.Inner.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyInlineStyleAsync(); }
        };
        inlineEditor.Controls.Add(_inlineStyleBox);
        inlineEditor.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        inlineEditor.Controls.Add(_applyStyleBtn);

        _stylesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.75f)
        };
        _stylesList.Columns.Add("Özellik", 160);
        _stylesList.Columns.Add("Değer", 240);

        styleHost.Controls.Add(_stylesList);
        styleHost.Controls.Add(_selSelectorLbl);
        styleHost.Controls.Add(inlineEditor);

        _elementsView.Controls.Add(_domTree);
        _elementsView.Controls.Add(_domSplitter);
        _elementsView.Controls.Add(styleHost);
        _elementsView.Controls.Add(domTop);

        // ── Tab 1: Console ────────────────────────────────────────────────────────
        _consoleView = new Panel { Dock = DockStyle.Fill };
        var conTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 5)
        };
        var conClearBtn = MakeMiniBtn("Temizle", (_, _) =>
        {
            _consoleEntries.Clear();
            _errorCount = 0;
            UpdateTabStripAppearance();
            RebuildConsoleList();
        });
        conTop.Controls.Add(conClearBtn);

        (string Key, string Label)[] conFilters = { ("ALL", "Tümü"), ("ERR", "Hata"), ("WRN", "Uyarı"), ("LOG", "Bilgi") };
        _consoleFilterBtns = new Button[conFilters.Length];
        for (int i = 0; i < conFilters.Length; i++)
        {
            string k = conFilters[i].Key;
            var fb = MakeMiniBtn(conFilters[i].Label, (_, _) =>
            {
                _consoleLevelFilter = k;
                RebuildConsoleList();
                StyleFilterGroup(_consoleFilterBtns, k);
            });
            fb.Tag = k;
            _consoleFilterBtns[i] = fb;
            conTop.Controls.Add(fb);
        }

        _consoleFilterBox = new RoundedTextBox { Width = 160, Height = 28, PlaceholderText = "Filtrele…", Margin = new Padding(6, 0, 0, 0) };
        _consoleFilterBox.Inner.TextChanged += (_, _) => RebuildConsoleList();
        conTop.Controls.Add(_consoleFilterBox);

        _consoleList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 26,
            Font = new Font("Consolas", 8.75f)
        };
        _consoleList.DrawItem += DrawConsoleItem;
        _consoleList.DoubleClick += (_, _) =>
        {
            if (_consoleList.SelectedIndex >= 0 && _consoleList.SelectedIndex < _filteredConsole.Count)
                Clipboard.SetText(_filteredConsole[_consoleList.SelectedIndex].Text);
        };

        var replBar = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 5, 8, 5) };
        var promptLbl = new Label
        {
            Text = ">",
            Dock = DockStyle.Left,
            Width = 18,
            Font = new Font("Consolas", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        _replInput = new RoundedTextBox { Dock = DockStyle.Fill, PlaceholderText = "JavaScript ifadesi çalıştır (Enter)…", Height = 30 };
        _replInput.Inner.Font = new Font("Consolas", 9.25f);
        _replRunBtn = new ModernButton { Text = "Çalıştır", Primary = true, Dock = DockStyle.Right, Width = 84, Height = 30 };
        _replRunBtn.Click += async (_, _) => await EvaluateReplAsync();
        _replInput.Inner.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await EvaluateReplAsync();
            }
            else if (e.KeyCode == Keys.Up && _replHistory.Count > 0)
            {
                e.SuppressKeyPress = true;
                _replHistIdx = Math.Clamp(_replHistIdx < 0 ? _replHistory.Count - 1 : _replHistIdx - 1, 0, _replHistory.Count - 1);
                _replInput.Text = _replHistory[_replHistIdx];
                _replInput.Inner.SelectionStart = _replInput.Text.Length;
            }
            else if (e.KeyCode == Keys.Down && _replHistory.Count > 0 && _replHistIdx >= 0)
            {
                e.SuppressKeyPress = true;
                _replHistIdx++;
                if (_replHistIdx >= _replHistory.Count)
                {
                    _replHistIdx = _replHistory.Count;
                    _replInput.Text = "";
                }
                else
                {
                    _replInput.Text = _replHistory[_replHistIdx];
                    _replInput.Inner.SelectionStart = _replInput.Text.Length;
                }
            }
        };
        replBar.Controls.Add(_replInput);
        replBar.Controls.Add(promptLbl);
        replBar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
        replBar.Controls.Add(_replRunBtn);

        _consoleView.Controls.Add(_consoleList);
        _consoleView.Controls.Add(conTop);
        _consoleView.Controls.Add(replBar);

        // ── Tab 2: Network ────────────────────────────────────────────────────────
        _networkView = new Panel { Dock = DockStyle.Fill };
        var netTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 5)
        };
        netTop.Controls.Add(MakeMiniBtn("Temizle", (_, _) =>
        {
            _networkItems.Clear();
            _networkMap.Clear();
            RebuildNetworkList();
            _netDetailBox.Clear();
        }));

        (string Key, string Label)[] netFilters = { ("ALL", "Tümü"), ("XHR", "XHR/Fetch"), ("Script", "JS"), ("Stylesheet", "CSS"), ("Image", "Görsel") };
        _netFilterBtns = new Button[netFilters.Length];
        for (int i = 0; i < netFilters.Length; i++)
        {
            string k = netFilters[i].Key;
            var fb = MakeMiniBtn(netFilters[i].Label, (_, _) =>
            {
                _netTypeFilter = k;
                RebuildNetworkList();
                StyleFilterGroup(_netFilterBtns, k);
            });
            fb.Tag = k;
            _netFilterBtns[i] = fb;
            netTop.Controls.Add(fb);
        }

        _cacheDisabledCheck = new CheckBox
        {
            Text = "Önbellek Kapalı",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(8, 5, 0, 0),
            UseMnemonic = false
        };
        _cacheDisabledCheck.CheckedChanged += async (_, _) =>
        {
            if (_currentCore is not null)
            {
                try { await _currentCore.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", JsonSerializer.Serialize(new { cacheDisabled = _cacheDisabledCheck.Checked })); }
                catch { }
            }
        };
        netTop.Controls.Add(_cacheDisabledCheck);

        var netBottom = new Panel { Dock = DockStyle.Bottom, Height = 190 };
        var netSplitter = new Splitter { Dock = DockStyle.Bottom, Height = 4, BackColor = _p.Border };
        _netList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 8.75f)
        };
        _netList.Columns.Add("Ad", 170);
        _netList.Columns.Add("Durum", 64);
        _netList.Columns.Add("Metot", 58);
        _netList.Columns.Add("Tür", 72);
        _netList.Columns.Add("Boyut", 68);
        _netList.Columns.Add("Süre", 64);
        _netList.SelectedIndexChanged += async (_, _) => await ShowSelectedNetworkDetailsAsync();

        _netDetailBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.75f)
        };
        netBottom.Controls.Add(_netDetailBox);
        _networkView.Controls.Add(_netList);
        _networkView.Controls.Add(netSplitter);
        _networkView.Controls.Add(netBottom);
        _networkView.Controls.Add(netTop);

        // ── Tab 3: Application / Storage ──────────────────────────────────────────
        _appView = new Panel { Dock = DockStyle.Fill };
        var appTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 5)
        };
        (string Key, string Label)[] stModes = { ("cookies", "Çerezler"), ("local", "LocalStorage"), ("session", "SessionStorage") };
        _storageModeBtns = new Button[stModes.Length];
        for (int i = 0; i < stModes.Length; i++)
        {
            string k = stModes[i].Key;
            var sb = MakeMiniBtn(stModes[i].Label, async (_, _) =>
            {
                _storageMode = k;
                StyleFilterGroup(_storageModeBtns, k);
                await RefreshStorageAsync();
            });
            sb.Tag = k;
            _storageModeBtns[i] = sb;
            appTop.Controls.Add(sb);
        }
        appTop.Controls.Add(MakeMiniBtn("Yenile", async (_, _) => await RefreshStorageAsync()));
        appTop.Controls.Add(MakeMiniBtn("Seçiliyi Sil", async (_, _) => await DeleteSelectedStorageItemAsync()));

        _storageList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.75f)
        };
        _storageList.Columns.Add("Ad", 150);
        _storageList.Columns.Add("Değer", 220);
        _storageList.Columns.Add("Alan / Bilgi", 120);
        _appView.Controls.Add(_storageList);
        _appView.Controls.Add(appTop);

        // ── Tab 4: Sources ────────────────────────────────────────────────────────
        _sourcesView = new Panel { Dock = DockStyle.Fill };
        var srcTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 5)
        };
        srcTop.Controls.Add(MakeMiniBtn("Listeyi Yenile", async (_, _) => await RefreshSourcesListAsync()));
        srcTop.Controls.Add(MakeMiniBtn("Kaynağı Kopyala", (_, _) =>
        {
            if (!string.IsNullOrEmpty(_sourceViewer.Text))
                Clipboard.SetText(_sourceViewer.Text);
        }));

        var srcTopFiles = new Panel { Dock = DockStyle.Top, Height = 150 };
        var srcSplitter = new Splitter { Dock = DockStyle.Top, Height = 4, BackColor = _p.Border };
        _sourceFilesList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 8.75f),
            ItemHeight = 22
        };
        _sourceFilesList.SelectedIndexChanged += async (_, _) => await LoadSelectedSourceAsync();

        _sourceViewer = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.75f)
        };
        srcTopFiles.Controls.Add(_sourceFilesList);
        _sourcesView.Controls.Add(_sourceViewer);
        _sourcesView.Controls.Add(srcSplitter);
        _sourcesView.Controls.Add(srcTopFiles);
        _sourcesView.Controls.Add(srcTop);

        _bodyHost.Controls.Add(_elementsView);
        _bodyHost.Controls.Add(_consoleView);
        _bodyHost.Controls.Add(_networkView);
        _bodyHost.Controls.Add(_appView);
        _bodyHost.Controls.Add(_sourcesView);
        _bodyHost.Controls.Add(_emptyLabel);

        Controls.Add(_bodyHost);
        Controls.Add(_header);

        SelectTab(0);
        UpdateDockButtons();
        ApplyTheme(_p);
    }

    private IconButton MakeHeaderBtn(VectorIcon icon, string tip, EventHandler onClick)
    {
        var b = new IconButton
        {
            IconType = icon,
            Size = new Size(28, 28),
            IconSizePx = 14,
            CornerRadius = 6,
            Margin = new Padding(2, 0, 0, 0)
        };
        b.SetTooltip(tip);
        b.Click += onClick;
        return b;
    }

    private Button MakeMiniBtn(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 26,
            Padding = new Padding(7, 0, 7, 0),
            Margin = new Padding(0, 0, 4, 0),
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };
        b.FlatAppearance.BorderSize = 1;
        b.Click += onClick;
        return b;
    }

    public void SelectTab(int index)
    {
        ActiveTabIndex = Math.Clamp(index, 0, 4);
        _elementsView.Visible = ActiveTabIndex == 0;
        _consoleView.Visible = ActiveTabIndex == 1;
        _networkView.Visible = ActiveTabIndex == 2;
        _appView.Visible = ActiveTabIndex == 3;
        _sourcesView.Visible = ActiveTabIndex == 4;
        UpdateTabStripAppearance();

        if (Visible && _currentCore is not null)
        {
            if (ActiveTabIndex == 0 && _domTree.Nodes.Count == 0) _ = RefreshDomTreeAsync();
            else if (ActiveTabIndex == 3) _ = RefreshStorageAsync();
            else if (ActiveTabIndex == 4 && _sourceEntries.Count == 0) _ = RefreshSourcesListAsync();
        }
    }

    public async Task AttachToTabAsync(BrowserTab? tab)
    {
        _currentTab = tab;
        _currentCore = tab?.WebView?.CoreWebView2;

        if (_currentTab is null || !_currentTab.HasNavigated || _currentCore is null)
        {
            _emptyLabel.Visible = true;
            _emptyLabel.BringToFront();
            return;
        }

        _emptyLabel.Visible = false;
        await EnsureCoreSubscribedAsync(_currentCore);

        if (Visible)
        {
            if (ActiveTabIndex == 0) await RefreshDomTreeAsync();
            else if (ActiveTabIndex == 3) await RefreshStorageAsync();
            else if (ActiveTabIndex == 4) await RefreshSourcesListAsync();
        }
    }

    public async Task InspectAtPointAsync(int clientX, int clientY)
    {
        if (_currentCore is null) return;
        SelectTab(0);
        try
        {
            var js = $@"(function() {{
                var x = {clientX} / (window.devicePixelRatio || 1);
                var y = {clientY} / (window.devicePixelRatio || 1);
                var el = document.elementFromPoint(x, y) || document.elementFromPoint({clientX}, {clientY});
                if (!el) return '';
                function cssPath(node) {{
                    if (!node || node.nodeType !== 1) return '';
                    if (node.id) return '#' + CSS.escape(node.id);
                    var parts = [];
                    while (node && node.nodeType === 1) {{
                        var tag = node.tagName.toLowerCase();
                        var sib = node, nth = 1;
                        while ((sib = sib.previousElementSibling)) {{ if (sib.tagName === node.tagName) nth++; }}
                        parts.unshift(nth > 1 ? tag + ':nth-of-type(' + nth + ')' : tag);
                        if (tag === 'body' || tag === 'html') break;
                        node = node.parentElement;
                    }}
                    return parts.join(' > ');
                }}
                return cssPath(el);
            }})();";
            var res = await _currentCore.ExecuteScriptAsync(js);
            var selector = JsonSerializer.Deserialize<string>(res) ?? "";
            await RefreshDomTreeAsync(selector);
            if (!string.IsNullOrEmpty(selector))
            {
                await LoadComputedStylesAsync(selector);
                await HighlightSelectorInPageAsync(selector);
            }
        }
        catch { }
    }

    public async Task ToggleElementPickerAsync()
    {
        if (_currentCore is null) return;
        _pickerActive = !_pickerActive;
        _pickerBtn.IsActive = _pickerActive;
        _pickerBtn.IconColor = _pickerActive ? _p.Accent : _p.TextSecondary;
        _pickerBtn.Invalidate();

        if (_pickerActive)
        {
            var script = @"(function(){
                if (window.__edPickerCleanup) window.__edPickerCleanup();
                var ov = document.createElement('div');
                ov.id = '__ed_picker_overlay';
                ov.style.cssText = 'position:fixed;pointer-events:none;z-index:2147483647;border:2px solid #38bdf8;background:rgba(56,189,248,0.16);border-radius:3px;transition:all 0.04s;';
                document.documentElement.appendChild(ov);
                function cssPath(node) {
                    if (!node || node.nodeType !== 1) return '';
                    if (node.id) return '#' + CSS.escape(node.id);
                    var parts = [];
                    while (node && node.nodeType === 1) {
                        var tag = node.tagName.toLowerCase();
                        var sib = node, nth = 1;
                        while ((sib = sib.previousElementSibling)) { if (sib.tagName === node.tagName) nth++; }
                        parts.unshift(nth > 1 ? tag + ':nth-of-type(' + nth + ')' : tag);
                        if (tag === 'body' || tag === 'html') break;
                        node = node.parentElement;
                    }
                    return parts.join(' > ');
                }
                function onMove(e) {
                    var el = document.elementFromPoint(e.clientX, e.clientY);
                    if (!el || el === ov) return;
                    var r = el.getBoundingClientRect();
                    ov.style.left = r.left + 'px'; ov.style.top = r.top + 'px';
                    ov.style.width = r.width + 'px'; ov.style.height = r.height + 'px';
                }
                function onClick(e) {
                    e.preventDefault(); e.stopPropagation();
                    var el = document.elementFromPoint(e.clientX, e.clientY);
                    cleanup();
                    if (el && window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'ed-devtools-pick',
                            selector: cssPath(el),
                            tag: el.tagName.toLowerCase()
                        }));
                    }
                }
                function cleanup() {
                    document.removeEventListener('mousemove', onMove, true);
                    document.removeEventListener('click', onClick, true);
                    if (ov && ov.parentNode) ov.parentNode.removeChild(ov);
                    window.__edPickerCleanup = null;
                }
                window.__edPickerCleanup = cleanup;
                document.addEventListener('mousemove', onMove, true);
                document.addEventListener('click', onClick, true);
            })();";
            try { await _currentCore.ExecuteScriptAsync(script); } catch { }
        }
        else
        {
            try { await _currentCore.ExecuteScriptAsync("if (window.__edPickerCleanup) window.__edPickerCleanup();"); } catch { }
        }
    }

    public async Task HandlePickedElementJsonAsync(string json)
    {
        _pickerActive = false;
        _pickerBtn.IsActive = false;
        _pickerBtn.IconColor = _p.TextSecondary;
        _pickerBtn.Invalidate();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var selector = doc.RootElement.TryGetProperty("selector", out var s) ? s.GetString() ?? "" : "";
            SelectTab(0);
            await RefreshDomTreeAsync(selector);
            if (!string.IsNullOrEmpty(selector))
                await LoadComputedStylesAsync(selector);
        }
        catch { }
    }

    private async Task CycleDeviceEmulationAsync()
    {
        if (_currentCore is null) return;
        _emulationMode = (_emulationMode + 1) % 3;
        try
        {
            if (_emulationMode == 0)
            {
                await _currentCore.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
                _deviceBtn.IsActive = false;
                _deviceBtn.IconColor = _p.TextSecondary;
                _deviceBtn.SetTooltip("Cihaz Görünümü: Masaüstü");
            }
            else if (_emulationMode == 1)
            {
                await _currentCore.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    JsonSerializer.Serialize(new { width = 390, height = 844, deviceScaleFactor = 3, mobile = true }));
                _deviceBtn.IsActive = true;
                _deviceBtn.IconColor = _p.Accent;
                _deviceBtn.SetTooltip("Cihaz Görünümü: Mobil (390×844)");
            }
            else
            {
                await _currentCore.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    JsonSerializer.Serialize(new { width = 768, height = 1024, deviceScaleFactor = 2, mobile = true }));
                _deviceBtn.IsActive = true;
                _deviceBtn.IconColor = _p.AccentPurple;
                _deviceBtn.SetTooltip("Cihaz Görünümü: Tablet (768×1024)");
            }
            _deviceBtn.Invalidate();
        }
        catch { }
    }

    private async Task EnsureCoreSubscribedAsync(CoreWebView2 core)
    {
        if (!_subscribedCores.Add(core))
            return;

        try
        {
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            await core.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");

            core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (core != _currentCore) return;
                BeginInvoke(() => OnConsoleApiCalled(e.ParameterObjectAsJson));
            };
            core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (core != _currentCore) return;
                BeginInvoke(() => OnExceptionThrown(e.ParameterObjectAsJson));
            };
            core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (core != _currentCore) return;
                BeginInvoke(() => OnNetworkRequest(e.ParameterObjectAsJson));
            };
            core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (core != _currentCore) return;
                BeginInvoke(() => OnNetworkResponse(e.ParameterObjectAsJson));
            };
            core.GetDevToolsProtocolEventReceiver("Network.loadingFinished").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (core != _currentCore) return;
                BeginInvoke(() => OnNetworkFinished(e.ParameterObjectAsJson));
            };
            core.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (core != _currentCore) return;
                BeginInvoke(() => OnNetworkFailed(e.ParameterObjectAsJson));
            };
            core.NavigationCompleted += (_, _) =>
            {
                if (core != _currentCore || !Visible) return;
                BeginInvoke(async () =>
                {
                    if (ActiveTabIndex == 0) await RefreshDomTreeAsync();
                    else if (ActiveTabIndex == 3) await RefreshStorageAsync();
                    else if (ActiveTabIndex == 4) await RefreshSourcesListAsync();
                });
            };
        }
        catch { }
    }

    // ── DOM & STYLES ────────────────────────────────────────────────────────────
    public async Task RefreshDomTreeAsync(string? selectSelector = null)
    {
        if (_currentCore is null) return;
        try
        {
            var script = @"(function(){
                function cssPath(node) {
                    if (!node || node.nodeType !== 1) return '';
                    if (node.id) return '#' + CSS.escape(node.id);
                    var parts = [];
                    while (node && node.nodeType === 1) {
                        var tag = node.tagName.toLowerCase();
                        var sib = node, nth = 1;
                        while ((sib = sib.previousElementSibling)) { if (sib.tagName === node.tagName) nth++; }
                        parts.unshift(nth > 1 ? tag + ':nth-of-type(' + nth + ')' : tag);
                        if (tag === 'body' || tag === 'html') break;
                        node = node.parentElement;
                    }
                    return parts.join(' > ');
                }
                function walk(el, depth) {
                    if (!el || depth > 6) return null;
                    var tag = el.tagName ? el.tagName.toLowerCase() : '';
                    var label = '<' + tag;
                    if (el.id) label += '#' + el.id;
                    if (el.className && typeof el.className === 'string') {
                        var cls = el.className.trim().split(/\s+/).slice(0, 3).join('.');
                        if (cls) label += '.' + cls;
                    }
                    label += '>';
                    var txt = '';
                    for (var i = 0; i < el.childNodes.length; i++) {
                        if (el.childNodes[i].nodeType === 3) {
                            var t = (el.childNodes[i].nodeValue || '').trim();
                            if (t) { txt = t.slice(0, 40); break; }
                        }
                    }
                    if (txt && el.children.length === 0) label += ' ' + txt + ' </' + tag + '>';
                    var children = [];
                    var maxC = Math.min(el.children.length, 45);
                    for (var c = 0; c < maxC; c++) {
                        var child = walk(el.children[c], depth + 1);
                        if (child) children.push(child);
                    }
                    return { label: label, selector: cssPath(el), tag: tag, children: children };
                }
                return walk(document.documentElement, 0);
            })();";
            var json = await _currentCore.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null") return;
            using var doc = JsonDocument.Parse(json);

            _domTree.BeginUpdate();
            _domTree.Nodes.Clear();
            TreeNode? targetNode = null;
            TreeNode BuildNode(JsonElement el)
            {
                var label = el.GetProperty("label").GetString() ?? "<node>";
                var sel = el.GetProperty("selector").GetString() ?? "";
                var tag = el.GetProperty("tag").GetString() ?? "";
                var tn = new TreeNode(label) { Tag = new DomNodeTag { Selector = sel, Tag = tag } };
                if (!string.IsNullOrEmpty(selectSelector) && sel == selectSelector)
                    targetNode = tn;
                if (el.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in ch.EnumerateArray())
                        tn.Nodes.Add(BuildNode(item));
                }
                return tn;
            }
            var root = BuildNode(doc.RootElement);
            _domTree.Nodes.Add(root);
            root.Expand();
            if (root.Nodes.Count > 0) root.Nodes[root.Nodes.Count - 1].Expand();
            if (targetNode is not null)
            {
                _domTree.SelectedNode = targetNode;
                targetNode.EnsureVisible();
            }
            _domTree.EndUpdate();
        }
        catch { }
    }

    private async Task SearchAndSelectDomAsync(string query)
    {
        if (_currentCore is null || string.IsNullOrEmpty(query)) return;
        try
        {
            var qJson = JsonSerializer.Serialize(query);
            var res = await _currentCore.ExecuteScriptAsync($@"(function(){{
                var q = {qJson};
                var el = null;
                try {{ el = document.querySelector(q); }} catch(_) {{}}
                if (!el) {{
                    var all = document.querySelectorAll('*');
                    for (var i = 0; i < all.length; i++) {{
                        if ((all[i].textContent || '').toLowerCase().includes(q.toLowerCase())) {{ el = all[i]; break; }}
                    }}
                }}
                if (!el) return '';
                function cssPath(node) {{
                    if (node.id) return '#' + CSS.escape(node.id);
                    var parts = [];
                    while (node && node.nodeType === 1) {{
                        var tag = node.tagName.toLowerCase();
                        var sib = node, nth = 1;
                        while ((sib = sib.previousElementSibling)) {{ if (sib.tagName === node.tagName) nth++; }}
                        parts.unshift(nth > 1 ? tag + ':nth-of-type(' + nth + ')' : tag);
                        if (tag === 'body' || tag === 'html') break;
                        node = node.parentElement;
                    }}
                    return parts.join(' > ');
                }}
                return cssPath(el);
            }})();");
            var sel = JsonSerializer.Deserialize<string>(res) ?? "";
            if (!string.IsNullOrEmpty(sel))
            {
                await RefreshDomTreeAsync(sel);
                await LoadComputedStylesAsync(sel);
                await HighlightSelectorInPageAsync(sel);
            }
        }
        catch { }
    }

    private async Task LoadComputedStylesAsync(string selector)
    {
        _selectedCssSelector = selector;
        _selSelectorLbl.Text = string.IsNullOrEmpty(selector) ? "Seçili öğe yok" : selector;
        if (_currentCore is null || string.IsNullOrEmpty(selector)) return;

        try
        {
            var sJson = JsonSerializer.Serialize(selector);
            var json = await _currentCore.ExecuteScriptAsync($@"(function(){{
                var el = document.querySelector({sJson});
                if (!el) return null;
                var cs = getComputedStyle(el);
                var props = ['display','position','width','height','margin','padding','color','background-color','font-family','font-size','border','z-index'];
                var out = [];
                for (var i = 0; i < props.length; i++) out.push([props[i], cs.getPropertyValue(props[i])]);
                return {{ inlineStyle: el.getAttribute('style') || '', computed: out }};
            }})();");
            if (string.IsNullOrWhiteSpace(json) || json == "null") return;
            using var doc = JsonDocument.Parse(json);
            _inlineStyleBox.Text = doc.RootElement.GetProperty("inlineStyle").GetString() ?? "";
            _stylesList.BeginUpdate();
            _stylesList.Items.Clear();
            foreach (var row in doc.RootElement.GetProperty("computed").EnumerateArray())
            {
                var k = row[0].GetString() ?? "";
                var v = row[1].GetString() ?? "";
                _stylesList.Items.Add(new ListViewItem(new[] { k, v }));
            }
            _stylesList.EndUpdate();
        }
        catch { }
    }

    private async Task ApplyInlineStyleAsync()
    {
        if (_currentCore is null || string.IsNullOrEmpty(_selectedCssSelector)) return;
        var sJson = JsonSerializer.Serialize(_selectedCssSelector);
        var valJson = JsonSerializer.Serialize(_inlineStyleBox.Text.Trim());
        try
        {
            await _currentCore.ExecuteScriptAsync($"(function(){{ var e=document.querySelector({sJson}); if(e) e.setAttribute('style', {valJson}); }})();");
            await LoadComputedStylesAsync(_selectedCssSelector);
        }
        catch { }
    }

    private async Task CopyOuterHtmlAsync(string selector)
    {
        if (_currentCore is null || string.IsNullOrEmpty(selector)) return;
        try
        {
            var res = await _currentCore.ExecuteScriptAsync($"document.querySelector({JsonSerializer.Serialize(selector)})?.outerHTML || ''");
            var html = JsonSerializer.Deserialize<string>(res) ?? "";
            if (!string.IsNullOrEmpty(html)) Clipboard.SetText(html);
        }
        catch { }
    }

    private async Task HighlightSelectorInPageAsync(string selector)
    {
        if (_currentCore is null || string.IsNullOrEmpty(selector)) return;
        try
        {
            await _currentCore.ExecuteScriptAsync($@"(function(){{
                var el = document.querySelector({JsonSerializer.Serialize(selector)});
                if (!el) return;
                el.scrollIntoView({{ block: 'center', behavior: 'smooth' }});
                var old = el.style.outline;
                el.style.outline = '2px solid #38bdf8';
                setTimeout(function(){{ el.style.outline = old; }}, 1400);
            }})();");
        }
        catch { }
    }

    private async Task ExecuteDomDeleteAsync(string selector)
    {
        if (_currentCore is null || string.IsNullOrEmpty(selector)) return;
        try { await _currentCore.ExecuteScriptAsync($"document.querySelector({JsonSerializer.Serialize(selector)})?.remove();"); }
        catch { }
    }

    // ── CONSOLE & JS REPL ──────────────────────────────────────────────────────
    private void OnConsoleApiCalled(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var typeStr = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "log" : "log";
            var lvl = typeStr switch
            {
                "error" or "assert" => ConsoleLevel.Error,
                "warning" or "warn" => ConsoleLevel.Warn,
                "info" => ConsoleLevel.Info,
                _ => ConsoleLevel.Log
            };
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in args.EnumerateArray())
                {
                    if (sb.Length > 0) sb.Append(' ');
                    if (a.TryGetProperty("value", out var v)) sb.Append(v.ToString());
                    else if (a.TryGetProperty("description", out var d)) sb.Append(d.GetString());
                    else if (a.TryGetProperty("type", out var tp)) sb.Append($"[{tp.GetString()}]");
                }
            }
            AddConsoleEntry(lvl, sb.ToString());
        }
        catch { }
    }

    private void OnExceptionThrown(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            string msg = "Uncaught Exception";
            if (doc.RootElement.TryGetProperty("exceptionDetails", out var det))
            {
                if (det.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var desc))
                    msg = desc.GetString() ?? msg;
                else if (det.TryGetProperty("text", out var txt))
                    msg = txt.GetString() ?? msg;
            }
            AddConsoleEntry(ConsoleLevel.Error, msg);
        }
        catch { }
    }

    private void AddConsoleEntry(ConsoleLevel level, string text)
    {
        if (level == ConsoleLevel.Error)
        {
            _errorCount++;
            UpdateTabStripAppearance();
        }
        _consoleEntries.Add(new ConsoleEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Level = level,
            Text = text
        });
        if (_consoleEntries.Count > 500)
            _consoleEntries.RemoveAt(0);
        RebuildConsoleList();
    }

    private void RebuildConsoleList()
    {
        var q = _consoleFilterBox.Text.Trim();
        _filteredConsole.Clear();
        foreach (var e in _consoleEntries)
        {
            if (_consoleLevelFilter == "ERR" && e.Level != ConsoleLevel.Error) continue;
            if (_consoleLevelFilter == "WRN" && e.Level != ConsoleLevel.Warn) continue;
            if (_consoleLevelFilter == "LOG" && e.Level is not (ConsoleLevel.Log or ConsoleLevel.Info or ConsoleLevel.Result)) continue;
            if (!string.IsNullOrEmpty(q) && !e.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            _filteredConsole.Add(e);
        }

        _consoleList.BeginUpdate();
        _consoleList.Items.Clear();
        foreach (var e in _filteredConsole) _consoleList.Items.Add(e);
        if (_consoleList.Items.Count > 0)
            _consoleList.TopIndex = _consoleList.Items.Count - 1;
        _consoleList.EndUpdate();
    }

    private void DrawConsoleItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _filteredConsole.Count) return;
        var item = _filteredConsole[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        Color rowBg = item.Level == ConsoleLevel.Error
            ? Color.FromArgb(sel ? 58 : 34, _p.Red)
            : item.Level == ConsoleLevel.Warn
                ? Color.FromArgb(sel ? 58 : 28, 245, 158, 11)
                : sel ? _p.HoverBg : _p.BaseBg;
        using (var bg = new SolidBrush(rowBg))
            e.Graphics.FillRectangle(bg, e.Bounds);

        Color badgeClr = item.Level switch
        {
            ConsoleLevel.Error => _p.Red,
            ConsoleLevel.Warn => Color.FromArgb(245, 158, 11),
            ConsoleLevel.Result => _p.Green,
            _ => _p.Accent
        };
        string badgeTxt = item.Level switch
        {
            ConsoleLevel.Error => "ERR",
            ConsoleLevel.Warn => "WRN",
            ConsoleLevel.Result => "<<<",
            ConsoleLevel.Info => "INF",
            _ => "LOG"
        };

        TextRenderer.DrawText(e.Graphics, item.Time, e.Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, 64, e.Bounds.Height),
            _p.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, badgeTxt, e.Font,
            new Rectangle(e.Bounds.X + 72, e.Bounds.Y, 32, e.Bounds.Height),
            badgeClr, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, item.Text, e.Font,
            new Rectangle(e.Bounds.X + 108, e.Bounds.Y, Math.Max(40, e.Bounds.Width - 114), e.Bounds.Height),
            _p.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private async Task EvaluateReplAsync()
    {
        var expr = _replInput.Text.Trim();
        if (string.IsNullOrEmpty(expr)) return;
        _replHistory.Add(expr);
        _replHistIdx = _replHistory.Count;
        _replInput.Text = "";
        AddConsoleEntry(ConsoleLevel.Info, "> " + expr);

        if (_currentCore is null)
        {
            AddConsoleEntry(ConsoleLevel.Error, "Etkin bir web sayfası yok.");
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                expression = expr,
                returnByValue = true,
                awaitPromise = true,
                userGesture = true
            });
            var raw = await _currentCore.CallDevToolsProtocolMethodAsync("Runtime.evaluate", payload);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex))
            {
                var err = ex.TryGetProperty("exception", out var exo) && exo.TryGetProperty("description", out var d)
                    ? d.GetString() ?? "Hata"
                    : ex.TryGetProperty("text", out var t) ? t.GetString() ?? "Hata" : "Hata";
                AddConsoleEntry(ConsoleLevel.Error, err);
            }
            else if (doc.RootElement.TryGetProperty("result", out var res))
            {
                string val = res.TryGetProperty("value", out var v) ? v.ToString()
                    : res.TryGetProperty("description", out var d) ? d.GetString() ?? "undefined"
                    : res.TryGetProperty("type", out var tp) ? tp.GetString() ?? "undefined" : "undefined";
                AddConsoleEntry(ConsoleLevel.Result, val);
            }
        }
        catch (Exception ex)
        {
            AddConsoleEntry(ConsoleLevel.Error, ex.Message);
        }
    }

    // ── NETWORK ─────────────────────────────────────────────────────────────────
    private void OnNetworkRequest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("requestId").GetString() ?? "";
            var req = doc.RootElement.GetProperty("request");
            var url = req.GetProperty("url").GetString() ?? "";
            var method = req.GetProperty("method").GetString() ?? "GET";
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "Other" : "Other";
            var ts = doc.RootElement.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetDouble() : 0;
            var name = Uri.TryCreate(url, UriKind.Absolute, out var u)
                ? (string.IsNullOrEmpty(Path.GetFileName(u.AbsolutePath)) ? u.Host : Path.GetFileName(u.AbsolutePath))
                : url;

            var item = new NetworkItem
            {
                RequestId = id,
                Name = name,
                Url = url,
                Method = method,
                Type = type,
                StartWallTime = ts,
                RequestHeaders = req.TryGetProperty("headers", out var hdr) ? hdr.ToString() : ""
            };
            _networkMap[id] = item;
            _networkItems.Add(item);
            if (_networkItems.Count > 350)
            {
                _networkMap.Remove(_networkItems[0].RequestId);
                _networkItems.RemoveAt(0);
            }
            RebuildNetworkList();
        }
        catch { }
    }

    private void OnNetworkResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("requestId").GetString() ?? "";
            if (!_networkMap.TryGetValue(id, out var item)) return;
            var resp = doc.RootElement.GetProperty("response");
            int status = resp.GetProperty("status").GetInt32();
            item.Status = status.ToString();
            item.ResponseHeaders = resp.TryGetProperty("headers", out var hdr) ? hdr.ToString() : "";
            RebuildNetworkList();
        }
        catch { }
    }

    private void OnNetworkFinished(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("requestId").GetString() ?? "";
            if (!_networkMap.TryGetValue(id, out var item)) return;
            if (doc.RootElement.TryGetProperty("encodedDataLength", out var len))
                item.SizeBytes = (long)len.GetDouble();
            if (doc.RootElement.TryGetProperty("timestamp", out var ts) && item.StartWallTime > 0)
                item.ElapsedMs = Math.Max(0, (ts.GetDouble() - item.StartWallTime) * 1000.0);
            RebuildNetworkList();
        }
        catch { }
    }

    private void OnNetworkFailed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("requestId").GetString() ?? "";
            if (!_networkMap.TryGetValue(id, out var item)) return;
            item.Status = "Hata";
            RebuildNetworkList();
        }
        catch { }
    }

    private void RebuildNetworkList()
    {
        _netList.BeginUpdate();
        _netList.Items.Clear();
        foreach (var it in _networkItems)
        {
            if (_netTypeFilter == "XHR" && it.Type is not ("XHR" or "Fetch")) continue;
            if (_netTypeFilter != "ALL" && _netTypeFilter != "XHR" && !it.Type.Equals(_netTypeFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var sizeStr = it.SizeBytes <= 0 ? "-" : it.SizeBytes < 1024 ? $"{it.SizeBytes} B" : $"{it.SizeBytes / 1024.0:F1} KB";
            var timeStr = it.ElapsedMs <= 0 ? "-" : $"{it.ElapsedMs:F0} ms";
            var lvi = new ListViewItem(new[] { it.Name, it.Status, it.Method, it.Type, sizeStr, timeStr }) { Tag = it };
            _netList.Items.Add(lvi);
        }
        _netList.EndUpdate();
    }

    private async Task ShowSelectedNetworkDetailsAsync()
    {
        if (_netList.SelectedItems.Count == 0 || _netList.SelectedItems[0].Tag is not NetworkItem item)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"URL: {item.Url}");
        sb.AppendLine($"Durum: {item.Status}   Metot: {item.Method}   Tür: {item.Type}");
        sb.AppendLine("--- Yanıt Üstbilgileri ---");
        sb.AppendLine(item.ResponseHeaders);
        sb.AppendLine("--- İstek Üstbilgileri ---");
        sb.AppendLine(item.RequestHeaders);

        if (_currentCore is not null && !string.IsNullOrEmpty(item.RequestId))
        {
            try
            {
                var bodyJson = await _currentCore.CallDevToolsProtocolMethodAsync("Network.getResponseBody",
                    JsonSerializer.Serialize(new { requestId = item.RequestId }));
                using var doc = JsonDocument.Parse(bodyJson);
                if (doc.RootElement.TryGetProperty("body", out var b))
                {
                    var body = b.GetString() ?? "";
                    sb.AppendLine("--- Yanıt İçeriği ---");
                    sb.AppendLine(body.Length > 8000 ? body[..8000] + "\n... (kısaltıldı)" : body);
                }
            }
            catch { }
        }
        _netDetailBox.Text = sb.ToString();
    }

    // ── APPLICATION STORAGE ─────────────────────────────────────────────────────
    public async Task RefreshStorageAsync()
    {
        if (_currentCore is null) return;
        _storageList.BeginUpdate();
        _storageList.Items.Clear();

        try
        {
            if (_storageMode == "cookies")
            {
                var raw = await _currentCore.CallDevToolsProtocolMethodAsync("Network.getCookies", "{}");
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("cookies", out var arr))
                {
                    foreach (var c in arr.EnumerateArray())
                    {
                        var name = c.GetProperty("name").GetString() ?? "";
                        var val = c.GetProperty("value").GetString() ?? "";
                        var dom = c.GetProperty("domain").GetString() ?? "";
                        _storageList.Items.Add(new ListViewItem(new[] { name, val, dom }) { Tag = dom });
                    }
                }
            }
            else
            {
                string store = _storageMode == "local" ? "localStorage" : "sessionStorage";
                var json = await _currentCore.ExecuteScriptAsync($@"(function(){{
                    var out = [];
                    for (var i = 0; i < {store}.length; i++) {{
                        var k = {store}.key(i);
                        out.push([k, {store}.getItem(k) || '']);
                    }}
                    return out;
                }})();");
                using var doc = JsonDocument.Parse(json);
                foreach (var pair in doc.RootElement.EnumerateArray())
                {
                    var k = pair[0].GetString() ?? "";
                    var v = pair[1].GetString() ?? "";
                    _storageList.Items.Add(new ListViewItem(new[] { k, v, $"{v.Length} krk" }));
                }
            }
        }
        catch { }

        _storageList.EndUpdate();
    }

    private async Task DeleteSelectedStorageItemAsync()
    {
        if (_currentCore is null || _storageList.SelectedItems.Count == 0) return;
        var item = _storageList.SelectedItems[0];
        var key = item.Text;
        try
        {
            if (_storageMode == "cookies")
            {
                var domain = item.Tag as string ?? "";
                await _currentCore.CallDevToolsProtocolMethodAsync("Network.deleteCookies",
                    JsonSerializer.Serialize(new { name = key, domain }));
            }
            else
            {
                string store = _storageMode == "local" ? "localStorage" : "sessionStorage";
                await _currentCore.ExecuteScriptAsync($"{store}.removeItem({JsonSerializer.Serialize(key)});");
            }
            await RefreshStorageAsync();
        }
        catch { }
    }

    // ── SOURCES ─────────────────────────────────────────────────────────────────
    public async Task RefreshSourcesListAsync()
    {
        if (_currentCore is null) return;
        _sourceEntries.Clear();
        _sourceFilesList.Items.Clear();

        try
        {
            var pageUrl = _currentCore.Source ?? "";
            _sourceEntries.Add(($"📄 Belge: {(string.IsNullOrEmpty(pageUrl) ? "Ana Sayfa" : pageUrl)}", pageUrl, true));
            var json = await _currentCore.ExecuteScriptAsync(@"(function(){
                var list = [];
                document.querySelectorAll('script[src]').forEach(s => list.push(['JS', s.src]));
                document.querySelectorAll('link[rel=""stylesheet""][href]').forEach(l => list.push(['CSS', l.href]));
                return list.slice(0, 60);
            })();");
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var kind = el[0].GetString() ?? "";
                var url = el[1].GetString() ?? "";
                var fname = Uri.TryCreate(url, UriKind.Absolute, out var u) ? Path.GetFileName(u.AbsolutePath) : url;
                if (string.IsNullOrWhiteSpace(fname)) fname = url;
                _sourceEntries.Add(($"[{kind}] {fname} — {url}", url, false));
            }
            foreach (var entry in _sourceEntries)
                _sourceFilesList.Items.Add(entry.Label);
            if (_sourceFilesList.Items.Count > 0)
                _sourceFilesList.SelectedIndex = 0;
        }
        catch { }
    }

    private async Task LoadSelectedSourceAsync()
    {
        if (_currentCore is null || _sourceFilesList.SelectedIndex < 0 || _sourceFilesList.SelectedIndex >= _sourceEntries.Count)
            return;
        var entry = _sourceEntries[_sourceFilesList.SelectedIndex];
        try
        {
            if (entry.IsDocument)
            {
                var res = await _currentCore.ExecuteScriptAsync("document.documentElement.outerHTML");
                _sourceViewer.Text = JsonSerializer.Deserialize<string>(res) ?? "";
            }
            else
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                _sourceViewer.Text = await http.GetStringAsync(entry.Url);
            }
        }
        catch (Exception ex)
        {
            _sourceViewer.Text = "// Kaynak yüklenemedi: " + ex.Message;
        }
    }

    // ── PREVIEW / SELF-TEST SAMPLE DATA ─────────────────────────────────────────
    public void PopulatePreviewSampleData()
    {
        _emptyLabel.Visible = false;
        _domTree.BeginUpdate();
        _domTree.Nodes.Clear();
        var html = new TreeNode("<html>") { Tag = new DomNodeTag { Selector = "html", Tag = "html" } };
        var head = new TreeNode("<head>") { Tag = new DomNodeTag { Selector = "head", Tag = "head" } };
        var body = new TreeNode("<body.dark-theme>") { Tag = new DomNodeTag { Selector = "body", Tag = "body" } };
        var main = new TreeNode("<main#app.container>") { Tag = new DomNodeTag { Selector = "main#app", Tag = "main" } };
        var h1 = new TreeNode("<h1> ED Browser v4.2 </h1>") { Tag = new DomNodeTag { Selector = "main#app > h1", Tag = "h1" } };
        var nav = new TreeNode("<nav.toolbar>") { Tag = new DomNodeTag { Selector = "nav.toolbar", Tag = "nav" } };
        main.Nodes.Add(nav);
        main.Nodes.Add(h1);
        body.Nodes.Add(main);
        html.Nodes.Add(head);
        html.Nodes.Add(body);
        _domTree.Nodes.Add(html);
        html.ExpandAll();
        _domTree.SelectedNode = h1;
        _domTree.EndUpdate();

        _selSelectorLbl.Text = "main#app > h1";
        _inlineStyleBox.Text = "color: #38bdf8; font-size: 24px;";
        _stylesList.Items.Clear();
        _stylesList.Items.Add(new ListViewItem(new[] { "display", "block" }));
        _stylesList.Items.Add(new ListViewItem(new[] { "color", "rgb(56, 189, 248)" }));
        _stylesList.Items.Add(new ListViewItem(new[] { "font-family", "Segoe UI, sans-serif" }));
        _stylesList.Items.Add(new ListViewItem(new[] { "font-size", "24px" }));
        _stylesList.Items.Add(new ListViewItem(new[] { "padding", "12px 16px" }));

        _consoleEntries.Clear();
        AddConsoleEntry(ConsoleLevel.Info, "ED Browser DevTools CDP oturumu hazır.");
        AddConsoleEntry(ConsoleLevel.Warn, "Örnek uyarı: Pasif kaynak önbelleği");
        AddConsoleEntry(ConsoleLevel.Result, "\"Merhaba Dünya\"");

        _networkItems.Clear();
        _networkItems.Add(new NetworkItem { RequestId = "1", Name = "index.html", Status = "200", Method = "GET", Type = "Document", SizeBytes = 14200, ElapsedMs = 42 });
        _networkItems.Add(new NetworkItem { RequestId = "2", Name = "app.js", Status = "200", Method = "GET", Type = "Script", SizeBytes = 48600, ElapsedMs = 68 });
        RebuildNetworkList();
    }

    // ── STYLING & THEME ─────────────────────────────────────────────────────────
    public void ApplyTheme(ThemePalette p)
    {
        _p = p;
        BackColor = p.SurfaceBg;
        ForeColor = p.TextPrimary;
        _header.BackColor = p.ElevatedBg;
        _bodyHost.BackColor = p.BaseBg;
        _emptyLabel.BackColor = p.BaseBg;
        _emptyLabel.ForeColor = p.TextMuted;

        foreach (var b in new[] { _pickerBtn, _deviceBtn, _dockRightBtn, _dockBottomBtn, _externalBtn, _closeBtn })
        {
            b.NormalBg = p.ElevatedBg;
            b.HoverBg = p.HoverBg;
            b.IconColor = b.IsActive ? p.Accent : p.TextSecondary;
        }
        UpdateDockButtons();
        UpdateTabStripAppearance();

        _elementsView.BackColor = p.BaseBg;
        _domSearchBox.ApplyTheme(p, elevated: true);
        _domRefreshBtn.ApplyTheme(p);
        _domSplitter.BackColor = p.Border;
        _domTree.BackColor = p.BaseBg;
        _domTree.ForeColor = p.TextPrimary;
        _selSelectorLbl.ForeColor = p.Accent;
        _stylesList.BackColor = p.SurfaceBg;
        _stylesList.ForeColor = p.TextPrimary;
        _inlineStyleBox.ApplyTheme(p, elevated: true);
        _applyStyleBtn.ApplyTheme(p);

        _consoleView.BackColor = p.BaseBg;
        _consoleFilterBox.ApplyTheme(p, elevated: true);
        _consoleList.BackColor = p.BaseBg;
        _consoleList.ForeColor = p.TextPrimary;
        _replInput.ApplyTheme(p, elevated: true);
        _replRunBtn.ApplyTheme(p);

        _networkView.BackColor = p.BaseBg;
        _cacheDisabledCheck.ForeColor = p.TextSecondary;
        _netList.BackColor = p.BaseBg;
        _netList.ForeColor = p.TextPrimary;
        _netDetailBox.BackColor = p.SurfaceBg;
        _netDetailBox.ForeColor = p.TextPrimary;

        _appView.BackColor = p.BaseBg;
        _storageList.BackColor = p.BaseBg;
        _storageList.ForeColor = p.TextPrimary;

        _sourcesView.BackColor = p.BaseBg;
        _sourceFilesList.BackColor = p.BaseBg;
        _sourceFilesList.ForeColor = p.TextPrimary;
        _sourceViewer.BackColor = p.SurfaceBg;
        _sourceViewer.ForeColor = p.TextPrimary;

        StyleFilterGroup(_consoleFilterBtns, _consoleLevelFilter);
        StyleFilterGroup(_netFilterBtns, _netTypeFilter);
        StyleFilterGroup(_storageModeBtns, _storageMode);
        Invalidate();
    }

    private void UpdateDockButtons()
    {
        _dockRightBtn.IsActive = DockMode == DevToolsDockMode.Right;
        _dockRightBtn.IconColor = _dockRightBtn.IsActive ? _p.Accent : _p.TextSecondary;
        _dockBottomBtn.IsActive = DockMode == DevToolsDockMode.Bottom;
        _dockBottomBtn.IconColor = _dockBottomBtn.IsActive ? _p.Accent : _p.TextSecondary;
    }

    private void UpdateTabStripAppearance()
    {
        string[] baseNames = { "Öğeler", "Konsol", "Ağ", "Depo", "Kaynak" };
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            var b = _tabButtons[i];
            b.Text = (i == 1 && _errorCount > 0) ? $"Konsol ({_errorCount})" : baseNames[i];
            bool active = i == ActiveTabIndex;
            b.BackColor = active ? _p.HoverBg : _p.ElevatedBg;
            b.ForeColor = active ? _p.Accent : (i == 1 && _errorCount > 0 ? _p.Red : _p.TextSecondary);
        }
    }

    private void StyleFilterGroup(Button[] btns, string activeTag)
    {
        foreach (var b in btns)
        {
            bool active = string.Equals(b.Tag as string, activeTag, StringComparison.OrdinalIgnoreCase);
            b.BackColor = active ? _p.HoverBg : _p.ElevatedBg;
            b.ForeColor = active ? _p.Accent : _p.TextSecondary;
            b.FlatAppearance.BorderColor = active ? _p.Accent : _p.Border;
        }
    }
}
