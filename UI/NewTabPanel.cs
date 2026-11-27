using System.Drawing.Drawing2D;
using System.Drawing.Text;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

/// <summary>
/// Sleek native GDI+ New Tab page (0 MB WebView2 process until navigation).
/// Contains NO hardcoded sites; shortcuts are 100% user-managed.
/// </summary>
public sealed class NewTabPanel : Panel
{
    private readonly Panel _centerPanel;
    private readonly Label _titleLabel;
    private readonly Label _subLabel;
    private readonly RoundedTextBox _searchBox;
    private readonly FlowLayoutPanel _shortcutsPanel;
    private readonly ModernCardPanel _statsCard;
    private readonly Label _statsLabel;
    private ThemePalette _p = ThemeService.Dark;
    private List<QuickShortcut> _shortcuts = new();

    public event Action<string>? NavigateRequested;
    public event Action<string>? OpenNewTabRequested;
    public event Action? ShortcutsChanged;

    public NewTabPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill;

        _centerPanel = new Panel
        {
            Size = new Size(680, 440),
            BackColor = Color.Transparent
        };

        var emblem = new Panel { Size = new Size(64, 64), Location = new Point(308, 4), BackColor = Color.Transparent };
        emblem.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(2, 2, 60, 60);
            using var gp = GraphicsExt.CreateRoundedRect(r, 16);
            using var bg = new LinearGradientBrush(r, Color.FromArgb(18, 28, 56), Color.FromArgb(26, 36, 74), 45f);
            g.FillPath(bg, gp);
            using var pen = new Pen(_p.Accent, 2f);
            g.DrawPath(pen, gp);
            using var f = new Font("Segoe UI", 20f, FontStyle.Bold);
            TextRenderer.DrawText(g, "ED", f, Rectangle.Round(r), _p.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        _titleLabel = new Label
        {
            Text = "ED BROWSER",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(680, 42),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 74),
            BackColor = Color.Transparent
        };

