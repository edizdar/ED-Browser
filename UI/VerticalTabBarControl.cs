using System.Drawing.Drawing2D;
using System.Drawing.Text;
using EDBrowser.Services;

namespace EDBrowser.UI;

/// <summary>
/// Edge/Arc-style collapsible vertical tabs sidebar docked on the left.
/// Supports favicons, colored tab groups, sleep/audio states, and middle/close actions.
/// </summary>
public sealed class VerticalTabBarControl : Control
{
    private readonly List<ModernTabItem> _items = new();
    private readonly List<RectangleF> _rowRects = new();
    private readonly List<RectangleF> _closeRects = new();
    private RectangleF _newTabRect;
    private RectangleF _collapseRect;
    private int _hoverRow = -1;
    private int _hoverClose = -1;
    private bool _hoverNew;
    private bool _hoverCollapse;
    private bool _collapsed;
    private int _scrollY;

    public ThemePalette Palette { get; private set; } = ThemeService.Dark;
    public bool IsCollapsed => _collapsed;

    public event Action<string>? TabSelected;
    public event Action<string>? TabClosed;
    public event Action<string, Point>? TabRightClicked;
    public event Action? NewTabClicked;
    public event Action? HorizontalTabsRequested;

    public VerticalTabBarControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Left;
        Width = 224;
        Font = new Font("Segoe UI", 8.75f);
    }

    public void ApplyTheme(ThemePalette p)
    {
        Palette = p;
        Invalidate();
    }

    public void SetTabs(IEnumerable<ModernTabItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        RecalculateLayout();
        Invalidate();
    }

    public void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        float sc = GraphicsExt.DpiScale(this);
        Width = (int)Math.Round((_collapsed ? 52 : 224) * sc);
        RecalculateLayout();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scrollY = Math.Max(0, _scrollY - e.Delta / 3);
        RecalculateLayout();
        Invalidate();
        base.OnMouseWheel(e);
    }

    private void RecalculateLayout()
    {
        _rowRects.Clear();
        _closeRects.Clear();

        float sc = GraphicsExt.DpiScale(this);
        float pad = 8f * sc;
        float headerH = 38f * sc;

        _collapseRect = new RectangleF(pad, 6f * sc, 28f * sc, 26f * sc);
        _newTabRect = _collapsed
            ? new RectangleF(pad, headerH + 2f * sc, Width - pad * 2, 30f * sc)
            : new RectangleF(pad + 34f * sc, 6f * sc, Math.Max(40f, Width - pad * 2 - 34f * sc), 26f * sc);

        float y = (_collapsed ? headerH + 38f * sc : headerH + 6f * sc) - _scrollY;
        float rowH = 34f * sc;
        float rowW = Math.Max(28f, Width - pad * 2);

        foreach (var it in _items)
        {
            var r = new RectangleF(pad, y, rowW, rowH);
            _rowRects.Add(r);
            float cSz = 18f * sc;
            _closeRects.Add((_collapsed || it.Pinned)
                ? RectangleF.Empty
                : new RectangleF(r.Right - cSz - 6f * sc, r.Y + (r.Height - cSz) / 2f, cSz, cSz));
            y += rowH + 4f * sc;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int newRow = -1, newClose = -1;
        for (int i = 0; i < _rowRects.Count; i++)
        {
            if (!_closeRects[i].IsEmpty && _closeRects[i].Contains(e.Location)) { newRow = i; newClose = i; break; }
            if (_rowRects[i].Contains(e.Location)) { newRow = i; break; }
        }
        bool newPlus = _newTabRect.Contains(e.Location);
        bool newCol = _collapseRect.Contains(e.Location);

        if (newRow != _hoverRow || newClose != _hoverClose || newPlus != _hoverNew || newCol != _hoverCollapse)
        {
            _hoverRow = newRow;
            _hoverClose = newClose;
            _hoverNew = newPlus;
            _hoverCollapse = newCol;
            Cursor = (newRow >= 0 || newPlus || newCol) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverRow = -1;
        _hoverClose = -1;
        _hoverNew = false;
        _hoverCollapse = false;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _collapseRect.Contains(e.Location))
        {
            ToggleCollapsed();
            return;
        }
        if (e.Button == MouseButtons.Right && _collapseRect.Contains(e.Location))
        {
            HorizontalTabsRequested?.Invoke();
            return;
        }
        if (e.Button == MouseButtons.Left && _newTabRect.Contains(e.Location))
        {
            NewTabClicked?.Invoke();
            return;
        }

        for (int i = 0; i < _rowRects.Count; i++)
        {
            if (!_rowRects[i].Contains(e.Location)) continue;
            var item = _items[i];
            if ((e.Button == MouseButtons.Middle && !item.Pinned) ||
                (e.Button == MouseButtons.Left && !_closeRects[i].IsEmpty && _closeRects[i].Contains(e.Location)))
                TabClosed?.Invoke(item.Id);
            else if (e.Button == MouseButtons.Left)
                TabSelected?.Invoke(item.Id);
            else if (e.Button == MouseButtons.Right)
                TabRightClicked?.Invoke(item.Id, PointToScreen(e.Location));
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(Palette.SurfaceBg))
            g.FillRectangle(bg, ClientRectangle);
        using (var sep = new Pen(Palette.Border, 1f))
            g.DrawLine(sep, Width - 1, 0, Width - 1, Height);

        float sc = GraphicsExt.DpiScale(this);

        // Collapse button
        using (var cgp = GraphicsExt.CreateRoundedRect(_collapseRect, 6f * sc))
        {
            using var cbr = new SolidBrush(_hoverCollapse ? Palette.HoverBg : Palette.ElevatedBg);
            g.FillPath(cbr, cgp);
            IconPainter.Draw(g, VectorIcon.VerticalTabs,
                new RectangleF(_collapseRect.X + 6f * sc, _collapseRect.Y + 5f * sc, 16f * sc, 16f * sc),
                _hoverCollapse ? Palette.Accent : Palette.TextSecondary, 1.5f);
        }

        // + Yeni Sekme button
        using (var ngp = GraphicsExt.CreateRoundedRect(_newTabRect, 7f * sc))
        {
            using var nbr = new SolidBrush(_hoverNew ? Palette.HoverBg : Palette.ElevatedBg);
            g.FillPath(nbr, ngp);
            using var npen = new Pen(Palette.Border, 1f);
            g.DrawPath(npen, ngp);
            if (_collapsed)
            {
                IconPainter.Draw(g, VectorIcon.Plus,
                    new RectangleF(_newTabRect.X + (_newTabRect.Width - 14f * sc) / 2f, _newTabRect.Y + 8f * sc, 14f * sc, 14f * sc),
                    Palette.Accent, 1.6f);
            }
            else
            {
                IconPainter.Draw(g, VectorIcon.Plus,
                    new RectangleF(_newTabRect.X + 8f * sc, _newTabRect.Y + 6f * sc, 13f * sc, 13f * sc),
                    Palette.Accent, 1.6f);
                TextRenderer.DrawText(g, "Yeni Sekme", Font,
                    new Rectangle((int)(_newTabRect.X + 24f * sc), (int)_newTabRect.Y, (int)(_newTabRect.Width - 28f * sc), (int)_newTabRect.Height),
                    Palette.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var r = _rowRects[i];
            if (r.Bottom < 36f * sc || r.Top > Height) continue;
            var it = _items[i];

            using var gp = GraphicsExt.CreateRoundedRect(r, 8f * sc);
            Color fill = it.Active ? Palette.ElevatedBg : (_hoverRow == i ? Palette.HoverBg : Color.Transparent);
            if (fill.A > 0)
            {
                using var br = new SolidBrush(fill);
                g.FillPath(br, gp);
            }

            if (it.Active || !it.GroupColor.IsEmpty)
            {
                var barColor = !it.GroupColor.IsEmpty ? it.GroupColor : Palette.Accent;
                using var barBr = new SolidBrush(barColor);
                g.FillRectangle(barBr, r.X + 2f * sc, r.Y + 6f * sc, 3f * sc, r.Height - 12f * sc);
            }

            float iconSz = 15f * sc;
            var iconRect = _collapsed
                ? new RectangleF(r.X + (r.Width - iconSz) / 2f, r.Y + (r.Height - iconSz) / 2f, iconSz, iconSz)
                : new RectangleF(r.X + 9f * sc, r.Y + (r.Height - iconSz) / 2f, iconSz, iconSz);

            if (!it.Loading && !it.Sleeping && !it.DeepUnloaded && it.Favicon is not null)
            {
                g.DrawImage(it.Favicon, iconRect);
            }
            else
            {
                var glyph = it.Loading ? VectorIcon.Reload : (it.Sleeping || it.DeepUnloaded) ? VectorIcon.Sleep : VectorIcon.Globe;
                var gc = it.Loading ? Palette.Accent : (it.Sleeping || it.DeepUnloaded) ? Palette.AccentPurple : (it.Active ? Palette.Accent : Palette.TextMuted);
                IconPainter.Draw(g, glyph, iconRect, gc, 1.45f);
            }

            if (_collapsed) continue;

            float rightStop = _closeRects[i].IsEmpty ? r.Right - 6f * sc : _closeRects[i].X - 4f * sc;
            if (!string.IsNullOrEmpty(it.GroupName))
            {
                var grpTagW = Math.Min(64, TextRenderer.MeasureText(it.GroupName, Font).Width + 10);
                var grpRect = new Rectangle((int)(rightStop - grpTagW), (int)(r.Y + 7f * sc), grpTagW, (int)(r.Height - 14f * sc));
                using var tagGp = GraphicsExt.CreateRoundedRect(grpRect, 5f * sc);
                var gc = it.GroupColor.IsEmpty ? Palette.Accent : it.GroupColor;
                using var tagBr = new SolidBrush(Color.FromArgb(40, gc));
                g.FillPath(tagBr, tagGp);
                TextRenderer.DrawText(g, it.GroupName, new Font(Font.FontFamily, 7.25f, FontStyle.Bold), grpRect, gc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                rightStop = grpRect.Left - 4f * sc;
            }

            var txtRect = new Rectangle((int)(iconRect.Right + 7f * sc), (int)r.Y, Math.Max(8, (int)(rightStop - iconRect.Right - 8f * sc)), (int)r.Height);
            using var tf = new Font(Font.FontFamily, Font.Size, it.Active ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(g, it.Title, tf, txtRect,
                it.Active ? Palette.TextPrimary : Palette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (!_closeRects[i].IsEmpty && (_hoverRow == i || it.Active))
            {
                var cr = _closeRects[i];
                if (_hoverClose == i)
                {
                    using var cbg = new SolidBrush(Color.FromArgb(65, Palette.Red));
                    g.FillEllipse(cbg, cr);
                }
                float cpad = 4.2f * sc;
                IconPainter.Draw(g, VectorIcon.Close, new RectangleF(cr.X + cpad, cr.Y + cpad, cr.Width - cpad * 2, cr.Height - cpad * 2),
                    _hoverClose == i ? Palette.Red : Palette.TextMuted, 1.4f);
            }
        }
    }
}
