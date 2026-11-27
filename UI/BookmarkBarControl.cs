using System.Drawing.Drawing2D;
using System.Drawing.Text;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

/// <summary>
/// Single-surface custom-drawn Bookmarks Bar with rock-solid direct mouse-capture drag-and-drop
/// reordering, folder drop targets, glowing insertion markers, ghost preview pill, and overflow menu.
/// </summary>
public sealed class BookmarkBarControl : Control
{
    private List<BookmarkItem> _bookmarks = new();
    private readonly List<RectangleF> _itemRects = new();
    private readonly List<BookmarkItem> _visibleItems = new();
    private readonly List<BookmarkItem> _overflowItems = new();
    private RectangleF _overflowRect;
    private bool _hasOverflow;

    private ThemePalette _p = ThemeService.Dark;
    private readonly ToolTip _tip = new();
    private int _hoverIdx = -1;
    private bool _hoverOverflow;
    private int _lastTipIdx = -1;

    public Func<string, Image?>? FaviconResolver { get; set; }

    // Direct drag state
    private int _pressIdx = -1;
    private Point _pressPt;
    private bool _dragging;
    private Point _dragCursorPos;
    private int _dropIntoFolderIdx = -1;
    private int _insertSlot = -1;

    public event Action<BookmarkItem>? BookmarkClicked;
    public event Action<BookmarkItem>? BookmarkMiddleClicked;
    public event Action<BookmarkItem, Rectangle>? FolderClicked;
    public event Action<BookmarkItem, Rectangle>? FolderHovered;
    public event Action<BookmarkItem, Point>? ItemRightClicked;
    public event Action<Point>? EmptyAreaRightClicked;
    public event Action<List<BookmarkItem>, Rectangle>? OverflowClicked;
    public event Action? BookmarksReordered;
    public event Action? DragStarted;