        _subLabel = new Label
        {
            Text = "Geliştirici: Erol Dizdar  •  Ultra Düşük RAM Sürümü (v4.3)",
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = false,
            Size = new Size(680, 24),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 116),
            BackColor = Color.Transparent
        };

        _searchBox = new RoundedTextBox
        {
            PillShape = true,
            Size = new Size(580, 44),
            Location = new Point(50, 154),
            Font = new Font("Segoe UI", 11f),
            PlaceholderText = "Web'de ara veya bir URL yaz… (Enter)"
        };
        _searchBox.Inner.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                e.SuppressKeyPress = true;
                var q = _searchBox.Text.Trim();
                _searchBox.Text = string.Empty;
                NavigateRequested?.Invoke(q);
            }
        };

        _shortcutsPanel = new FlowLayoutPanel
        {
            Location = new Point(20, 220),
            Size = new Size(640, 148),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        _statsCard = new ModernCardPanel
        {
            Size = new Size(440, 36),
            Location = new Point(120, 384),
            Padding = new Padding(12, 4, 12, 4)
        };
        _statsLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };
        _statsCard.Controls.Add(_statsLabel);

        _centerPanel.Controls.AddRange(new Control[]
        {
            emblem, _titleLabel, _subLabel, _searchBox, _shortcutsPanel, _statsCard
        });
        Controls.Add(_centerPanel);

        Resize += (_, _) =>
        {
            _centerPanel.Location = new Point(
                Math.Max(0, (ClientSize.Width - _centerPanel.Width) / 2),
                Math.Max(0, (ClientSize.Height - _centerPanel.Height) / 2 - 12));
        };

        RenderShortcuts();
    }

    public void BindShortcuts(List<QuickShortcut> shortcuts)
    {
        _shortcuts = shortcuts;
        RenderShortcuts();
    }

    public void RenderShortcuts()
    {
        _shortcutsPanel.SuspendLayout();
        _shortcutsPanel.Controls.Clear();

        foreach (var sc in _shortcuts)
            _shortcutsPanel.Controls.Add(BuildUserShortcutCard(sc));

        _shortcutsPanel.Controls.Add(BuildAddSiteCard());

        // Center cards horizontally inside _shortcutsPanel
        int totalCardW = _shortcutsPanel.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
        int availW = _shortcutsPanel.ClientSize.Width;
        int padLeft = totalCardW < availW ? Math.Max(0, (availW - totalCardW) / 2) : 6;
        _shortcutsPanel.Padding = new Padding(padLeft, 4, 4, 4);

        _shortcutsPanel.ResumeLayout();
    }

    private Control BuildUserShortcutCard(QuickShortcut sc)
    {
        var card = new ModernCardPanel
        {
            Size = new Size(92, 98),
            FillColor = _p.SurfaceBg,
            BorderColor = _p.Border,
            Margin = new Padding(5),
            Cursor = Cursors.Hand
        };

        Color badgeColor = ParseColorOrDefault(sc.ColorHex, _p.Accent);
        string initial = string.IsNullOrWhiteSpace(sc.Name)
            ? "?"
            : sc.Name.Trim()[0].ToString().ToUpperInvariant();

        var badge = new Panel
        {
            Size = new Size(42, 42),
            Location = new Point(25, 14),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        badge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0, 0, badge.Width - 1, badge.Height - 1);
            using var gp = GraphicsExt.CreateRoundedRect(r, 12);
            using var br = new SolidBrush(badgeColor);
            e.Graphics.FillPath(br, gp);
            using var f = new Font("Segoe UI", 14f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, initial, f, badge.ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        var delBtn = new IconButton
        {
            IconType = VectorIcon.Close,
            Size = new Size(20, 20),
            Location = new Point(68, 4),
            IconSizePx = 10,
            CornerRadius = 10,
            NormalBg = _p.ElevatedBg,
            HoverBg = Color.FromArgb(80, _p.Red),
            IconColor = _p.Red,
            Visible = false
        };
        delBtn.SetTooltip("Kaldır");
        delBtn.Click += (_, _) =>
        {
            _shortcuts.Remove(sc);
            ShortcutsChanged?.Invoke();
            RenderShortcuts();
        };

        var lbl = new Label
        {
            Text = sc.Name,
            UseMnemonic = false,
            Size = new Size(84, 24),
            Location = new Point(4, 62),
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
            ForeColor = _p.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        EventHandler enter = (_, _) =>
        {
            card.FillColor = _p.HoverBg;
            card.BorderColor = _p.Accent;
            delBtn.Visible = true;
            card.Invalidate();
        };
        EventHandler leave = (_, _) =>
        {
            if (card.ClientRectangle.Contains(card.PointToClient(Cursor.Position)))
                return;
            card.FillColor = _p.SurfaceBg;
            card.BorderColor = _p.Border;
            delBtn.Visible = false;
            card.Invalidate();
        };
        MouseEventHandler mouseUp = (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                NavigateRequested?.Invoke(sc.Url);
            else if (e.Button == MouseButtons.Middle)
                OpenNewTabRequested?.Invoke(sc.Url);
            else if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_p), ShowImageMargin = false, Padding = new Padding(4) };
                menu.Items.Add("Aç", null, (_, _) => NavigateRequested?.Invoke(sc.Url));
                menu.Items.Add("Yeni Sekmede Aç", null, (_, _) => OpenNewTabRequested?.Invoke(sc.Url));
                menu.Items.Add("Düzenle", null, (_, _) =>
                {
                    using var dlg = new ShortcutEditDialog(sc, _p);
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        ShortcutsChanged?.Invoke();
                        RenderShortcuts();
                    }
                });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Kaldır", null, (_, _) =>
                {
                    _shortcuts.Remove(sc);
                    ShortcutsChanged?.Invoke();
                    RenderShortcuts();
                });
                menu.Show(card, e.Location);
            }
        };

        foreach (Control c in new Control[] { card, badge, lbl })
        {
            c.MouseEnter += enter;
            c.MouseLeave += leave;
            c.MouseUp += mouseUp;
        }
        delBtn.MouseLeave += leave;

        card.Controls.Add(delBtn);
        card.Controls.Add(badge);
        card.Controls.Add(lbl);
        return card;
    }

    private Control BuildAddSiteCard()
    {
        var card = new ModernCardPanel
        {
            Size = new Size(92, 98),
            FillColor = _p.SurfaceBg,
            BorderColor = _p.Border,
            Margin = new Padding(5),
            Cursor = Cursors.Hand
        };

        var plusBox = new Panel
        {
            Size = new Size(42, 42),
            Location = new Point(25, 14),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        plusBox.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, plusBox.Width - 2, plusBox.Height - 2);
            using var gp = GraphicsExt.CreateRoundedRect(r, 12);
            using var bg = new SolidBrush(_p.ElevatedBg);
            g.FillPath(bg, gp);
            using var pen = new Pen(_p.Accent, 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, gp);
            IconPainter.Draw(g, VectorIcon.Plus, new RectangleF(12, 12, 18, 18), _p.Accent, 1.8f);
        };

        var lbl = new Label
        {
            Text = "Site Ekle",
            Size = new Size(84, 24),
            Location = new Point(4, 62),
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
            ForeColor = _p.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        EventHandler enter = (_, _) => { card.FillColor = _p.HoverBg; card.BorderColor = _p.Accent; card.Invalidate(); };
        EventHandler leave = (_, _) => { card.FillColor = _p.SurfaceBg; card.BorderColor = _p.Border; card.Invalidate(); };
        EventHandler click = (_, _) =>
        {
            using var dlg = new ShortcutEditDialog(null, _p);
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK && dlg.ResultShortcut is not null)
            {
                _shortcuts.Add(dlg.ResultShortcut);
                ShortcutsChanged?.Invoke();
                RenderShortcuts();
            }
        };

        foreach (Control c in new Control[] { card, plusBox, lbl })
        {
            c.MouseEnter += enter;
            c.MouseLeave += leave;
            c.Click += click;
        }

        card.Controls.Add(plusBox);
        card.Controls.Add(lbl);
        return card;
    }

    private static Color ParseColorOrDefault(string hex, Color fallback)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var bg = new SolidBrush(_p.BaseBg);
        g.FillRectangle(bg, ClientRectangle);

        using (var path1 = new GraphicsPath())
        {
            path1.AddEllipse(-Width * 0.1f, Height * 0.35f, Width * 0.65f, Height * 0.75f);
            using var pgb1 = new PathGradientBrush(path1)
            {
                CenterColor = Color.FromArgb(18, _p.Accent),
                SurroundColors = new[] { Color.Transparent }
            };
            g.FillPath(pgb1, path1);
        }
        using (var path2 = new GraphicsPath())
        {
            path2.AddEllipse(Width * 0.5f, -Height * 0.15f, Width * 0.65f, Height * 0.7f);
            using var pgb2 = new PathGradientBrush(path2)
            {
                CenterColor = Color.FromArgb(18, _p.AccentPurple),
                SurroundColors = new[] { Color.Transparent }
            };
            g.FillPath(pgb2, path2);
        }
    }

    public void UpdateStats(long blockedAds, long totalRamMb)
    {
        _statsLabel.Text = $"Engellenen Reklam: {blockedAds}   •   Toplam RAM: {totalRamMb} MB";
    }

    public void FocusSearch()
    {
        if (Visible)
            _searchBox.Inner.Focus();
    }

    public void ApplyTheme(ThemePalette p)
    {
        _p = p;
        BackColor = p.BaseBg;
        _titleLabel.ForeColor = p.Accent;
        _subLabel.ForeColor = p.TextMuted;
        _searchBox.ApplyTheme(p, elevated: false);
        _statsCard.FillColor = p.SurfaceBg;
        _statsCard.BorderColor = p.Border;
        _statsLabel.ForeColor = p.TextSecondary;
        RenderShortcuts();
        Invalidate();
    }
}
