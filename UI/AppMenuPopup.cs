using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using EDBrowser.Services;

namespace EDBrowser.UI;

public sealed class AppMenuState
{
    public string Version { get; init; } = "4.2.0";
    public long TotalRamMb { get; init; }
    public bool IsDarkTheme { get; init; } = true;
    public bool AdBlockEnabled { get; init; } = true;
    public bool ShowBookmarkBar { get; init; } = true;
    public bool ShowLeftSidebar { get; init; }
    public bool IsActiveTabPinned { get; init; }
    public bool IsActiveTabMuted { get; init; }
    public bool CanReopenClosedTab { get; init; }
    public bool HasNavigatedTab { get; init; }
    public int ZoomPercent { get; init; } = 100;
    public bool IsFullscreen { get; init; }

    public Action? NewTab { get; init; }
    public Action? NewWindow { get; init; }
    public Action? NewIncognito { get; init; }
    public Action? ReopenClosedTab { get; init; }

    public Action? TogglePinTab { get; init; }
    public Action? ToggleMuteTab { get; init; }
    public Action? DuplicateTab { get; init; }

    public Action? OpenBookmarks { get; init; }
    public Action? ToggleBookmarkBar { get; init; }
    public Action? ToggleLeftSidebar { get; init; }
    public Action? OpenHistory { get; init; }
    public Action? OpenDownloads { get; init; }
    public Action? OpenExtensions { get; init; }
    public Action? OpenPasswords { get; init; }

    public Action? ZoomOut { get; init; }
    public Action? ZoomReset { get; init; }
    public Action? ZoomIn { get; init; }
    public Action? ToggleFullscreen { get; init; }

    public Action? FindInPage { get; init; }
    public Action? SavePageAs { get; init; }
    public Action? PrintPage { get; init; }
    public Action? PrintToPdf { get; init; }
    public Action? ViewPageSource { get; init; }
    public Action? TakeScreenshot { get; init; }

    public Action? ClearBrowsingData { get; init; }
    public Action? OptimizeRam { get; init; }
    public Action? ImportData { get; init; }
    public Action? ToggleDevTools { get; init; }

    public Action? ToggleTheme { get; init; }
    public Action? ToggleAdBlock { get; init; }
    public Action? OpenSettings { get; init; }
    public Action? CheckUpdates { get; init; }
    public Action? ExitApp { get; init; }
}

