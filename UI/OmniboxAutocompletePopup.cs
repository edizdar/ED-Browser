using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using EDBrowser.Services;

namespace EDBrowser.UI;

public sealed record AutocompleteSuggestion(string Title, string Url, string Kind);

/// <summary>
/// Non-activating Chrome-style omnibox dropdown merging Bookmarks, History,
/// direct URL / Search, and live search suggestions.
/// </summary>
public sealed class OmniboxAutocompletePopup : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int CS_DROPSHADOW = 0x00020000;

    private static readonly HttpClient SuggestHttp = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly ThemePalette _p;
    private readonly float _sc;
    private readonly List<AutocompleteSuggestion> _items = new();
    private readonly List<Rectangle> _rowRects = new();
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private CancellationTokenSource? _suggestCts;

    public string? SelectedUrl =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Url : null;
    public int ItemCount => _items.Count;

    public event Action<string>? SuggestionChosen;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    public OmniboxAutocompletePopup(ThemePalette palette)
    {
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

        _sc = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = 2;
            _ = DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    public void UpdateSuggestions(string query, BrowserContext ctx, Point screenAnchor, int widthPx, bool fetchWebSuggest = true)
    {
        _suggestCts?.Cancel();
        _suggestCts = new CancellationTokenSource();
        var ct = _suggestCts.Token;

        var q = (query ?? "").Trim();
        var local = BuildLocalSuggestions(q, ctx);
        ApplySuggestions(local, screenAnchor, widthPx);

        if (fetchWebSuggest && q.Length >= 2 && !q.Contains("://", StringComparison.Ordinal))
        {
            _ = FetchAndMergeWebSuggestionsAsync(q, local, ctx, screenAnchor, widthPx, ct);
        }
    }

    public static List<AutocompleteSuggestion> BuildLocalSuggestions(string q, BrowserContext ctx)
    {
        var list = new List<AutocompleteSuggestion>();
        if (string.IsNullOrWhiteSpace(q)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Matching Bookmarks
        void ScanBookmarks(IEnumerable<Models.BookmarkItem> bms)
        {
            foreach (var b in bms)
            {
                if (b.IsFolder) { ScanBookmarks(b.Children); continue; }
                if (list.Count >= 4) break;
                if ((b.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                     b.Url.Contains(q, StringComparison.OrdinalIgnoreCase)) && seen.Add(b.Url))
                {
                    list.Add(new AutocompleteSuggestion(string.IsNullOrWhiteSpace(b.Name) ? b.Url : b.Name, b.Url, "Yer İmi"));
                }
            }
        }
        ScanBookmarks(ctx.Bookmarks);

        // 2. Matching History
        foreach (var h in ctx.History)
        {
            if (list.Count >= 7) break;
            if ((h.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 h.Url.Contains(q, StringComparison.OrdinalIgnoreCase)) && seen.Add(h.Url))
            {
                list.Add(new AutocompleteSuggestion(string.IsNullOrWhiteSpace(h.Title) ? h.Url : h.Title, h.Url, "Geçmiş"));
            }
        }

        // 3. Direct URL or Search fallback
        var normalized = ctx.NormalizeUrlOrSearch(q);
        if (seen.Add(normalized))
        {
            string kind = q.Contains('.') && !q.Contains(' ') ? "Siteye Git" : "Web Araması";
            list.Add(new AutocompleteSuggestion(q, normalized, kind));
        }

        return list;
    }

    private async Task FetchAndMergeWebSuggestionsAsync(
        string q,
        List<AutocompleteSuggestion> local,
        BrowserContext ctx,
        Point screenAnchor,
        int widthPx,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(160, ct).ConfigureAwait(false);
            var url = $"https://suggestqueries.google.com/complete/search?client=firefox&q={Uri.EscapeDataString(q)}";
            var json = await SuggestHttp.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 2)
                return;

            var merged = new List<AutocompleteSuggestion>(local);
            var seenTitles = new HashSet<string>(merged.Select(m => m.Title), StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement[1].EnumerateArray())
            {
                if (merged.Count >= 9) break;
                var term = item.GetString();
                if (!string.IsNullOrWhiteSpace(term) && seenTitles.Add(term))
                    merged.Add(new AutocompleteSuggestion(term, ctx.NormalizeUrlOrSearch(term), "Öneri"));
            }

            if (!ct.IsCancellationRequested && IsHandleCreated && !IsDisposed)
                BeginInvoke(() => ApplySuggestions(merged, screenAnchor, widthPx));
        }
        catch { }
    }

    private void ApplySuggestions(List<AutocompleteSuggestion> list, Point anchor, int widthPx)
    {
        _items.Clear();
        _items.AddRange(list.Take(9));
        _rowRects.Clear();
        _selectedIndex = -1;
        _hoverIndex = -1;

        if (_items.Count == 0)
        {
            Hide();
            return;
        }

        int rowH = (int)Math.Round(34 * _sc);
        int pad = (int)Math.Round(6 * _sc);
        int w = Math.Max((int)Math.Round(300 * _sc), widthPx);
        int h = pad * 2 + _items.Count * rowH;

        for (int i = 0; i < _items.Count; i++)
            _rowRects.Add(new Rectangle(pad, pad + i * rowH, w - pad * 2, rowH));

        Location = anchor;
        Size = new Size(w, h);
        if (!Visible) Show();
        Invalidate();
    }

    public void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        _selectedIndex = (_selectedIndex + delta + _items.Count) % _items.Count;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int nh = -1;
        for (int i = 0; i < _rowRects.Count; i++)
        {
            if (_rowRects[i].Contains(e.Location)) { nh = i; break; }
        }
        if (nh != _hoverIndex)
        {
            _hoverIndex = nh;
            Cursor = nh >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < _rowRects.Count; i++)
        {
            if (_rowRects[i].Contains(e.Location))
            {
                var url = _items[i].Url;
                Hide();
                SuggestionChosen?.Invoke(url);
                return;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(_p.SurfaceBg))
            g.FillRectangle(bg, ClientRectangle);
        using (var border = new Pen(_p.Border, 1.2f))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        int pad = (int)Math.Round(8 * _sc);
        int badgeW = (int)Math.Round(72 * _sc);

        for (int i = 0; i < _items.Count; i++)
        {
            var r = _rowRects[i];
            var it = _items[i];
            bool active = i == _selectedIndex || i == _hoverIndex;
            if (active)
            {
                using var gp = GraphicsExt.CreateRoundedRect(r, 6f * _sc);
                using var hbr = new SolidBrush(_p.HoverBg);
                g.FillPath(hbr, gp);
            }

            int icoSz = (int)Math.Round(13 * _sc);
            var ico = it.Kind == "Yer İmi" ? VectorIcon.StarFilled
                : it.Kind == "Geçmiş" ? VectorIcon.History
                : it.Kind == "Siteye Git" ? VectorIcon.Globe
                : VectorIcon.Search;
            IconPainter.Draw(g, ico, new RectangleF(r.X + pad, r.Y + (r.Height - icoSz) / 2f, icoSz, icoSz),
                it.Kind == "Yer İmi" ? Color.FromArgb(250, 204, 21) : _p.Accent, 1.45f);

            var badgeRect = new Rectangle(r.Right - badgeW - pad, r.Y, badgeW, r.Height);
            TextRenderer.DrawText(g, it.Kind, new Font("Segoe UI", 7.75f, FontStyle.Bold), badgeRect, _p.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int textX = r.X + pad + icoSz + (int)Math.Round(8 * _sc);
            int textW = Math.Max(40, badgeRect.Left - textX - 8);
            TextRenderer.DrawText(g, $"{it.Title}   —   {it.Url}", new Font("Segoe UI", 8.75f),
                new Rectangle(textX, r.Y, textW, r.Height),
                _p.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