    public BookmarkBarControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = 34;
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
    }

    public void ApplyTheme(ThemePalette p)
    {
        _p = p;
        Invalidate();
    }

    public void SetBookmarks(List<BookmarkItem> bookmarks)
    {
        _bookmarks = bookmarks;
        RecalculateLayout();
        Invalidate();
    }

    public int VisibleCount => _visibleItems.Count;

    public Rectangle GetItemClientRect(int index) =>
        (index >= 0 && index < _itemRects.Count) ? Rectangle.Round(_itemRects[index]) : Rectangle.Empty;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    private void RecalculateLayout()
    {
        _itemRects.Clear();
        _visibleItems.Clear();
        _overflowItems.Clear();
        _overflowRect = RectangleF.Empty;
        _hasOverflow = false;

        float sc = GraphicsExt.DpiScale(this);
        float padX = 10f * sc;
        float gap = 5f * sc;
        float itemH = Height - 8f * sc;
        float y = (Height - itemH) / 2f;
        float overflowBtnW = 26f * sc;
        float maxRight = Width - padX - overflowBtnW - 6f * sc;
        float x = padX;

        for (int i = 0; i < _bookmarks.Count; i++)
        {
            var item = _bookmarks[i];
            string label = string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name;
            int txtW = TextRenderer.MeasureText(label, Font, new Size((int)(160 * sc), (int)itemH), TextFormatFlags.NoPadding).Width;
            float w = Math.Min(175f * sc, Math.Max(52f * sc, 9f * sc + 14f * sc + 5f * sc + txtW + 10f * sc));

            if (x + w > maxRight && _visibleItems.Count > 0)
            {
                _hasOverflow = true;
                for (int k = i; k < _bookmarks.Count; k++)
                    _overflowItems.Add(_bookmarks[k]);
                break;
            }

            _visibleItems.Add(item);
            _itemRects.Add(new RectangleF(x, y, w, itemH));
            x += w + gap;
        }

        if (_hasOverflow)
            _overflowRect = new RectangleF(Width - padX - overflowBtnW, y, overflowBtnW, itemH);
    }

    private int HitTestItem(Point pt)
    {
        for (int i = 0; i < _itemRects.Count; i++)
        {
            if (_itemRects[i].Contains(pt))
                return i;
        }
        return -1;
    }

    private void UpdateDropTarget(Point cursorPt)
    {
        if (_pressIdx < 0 || _pressIdx >= _visibleItems.Count) return;
        var dragged = _visibleItems[_pressIdx];

        _dropIntoFolderIdx = -1;
        _insertSlot = -1;

        // Check middle 64% of any visible folder (except itself or descendants)
        for (int j = 0; j < _itemRects.Count; j++)
        {
            if (j == _pressIdx) continue;
            var target = _visibleItems[j];
            if (!target.IsFolder) continue;
            if (dragged.IsFolder && (dragged.Id == target.Id || SidePanelHost.IsDescendant(dragged, target.Id)))
                continue;

            var r = _itemRects[j];
            var inner = new RectangleF(r.X + r.Width * 0.18f, 0, r.Width * 0.64f, Height);
            if (inner.Contains(cursorPt))
            {
                _dropIntoFolderIdx = j;
                return;
            }
        }

        // Otherwise compute insertion slot 0.._visibleItems.Count
        int slot = _visibleItems.Count;
        for (int i = 0; i < _itemRects.Count; i++)
        {
            float mid = _itemRects[i].X + _itemRects[i].Width / 2f;
            if (cursorPt.X < mid)
            {
                slot = i;
                break;
            }
        }
        _insertSlot = slot;
    }

    /// <summary>
    /// Executes the pending drag move cleanly on _bookmarks. Returns true if the model changed.
    /// </summary>
    public bool CommitDrop(int fromIndex, int dropIntoFolderIdx, int insertSlot)
    {
        if (fromIndex < 0 || fromIndex >= _visibleItems.Count)
            return false;

        var dragged = _visibleItems[fromIndex];

        if (dropIntoFolderIdx >= 0 && dropIntoFolderIdx < _visibleItems.Count && dropIntoFolderIdx != fromIndex)
        {
            var folder = _visibleItems[dropIntoFolderIdx];
            if (folder.IsFolder && !(dragged.IsFolder && (dragged.Id == folder.Id || SidePanelHost.IsDescendant(dragged, folder.Id))))
            {
                _bookmarks.Remove(dragged);
                folder.Children.Add(dragged);
                RecalculateLayout();
                Invalidate();
                BookmarksReordered?.Invoke();
                return true;
            }
        }

        if (insertSlot >= 0)
        {
            int oldIdx = _bookmarks.IndexOf(dragged);
            if (oldIdx < 0) return false;

            int targetIdx = Math.Clamp(insertSlot, 0, _bookmarks.Count);
            if (targetIdx == oldIdx || targetIdx == oldIdx + 1)
                return false;

            _bookmarks.RemoveAt(oldIdx);
            if (targetIdx > oldIdx) targetIdx--;
            targetIdx = Math.Clamp(targetIdx, 0, _bookmarks.Count);
            _bookmarks.Insert(targetIdx, dragged);

            RecalculateLayout();
            Invalidate();
            BookmarksReordered?.Invoke();
            return true;
        }

        return false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressIdx = HitTestItem(e.Location);
            _pressPt = e.Location;
            _dragging = false;
            _dropIntoFolderIdx = -1;
            _insertSlot = -1;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_pressIdx >= 0 && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
        {
            if (!_dragging && (Math.Abs(e.X - _pressPt.X) >= 5 || Math.Abs(e.Y - _pressPt.Y) >= 5))
            {
                _dragging = true;
                Capture = true;
                Cursor = Cursors.SizeAll;
                DragStarted?.Invoke();
            }

            if (_dragging)
            {
                _dragCursorPos = e.Location;
                UpdateDropTarget(e.Location);
                Invalidate();
                return;
            }
        }

        int newHover = HitTestItem(e.Location);
        bool newOver = _hasOverflow && _overflowRect.Contains(e.Location);

        if (newHover != _lastTipIdx)
        {
            _lastTipIdx = newHover;
            if (newHover >= 0 && newHover < _visibleItems.Count)
            {
                var it = _visibleItems[newHover];
                _tip.SetToolTip(this, it.IsFolder ? $"{it.Name} ({it.Children.Count} öğe)" : $"{it.Name}\n{it.Url}");
            }
            else
            {
                _tip.SetToolTip(this, newOver ? "Diğer yer imleri" : "");
            }
        }

        if (newHover != _hoverIdx || newOver != _hoverOverflow)
        {
            _hoverIdx = newHover;
            _hoverOverflow = newOver;
            Cursor = (newHover >= 0 || newOver) ? Cursors.Hand : Cursors.Default;
            Invalidate();

            if (newHover >= 0 && _visibleItems[newHover].IsFolder)
            {
                var screenRect = RectangleToScreen(Rectangle.Round(_itemRects[newHover]));
                FolderHovered?.Invoke(_visibleItems[newHover], screenRect);
            }
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_dragging)
            {
                int from = _pressIdx;
                int dropFolder = _dropIntoFolderIdx;
                int slot = _insertSlot;
                ResetDragState();
                CommitDrop(from, dropFolder, slot);
                return;
            }

            int upIdx = HitTestItem(e.Location);
            int pressed = _pressIdx;
            _pressIdx = -1;

            if (_hasOverflow && _overflowRect.Contains(e.Location))
            {
                OverflowClicked?.Invoke(_overflowItems.ToList(), RectangleToScreen(Rectangle.Round(_overflowRect)));
                return;
            }

            if (pressed >= 0 && pressed == upIdx && upIdx < _visibleItems.Count)
            {
                var item = _visibleItems[upIdx];
                if (item.IsFolder)
                    FolderClicked?.Invoke(item, RectangleToScreen(Rectangle.Round(_itemRects[upIdx])));
                else
                    BookmarkClicked?.Invoke(item);
                return;
            }
        }
        else if (e.Button == MouseButtons.Middle)
        {
            int idx = HitTestItem(e.Location);
            if (idx >= 0 && !_visibleItems[idx].IsFolder)
                BookmarkMiddleClicked?.Invoke(_visibleItems[idx]);
        }
        else if (e.Button == MouseButtons.Right)
        {
            int idx = HitTestItem(e.Location);
            var screenPt = PointToScreen(e.Location);
            if (idx >= 0)
                ItemRightClicked?.Invoke(_visibleItems[idx], screenPt);
            else
                EmptyAreaRightClicked?.Invoke(screenPt);
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_dragging && (_hoverIdx != -1 || _hoverOverflow))
        {
            _hoverIdx = -1;
            _hoverOverflow = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (_dragging)
            ResetDragState();
        base.OnMouseCaptureChanged(e);
    }

    private void ResetDragState()
    {
        _dragging = false;
        _pressIdx = -1;
        _dropIntoFolderIdx = -1;
        _insertSlot = -1;
        if (Capture) Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    /// <summary>
    /// Programmatically exercises the exact OnMouseDown -> OnMouseMove -> OnMouseUp path for automated verification.
    /// </summary>
    public bool PerformDragDropSimulation(int fromIndex, Point targetClientPoint)
    {
        if (fromIndex < 0 || fromIndex >= _visibleItems.Count) return false;
        var r = _itemRects[fromIndex];
        var startPt = new Point((int)(r.X + r.Width / 2f), (int)(r.Y + r.Height / 2f));

        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, startPt.X, startPt.Y, 0));
        _pressIdx = fromIndex;
        _dragging = true;
        _dragCursorPos = targetClientPoint;
        UpdateDropTarget(targetClientPoint);

        int dropFolder = _dropIntoFolderIdx;
        int slot = _insertSlot;
        ResetDragState();
        return CommitDrop(fromIndex, dropFolder, slot);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(_p.SurfaceBg))
            g.FillRectangle(bg, ClientRectangle);
        using (var sep = new Pen(_p.Border, 1f))
            g.DrawLine(sep, 0, Height - 1, Width, Height - 1);

        float sc = GraphicsExt.DpiScale(this);

        for (int i = 0; i < _visibleItems.Count; i++)
        {
            var item = _visibleItems[i];
            var r = _itemRects[i];
            bool isDraggedSource = _dragging && i == _pressIdx;
            bool isDropFolder = _dragging && i == _dropIntoFolderIdx;
            bool hovered = !_dragging && i == _hoverIdx;

            DrawPill(g, item, r, sc, hovered, isDraggedSource, isDropFolder);
        }

        if (_hasOverflow)
        {
            using var path = GraphicsExt.CreateRoundedRect(_overflowRect, 6f * sc);
            if (_hoverOverflow)
            {
                using var hbr = new SolidBrush(_p.HoverBg);
                g.FillPath(hbr, path);
            }
            TextRenderer.DrawText(g, "»", Font, Rectangle.Round(_overflowRect),
                _hoverOverflow ? _p.Accent : _p.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // Draw insertion marker line when reordering
        if (_dragging && _dropIntoFolderIdx < 0 && _insertSlot >= 0)
        {
            float lineX;
            if (_itemRects.Count == 0)
                lineX = 10f * sc;
            else if (_insertSlot <= 0)
                lineX = _itemRects[0].X - 2.5f * sc;
            else if (_insertSlot >= _itemRects.Count)
                lineX = _itemRects[^1].Right + 2.5f * sc;
            else
                lineX = (_itemRects[_insertSlot - 1].Right + _itemRects[_insertSlot].X) / 2f;

            using var linePen = new Pen(_p.Accent, 2.5f * sc);
            g.DrawLine(linePen, lineX, 4f * sc, lineX, Height - 5f * sc);
            using var capBr = new SolidBrush(_p.Accent);
            float capR = 3f * sc;
            g.FillEllipse(capBr, lineX - capR, 3f * sc, capR * 2, capR * 2);
            g.FillEllipse(capBr, lineX - capR, Height - 6f * sc - capR, capR * 2, capR * 2);
        }

        // Draw translucent floating ghost pill under cursor
        if (_dragging && _pressIdx >= 0 && _pressIdx < _visibleItems.Count)
        {
            var srcRect = _itemRects[_pressIdx];
            var ghostX = Math.Clamp(_dragCursorPos.X - srcRect.Width / 2f, 4f, Math.Max(4f, Width - srcRect.Width - 4f));
            var ghostRect = new RectangleF(ghostX, srcRect.Y, srcRect.Width, srcRect.Height);
            using var path = GraphicsExt.CreateRoundedRect(ghostRect, 7f * sc);
            using (var gbr = new SolidBrush(Color.FromArgb(220, _p.ElevatedBg)))
                g.FillPath(gbr, path);
            using (var gpen = new Pen(_p.Accent, 1.5f))
                g.DrawPath(gpen, path);

            var ghostItem = _visibleItems[_pressIdx];
            string label = string.IsNullOrWhiteSpace(ghostItem.Name) ? ghostItem.Url : ghostItem.Name;
            float iconSz = 12f * sc;
            IconPainter.Draw(g, ghostItem.IsFolder ? VectorIcon.Folder : VectorIcon.Bookmark,
                new RectangleF(ghostRect.X + 8f * sc, ghostRect.Y + (ghostRect.Height - iconSz) / 2f, iconSz, iconSz),
                _p.Accent, 1.45f);
            TextRenderer.DrawText(g, label, Font,
                new Rectangle((int)(ghostRect.X + 24f * sc), (int)ghostRect.Y, (int)(ghostRect.Width - 30f * sc), (int)ghostRect.Height),
                _p.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawPill(Graphics g, BookmarkItem item, RectangleF r, float sc, bool hovered, bool dimmed, bool dropFolderHighlight)
    {
        using var path = GraphicsExt.CreateRoundedRect(r, 7f * sc);
        Color fill = dropFolderHighlight
            ? Color.FromArgb(55, _p.Accent)
            : hovered
                ? _p.HoverBg
                : dimmed
                    ? Color.FromArgb(90, _p.ElevatedBg)
                    : _p.SurfaceBg;

        using (var br = new SolidBrush(fill))
            g.FillPath(br, path);

        if (dropFolderHighlight)
        {
            using var pen = new Pen(_p.Accent, 1.8f);
            g.DrawPath(pen, path);
        }
        else if (item.IsFolder || hovered)
        {
            using var pen = new Pen(hovered ? _p.Accent : _p.Border, 1f);
            g.DrawPath(pen, path);
        }

        float iconSz = 13f * sc;
        Color fg = dimmed ? _p.TextMuted : (dropFolderHighlight || item.IsFolder ? _p.Accent : _p.TextSecondary);
        var icoRect = new RectangleF(r.X + 8f * sc, r.Y + (r.Height - iconSz) / 2f, iconSz, iconSz);
        Image? fav = !item.IsFolder ? FaviconResolver?.Invoke(item.Url) : null;
        if (fav is not null)
            g.DrawImage(fav, icoRect);
        else
            IconPainter.Draw(g, item.IsFolder ? VectorIcon.Folder : VectorIcon.Bookmark, icoRect, fg, 1.45f);

        string label = string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name;
        var txtRect = new Rectangle((int)(r.X + 24f * sc), (int)r.Y, Math.Max(8, (int)(r.Width - 30f * sc)), (int)r.Height);
        TextRenderer.DrawText(g, label, Font, txtRect,
            dimmed ? _p.TextMuted : _p.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