/// <summary>
/// Modern, sleek borderless Chrome/Arc-style application menu popup with DWM rounded corners,
/// vector icons, right-aligned shortcut badges, inline quick toggles, and triple auto-dismiss
/// (Deactivate + Escape + global mouse outside-click monitor so clicking inside WebView2 always closes it).
/// </summary>
public sealed class AppMenuPopup : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int CS_DROPSHADOW = 0x00020000;
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;

    private readonly Form? _ownerForm;
    private readonly ThemePalette _p;
    private readonly AppMenuState _state;
    private readonly System.Windows.Forms.Timer _outsideClickTimer;
    private readonly Panel _scrollHost;
    private readonly int _menuWidth;
    private bool _closing;

    public int MenuItemCount { get; private set; }
    public int MaxItemRightEdge { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => false;

    public AppMenuPopup(Form? ownerForm, AppMenuState state, ThemePalette palette, Point anchorBottomRightScreen, bool enableOutsideDismiss = true)
    {
        _ownerForm = ownerForm;
        _state = state;
        _p = palette;

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

        float sc = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        _menuWidth = (int)Math.Round(332 * sc);

        _scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = _p.SurfaceBg,
            Padding = new Padding(6)
        };
        Controls.Add(_scrollHost);

        int y = 6;
        y = BuildQuickHeader(y, sc);
        y = AddSeparator(y);

        y = AddActionRow(y, VectorIcon.Plus, "Yeni Sekme", "Ctrl+T", true, _state.NewTab);
        y = AddActionRow(y, VectorIcon.NewWindow, "Yeni Pencere", "Ctrl+N", true, _state.NewWindow);
        y = AddActionRow(y, VectorIcon.Incognito, "Yeni Gizli Pencere", "Ctrl+Shift+P", true, _state.NewIncognito);
        y = AddActionRow(y, VectorIcon.Undo, "Kapalı Sekmeyi Yeniden Aç", "Ctrl+Shift+T", _state.CanReopenClosedTab, _state.ReopenClosedTab);
        y = AddSeparator(y);

        y = AddActionRow(y, VectorIcon.Pin, _state.IsActiveTabPinned ? "Sekme Sabitlemesini Kaldır" : "Sekmeyi Sabitle", "", true, _state.TogglePinTab);
        y = AddActionRow(y, _state.IsActiveTabMuted ? VectorIcon.VolumeOn : VectorIcon.VolumeOff,
            _state.IsActiveTabMuted ? "Sekme Sesini Aç" : "Sekme Sesini Kapat", "", _state.HasNavigatedTab, _state.ToggleMuteTab);
        y = AddActionRow(y, VectorIcon.NewWindow, "Sekmeyi Çoğalt", "", true, _state.DuplicateTab);
        y = AddSeparator(y);

        y = BuildBookmarksRow(y, sc);
        y = AddActionRow(y, VectorIcon.History, "Geçmiş", "Ctrl+H", true, _state.OpenHistory);
        y = AddActionRow(y, VectorIcon.Download, "İndirilenler", "Ctrl+Shift+J", true, _state.OpenDownloads);
        y = AddActionRow(y, VectorIcon.Puzzle, "Eklentiler ve Mağaza", "Ctrl+Shift+E", true, _state.OpenExtensions);
        y = AddActionRow(y, VectorIcon.Key, "Şifre Yöneticisi", "", true, _state.OpenPasswords);
        y = AddActionRow(y, VectorIcon.Sidebar, _state.ShowLeftSidebar ? "Sol Kenar Çubuğunu Gizle" : "Sol Kenar Çubuğunu Göster", "Ctrl+Shift+L", true, _state.ToggleLeftSidebar);
        y = AddSeparator(y);

        y = BuildZoomRow(y, sc);
        y = AddSeparator(y);

        y = AddActionRow(y, VectorIcon.Search, "Sayfada Ara", "Ctrl+F", true, _state.FindInPage);
        y = AddActionRow(y, VectorIcon.Save, "Sayfayı Farklı Kaydet", "Ctrl+S", _state.HasNavigatedTab, _state.SavePageAs);
        y = AddActionRow(y, VectorIcon.Printer, "Yazdır", "Ctrl+P", _state.HasNavigatedTab, _state.PrintPage);
        y = AddActionRow(y, VectorIcon.FilePdf, "PDF Olarak Kaydet", "", _state.HasNavigatedTab, _state.PrintToPdf);
        y = AddActionRow(y, VectorIcon.Code, "Sayfa Kaynağını Görüntüle", "Ctrl+U", _state.HasNavigatedTab, _state.ViewPageSource);
        y = AddActionRow(y, VectorIcon.Camera, "Ekran Görüntüsü Al", "Ctrl+Shift+S", _state.HasNavigatedTab, _state.TakeScreenshot);
        y = AddSeparator(y);

        y = AddActionRow(y, VectorIcon.Trash, "Tarama Verilerini Temizle", "Ctrl+Shift+Del", true, _state.ClearBrowsingData);
        y = AddActionRow(y, VectorIcon.Bolt, "RAM Optimize Et", "", true, _state.OptimizeRam);
        y = AddActionRow(y, VectorIcon.Import, "Diğer Tarayıcıdan İçe Aktar", "", true, _state.ImportData);
        y = AddActionRow(y, VectorIcon.Terminal, "Geliştirici Araçları", "F12", true, _state.ToggleDevTools);
        y = AddSeparator(y);

        y = AddActionRow(y, VectorIcon.Settings, "Ayarlar", "Ctrl+,", true, _state.OpenSettings);
        y = AddActionRow(y, VectorIcon.RefreshUpdate, "Güncellemeleri Denetle", "", true, _state.CheckUpdates);
        y = AddActionRow(y, VectorIcon.Exit, "Çıkış", "Alt+F4", true, _state.ExitApp, dangerTint: true);

        int desiredH = y + 8;
        var wa = Screen.FromPoint(anchorBottomRightScreen).WorkingArea;
        int maxH = Math.Max(320, wa.Height - 24);
        int finalH = Math.Min(desiredH, maxH);
        int finalW = _menuWidth + (desiredH > maxH ? SystemInformation.VerticalScrollBarWidth : 0);

        int posX = Math.Clamp(anchorBottomRightScreen.X - finalW, wa.Left + 8, wa.Right - finalW - 8);
        int posY = anchorBottomRightScreen.Y + finalH <= wa.Bottom - 6
            ? anchorBottomRightScreen.Y
            : Math.Max(wa.Top + 6, wa.Bottom - finalH - 8);

        Size = new Size(finalW, finalH);
        Location = new Point(posX, posY);

        Paint += (_, e) =>
        {
            using var pen = new Pen(_p.Border, 1.2f);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        _outsideClickTimer = new System.Windows.Forms.Timer { Interval = 75 };
        if (enableOutsideDismiss)
        {
            Deactivate += (_, _) => SafeClose();
            _outsideClickTimer.Tick += (_, _) =>
            {
                if (_closing || IsDisposed || !Visible) return;
                bool lDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool rDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
                if ((lDown || rDown) && !Bounds.Contains(Cursor.Position))
                    SafeClose();
            };
            Shown += (_, _) => _outsideClickTimer.Start();
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

    private void InvokeAction(Action? act)
    {
        SafeClose();
        if (act is null) return;
        if (_ownerForm is { IsDisposed: false, IsHandleCreated: true })
            _ownerForm.BeginInvoke(act);
        else
            act();
    }

    private int BuildQuickHeader(int y, float sc)
    {
        int rowW = _menuWidth - 14;
        var card = new Panel
        {
            Location = new Point(7, y),
            Size = new Size(rowW, 40),
            BackColor = _p.ElevatedBg
        };
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, card.Width - 1f, card.Height - 1f);
            using var path = GraphicsExt.CreateRoundedRect(r, 8);
            using var pen = new Pen(_p.Border, 1f);
            e.Graphics.DrawPath(pen, path);
        };

        var brand = new Label
        {
            Text = $"ED Browser v{_state.Version}  •  {_state.TotalRamMb} MB",
            Location = new Point(10, 0),
            Size = new Size(rowW - 108, 40),
            ForeColor = _p.Accent,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };

        IconButton MakeQuick(VectorIcon ic, string tip, int rightOffset, Color color, Action? act)
        {
            var b = new IconButton
            {
                IconType = ic,
                Size = new Size(28, 28),
                Location = new Point(rowW - rightOffset, 6),
                IconSizePx = 14,
                CornerRadius = 7,
                NormalBg = _p.SurfaceBg,
                HoverBg = _p.HoverBg,
                IconColor = color
            };
            b.SetTooltip(tip);
            b.Click += (_, _) => InvokeAction(act);
            return b;
        }

        var themeBtn = MakeQuick(_state.IsDarkTheme ? VectorIcon.Sun : VectorIcon.Moon,
            _state.IsDarkTheme ? "Açık Temaya Geç" : "Koyu Temaya Geç", 98, _p.TextSecondary, _state.ToggleTheme);
        var shieldBtn = MakeQuick(VectorIcon.Shield,
            _state.AdBlockEnabled ? "Reklam Engelleyici: Açık" : "Reklam Engelleyici: Kapalı", 66,
            _state.AdBlockEnabled ? _p.Green : _p.TextMuted, _state.ToggleAdBlock);
        var updBtn = MakeQuick(VectorIcon.RefreshUpdate, "Güncellemeleri Denetle", 34, _p.Accent, _state.CheckUpdates);

        card.Controls.AddRange(new Control[] { brand, themeBtn, shieldBtn, updBtn });
        _scrollHost.Controls.Add(card);
        MaxItemRightEdge = Math.Max(MaxItemRightEdge, card.Right);
        return y + card.Height + 4;
    }

    private int BuildBookmarksRow(int y, float sc)
    {
        int rowW = _menuWidth - 14;
        var row = new Panel
        {
            Location = new Point(7, y),
            Size = new Size(rowW, 30),
            BackColor = _p.SurfaceBg,
            Cursor = Cursors.Hand
        };

        bool hovered = false;
        row.MouseEnter += (_, _) => { hovered = true; row.Invalidate(); };
        row.MouseLeave += (_, _) => { hovered = false; row.Invalidate(); };
        row.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) InvokeAction(_state.OpenBookmarks); };
        row.Paint += (_, e) => PaintMenuRow(e.Graphics, row.ClientRectangle, hovered, VectorIcon.Bookmark, "Yer İmleri", "Ctrl+Shift+O", true, false, sc, rightReserve: 68);

        var barToggle = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = _state.ShowBookmarkBar ? "Çubuk ✓" : "Çubuk",
            CornerRadius = 6,
            Location = new Point(rowW - 76, 3),
            Size = new Size(72, 24),
            Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
            NormalBg = _state.ShowBookmarkBar ? _p.HoverBg : _p.ElevatedBg,
            HoverBg = _p.HoverBg,
            IconColor = _state.ShowBookmarkBar ? _p.Accent : _p.TextMuted,
            BorderColor = _state.ShowBookmarkBar ? _p.Accent : _p.Border
        };
        barToggle.Click += (_, _) => InvokeAction(_state.ToggleBookmarkBar);

        row.Controls.Add(barToggle);
        _scrollHost.Controls.Add(row);
        MenuItemCount++;
        MaxItemRightEdge = Math.Max(MaxItemRightEdge, row.Right);
        return y + 31;
    }

    private int BuildZoomRow(int y, float sc)
    {
        int rowW = _menuWidth - 14;
        var row = new Panel
        {
            Location = new Point(7, y),
            Size = new Size(rowW, 34),
            BackColor = _p.SurfaceBg
        };

        row.Paint += (_, e) =>
        {
            IconPainter.Draw(e.Graphics, VectorIcon.ZoomIn, new RectangleF(10, 9, 15, 15), _p.TextSecondary, 1.5f);
            TextRenderer.DrawText(e.Graphics, "Yakınlaştırma", new Font("Segoe UI", 9f),
                new Rectangle(32, 0, 110, 34), _p.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        IconButton MakeZBtn(VectorIcon ic, string tip, int x, Action? act)
        {
            var b = new IconButton
            {
                IconType = ic,
                Size = new Size(28, 26),
                Location = new Point(x, 4),
                IconSizePx = 13,
                CornerRadius = 6,
                NormalBg = _p.ElevatedBg,
                HoverBg = _p.HoverBg,
                IconColor = _p.TextPrimary,
                BorderColor = _p.Border
            };
            b.SetTooltip(tip);
            b.Click += (_, _) => InvokeAction(act);
            return b;
        }

        var outBtn = MakeZBtn(VectorIcon.Minus, "Uzaklaştır (Ctrl+-)", rowW - 166, _state.ZoomOut);
        var pctBtn = new IconButton
        {
            IconType = VectorIcon.None,
            LabelText = $"%{_state.ZoomPercent}",
            CornerRadius = 6,
            Location = new Point(rowW - 134, 4),
            Size = new Size(60, 26),
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            NormalBg = _p.ElevatedBg,
            HoverBg = _p.HoverBg,
            IconColor = _p.Accent,
            BorderColor = _p.Border
        };
        pctBtn.Click += (_, _) => InvokeAction(_state.ZoomReset);

        var inBtn = MakeZBtn(VectorIcon.Plus, "Yakınlaştır (Ctrl++)", rowW - 70, _state.ZoomIn);
        var fsBtn = MakeZBtn(VectorIcon.Fullscreen, _state.IsFullscreen ? "Tam Ekrandan Çık (F11)" : "Tam Ekran (F11)", rowW - 36, _state.ToggleFullscreen);
        if (_state.IsFullscreen) fsBtn.IconColor = _p.Accent;

        row.Controls.AddRange(new Control[] { outBtn, pctBtn, inBtn, fsBtn });
        _scrollHost.Controls.Add(row);
        MenuItemCount++;
        MaxItemRightEdge = Math.Max(MaxItemRightEdge, row.Right);
        return y + 35;
    }

    private int AddActionRow(int y, VectorIcon icon, string text, string shortcut, bool enabled, Action? action, bool dangerTint = false)
    {
        int rowW = _menuWidth - 14;
        var row = new Panel
        {
            Location = new Point(7, y),
            Size = new Size(rowW, 25),
            BackColor = _p.SurfaceBg,
            Cursor = enabled ? Cursors.Hand : Cursors.Default
        };

        bool hovered = false;
        row.MouseEnter += (_, _) => { if (enabled) { hovered = true; row.Invalidate(); } };
        row.MouseLeave += (_, _) => { if (hovered) { hovered = false; row.Invalidate(); } };
        row.MouseUp += (_, e) =>
        {
            if (enabled && e.Button == MouseButtons.Left)
                InvokeAction(action);
        };
        row.Paint += (_, e) => PaintMenuRow(e.Graphics, row.ClientRectangle, hovered, icon, text, shortcut, enabled, dangerTint, 1f, 8);

        _scrollHost.Controls.Add(row);
        MenuItemCount++;
        MaxItemRightEdge = Math.Max(MaxItemRightEdge, row.Right);
        return y + 26;
    }

    private void PaintMenuRow(Graphics g, Rectangle bounds, bool hovered, VectorIcon icon, string text, string shortcut, bool enabled, bool dangerTint, float sc, int rightReserve)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (hovered)
        {
            var r = new RectangleF(1, 1, bounds.Width - 2, bounds.Height - 2);
            using var gp = GraphicsExt.CreateRoundedRect(r, 6);
            using var br = new SolidBrush(dangerTint ? Color.FromArgb(45, _p.Red) : _p.HoverBg);
            g.FillPath(br, gp);
        }

        Color iconClr = !enabled ? _p.TextMuted : dangerTint ? _p.Red : (hovered ? _p.Accent : _p.TextSecondary);
        Color textClr = !enabled ? _p.TextMuted : dangerTint ? _p.Red : _p.TextPrimary;

        IconPainter.Draw(g, icon, new RectangleF(10, (bounds.Height - 14) / 2f, 14, 14), iconClr, 1.5f);

        int shortcutW = string.IsNullOrEmpty(shortcut) ? 0 : 94;
        if (shortcutW > 0 && rightReserve <= 12)
        {
            var scRect = new Rectangle(bounds.Width - shortcutW - 8, 0, shortcutW, bounds.Height);
            TextRenderer.DrawText(g, shortcut, new Font("Segoe UI", 8f), scRect, _p.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        int txtWidth = Math.Max(40, bounds.Width - 34 - Math.Max(rightReserve, shortcutW + 12));
        var txtRect = new Rectangle(32, 0, txtWidth, bounds.Height);
        TextRenderer.DrawText(g, text, new Font("Segoe UI", 9f), txtRect, textClr,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private int AddSeparator(int y)
    {
        int rowW = _menuWidth - 22;
        var sep = new Panel
        {
            Location = new Point(11, y + 3),
            Size = new Size(rowW, 1),
            BackColor = _p.Border
        };
        _scrollHost.Controls.Add(sep);
        return y + 7;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _outsideClickTimer.Dispose();
        base.Dispose(disposing);
    }
}
