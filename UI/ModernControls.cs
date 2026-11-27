using System.Drawing.Drawing2D;
using System.Drawing.Text;
using EDBrowser.Services;

namespace EDBrowser.UI;

public enum VectorIcon
{
    None,
    Back,
    Forward,
    Reload,
    Stop,
    Home,
    Plus,
    Close,
    Lock,
    Warning,
    Globe,
    Shield,
    Star,
    StarFilled,
    Bolt,
    Key,
    Download,
    Bookmark,
    History,
    Camera,
    Incognito,
    Import,
    Code,
    Fullscreen,
    Settings,
    MoreDots,
    Folder,
    Search,
    Sleep,
    ZoomIn,
    ZoomOut,
    Sliders,
    Info,
    ArrowRight
}

public static class GraphicsExt
{
    public static GraphicsPath CreateRoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static float DpiScale(Control c) => (c.DeviceDpi > 0 ? c.DeviceDpi : 96f) / 96f;
    public static int Scale(Control c, int px) => (int)Math.Round(px * DpiScale(c));
}

public static class IconPainter
{
    public static void Draw(Graphics g, VectorIcon icon, RectangleF bounds, Color color, float strokeWidth = 1.65f)
    {
        if (icon == VectorIcon.None || bounds.Width < 4 || bounds.Height < 4)
            return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float s = Math.Min(bounds.Width, bounds.Height);
        float u = s / 16f;

        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (icon)
        {
            case VectorIcon.Back:
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + 1.5f * u, cy - 4.5f * u),
                    new PointF(cx - 3.5f * u, cy),
                    new PointF(cx + 1.5f * u, cy + 4.5f * u)
                });
                g.DrawLine(pen, cx - 3.2f * u, cy, cx + 4.5f * u, cy);
                break;

            case VectorIcon.Forward:
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 1.5f * u, cy - 4.5f * u),
                    new PointF(cx + 3.5f * u, cy),
                    new PointF(cx - 1.5f * u, cy + 4.5f * u)
                });
                g.DrawLine(pen, cx - 4.5f * u, cy, cx + 3.2f * u, cy);
                break;

            case VectorIcon.ArrowRight:
                g.DrawLine(pen, cx - 4f * u, cy, cx + 4f * u, cy);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + 0.5f * u, cy - 3.5f * u),
                    new PointF(cx + 4f * u, cy),
                    new PointF(cx + 0.5f * u, cy + 3.5f * u)
                });
                break;

            case VectorIcon.Reload:
                var arcRect = new RectangleF(cx - 4.8f * u, cy - 4.8f * u, 9.6f * u, 9.6f * u);
                g.DrawArc(pen, arcRect, -45, 290);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + 1.5f * u, cy - 5.2f * u),
                    new PointF(cx + 4.5f * u, cy - 4.4f * u),
                    new PointF(cx + 3.6f * u, cy - 1.4f * u)
                });
                break;

            case VectorIcon.Stop:
            case VectorIcon.Close:
                float d = 3.8f * u;
                g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                break;

            case VectorIcon.Plus:
                float p = 4.5f * u;
                g.DrawLine(pen, cx - p, cy, cx + p, cy);
                g.DrawLine(pen, cx, cy - p, cx, cy + p);
                break;

            case VectorIcon.Home:
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 5.2f * u, cy - 0.5f * u),
                    new PointF(cx, cy - 5.2f * u),
                    new PointF(cx + 5.2f * u, cy - 0.5f * u)
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 3.8f * u, cy - 0.5f * u),
                    new PointF(cx - 3.8f * u, cy + 4.8f * u),
                    new PointF(cx - 1.2f * u, cy + 4.8f * u),
                    new PointF(cx - 1.2f * u, cy + 1.5f * u),
                    new PointF(cx + 1.2f * u, cy + 1.5f * u),
                    new PointF(cx + 1.2f * u, cy + 4.8f * u),
                    new PointF(cx + 3.8f * u, cy + 4.8f * u),
                    new PointF(cx + 3.8f * u, cy - 0.5f * u)
                });
                break;

            case VectorIcon.Lock:
                var body = new RectangleF(cx - 4f * u, cy - 0.8f * u, 8f * u, 6f * u);
                using (var gp = GraphicsExt.CreateRoundedRect(body, 1.5f * u))
                    g.DrawPath(pen, gp);
                g.DrawArc(pen, cx - 2.6f * u, cy - 5.2f * u, 5.2f * u, 5.2f * u, 180, 180);
                g.DrawLine(pen, cx - 2.6f * u, cy - 2.6f * u, cx - 2.6f * u, cy - 0.8f * u);
                g.DrawLine(pen, cx + 2.6f * u, cy - 2.6f * u, cx + 2.6f * u, cy - 0.8f * u);
                break;

            case VectorIcon.Warning:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx, cy - 5.2f * u),
                    new PointF(cx + 5.2f * u, cy + 4.4f * u),
                    new PointF(cx - 5.2f * u, cy + 4.4f * u)
                });
                g.DrawLine(pen, cx, cy - 1.8f * u, cx, cy + 1.4f * u);
                g.FillEllipse(brush, cx - 0.8f * u, cy + 2.3f * u, 1.6f * u, 1.6f * u);
                break;

            case VectorIcon.Globe:
                g.DrawEllipse(pen, cx - 5.2f * u, cy - 5.2f * u, 10.4f * u, 10.4f * u);
                g.DrawEllipse(pen, cx - 2.4f * u, cy - 5.2f * u, 4.8f * u, 10.4f * u);
                g.DrawLine(pen, cx - 5.1f * u, cy, cx + 5.1f * u, cy);
                break;

            case VectorIcon.Shield:
                using (var gp = new GraphicsPath())
                {
                    gp.AddLines(new[]
                    {
                        new PointF(cx, cy - 5.5f * u),
                        new PointF(cx + 4.8f * u, cy - 3.6f * u),
                        new PointF(cx + 4.4f * u, cy + 1.2f * u),
                        new PointF(cx, cy + 5.5f * u),
                        new PointF(cx - 4.4f * u, cy + 1.2f * u),
                        new PointF(cx - 4.8f * u, cy - 3.6f * u)
                    });
                    gp.CloseFigure();
                    g.DrawPath(pen, gp);
                }
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 2f * u, cy),
                    new PointF(cx - 0.4f * u, cy + 1.8f * u),
                    new PointF(cx + 2.5f * u, cy - 1.8f * u)
                });
                break;

            case VectorIcon.Star:
            case VectorIcon.StarFilled:
                var pts = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double ang = -Math.PI / 2 + i * Math.PI / 5.0;
                    float r = (i % 2 == 0) ? 5.4f * u : 2.3f * u;
                    pts[i] = new PointF(cx + (float)(Math.Cos(ang) * r), cy + (float)(Math.Sin(ang) * r));
                }
                if (icon == VectorIcon.StarFilled)
                    g.FillPolygon(brush, pts);
                g.DrawPolygon(pen, pts);
                break;

            case VectorIcon.Bolt:
                var boltPts = new[]
                {
                    new PointF(cx + 1.2f * u, cy - 5.5f * u),
                    new PointF(cx - 3.8f * u, cy + 0.5f * u),
                    new PointF(cx - 0.3f * u, cy + 0.5f * u),
                    new PointF(cx - 1.4f * u, cy + 5.5f * u),
                    new PointF(cx + 3.8f * u, cy - 0.5f * u),
                    new PointF(cx + 0.3f * u, cy - 0.5f * u)
                };
                g.DrawPolygon(pen, boltPts);
                break;

            case VectorIcon.Key:
                g.DrawEllipse(pen, cx - 5.2f * u, cy - 2.6f * u, 5.2f * u, 5.2f * u);
                g.DrawLine(pen, cx, cy, cx + 5.4f * u, cy);
                g.DrawLine(pen, cx + 3.4f * u, cy, cx + 3.4f * u, cy + 2.3f * u);
                g.DrawLine(pen, cx + 5.4f * u, cy, cx + 5.4f * u, cy + 2.3f * u);
                break;

            case VectorIcon.Download:
                g.DrawLine(pen, cx, cy - 5f * u, cx, cy + 2.5f * u);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 2.8f * u, cy - 0.2f * u),
                    new PointF(cx, cy + 2.7f * u),
                    new PointF(cx + 2.8f * u, cy - 0.2f * u)
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 4.8f * u, cy + 2.8f * u),
                    new PointF(cx - 4.8f * u, cy + 5f * u),
                    new PointF(cx + 4.8f * u, cy + 5f * u),
                    new PointF(cx + 4.8f * u, cy + 2.8f * u)
                });
                break;

            case VectorIcon.Bookmark:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx - 3.6f * u, cy - 5f * u),
                    new PointF(cx + 3.6f * u, cy - 5f * u),
                    new PointF(cx + 3.6f * u, cy + 5.2f * u),
                    new PointF(cx, cy + 2.4f * u),
                    new PointF(cx - 3.6f * u, cy + 5.2f * u)
                });
                break;

            case VectorIcon.History:
                g.DrawEllipse(pen, cx - 5f * u, cy - 5f * u, 10f * u, 10f * u);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx, cy - 2.8f * u),
                    new PointF(cx, cy + 0.4f * u),
                    new PointF(cx + 2.4f * u, cy + 1.8f * u)
                });
                break;

            case VectorIcon.Camera:
                using (var gp = GraphicsExt.CreateRoundedRect(new RectangleF(cx - 5.2f * u, cy - 3.2f * u, 10.4f * u, 7.6f * u), 1.5f * u))
                    g.DrawPath(pen, gp);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 2.5f * u, cy - 3.2f * u),
                    new PointF(cx - 1.5f * u, cy - 5f * u),
                    new PointF(cx + 1.5f * u, cy - 5f * u),
                    new PointF(cx + 2.5f * u, cy - 3.2f * u)
                });
                g.DrawEllipse(pen, cx - 2.2f * u, cy - 1.5f * u, 4.4f * u, 4.4f * u);
                break;

            case VectorIcon.Incognito:
                g.DrawLine(pen, cx - 5.8f * u, cy + 0.2f * u, cx + 5.8f * u, cy + 0.2f * u);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 3.6f * u, cy + 0.2f * u),
                    new PointF(cx - 2.4f * u, cy - 4.6f * u),
                    new PointF(cx, cy - 3.8f * u),
                    new PointF(cx + 2.4f * u, cy - 4.6f * u),
                    new PointF(cx + 3.6f * u, cy + 0.2f * u)
                });
                g.DrawEllipse(pen, cx - 4.2f * u, cy + 1.5f * u, 3.3f * u, 3.3f * u);
                g.DrawEllipse(pen, cx + 0.9f * u, cy + 1.5f * u, 3.3f * u, 3.3f * u);
                g.DrawLine(pen, cx - 0.9f * u, cy + 3.1f * u, cx + 0.9f * u, cy + 3.1f * u);
                break;

            case VectorIcon.Import:
                using (var gp = GraphicsExt.CreateRoundedRect(new RectangleF(cx - 4.8f * u, cy - 4.8f * u, 9.6f * u, 9.6f * u), 1.6f * u))
                    g.DrawPath(pen, gp);
                g.DrawLine(pen, cx, cy - 2.6f * u, cx, cy + 2.6f * u);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 2.3f * u, cy + 0.4f * u),
                    new PointF(cx, cy + 2.7f * u),
                    new PointF(cx + 2.3f * u, cy + 0.4f * u)
                });
                break;

            case VectorIcon.Code:
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 2.4f * u, cy - 3f * u),
                    new PointF(cx - 5.4f * u, cy),
                    new PointF(cx - 2.4f * u, cy + 3f * u)
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + 2.4f * u, cy - 3f * u),
                    new PointF(cx + 5.4f * u, cy),
                    new PointF(cx + 2.4f * u, cy + 3f * u)
                });
                g.DrawLine(pen, cx + 1f * u, cy - 4f * u, cx - 1f * u, cy + 4f * u);
                break;

            case VectorIcon.Fullscreen:
                float f = 4.8f * u;
                float leg = 2.6f * u;
                g.DrawLines(pen, new[] { new PointF(cx - f, cy - f + leg), new PointF(cx - f, cy - f), new PointF(cx - f + leg, cy - f) });
                g.DrawLines(pen, new[] { new PointF(cx + f - leg, cy - f), new PointF(cx + f, cy - f), new PointF(cx + f, cy - f + leg) });
                g.DrawLines(pen, new[] { new PointF(cx - f, cy + f - leg), new PointF(cx - f, cy + f), new PointF(cx - f + leg, cy + f) });
                g.DrawLines(pen, new[] { new PointF(cx + f - leg, cy + f), new PointF(cx + f, cy + f), new PointF(cx + f, cy + f - leg) });
                break;

            case VectorIcon.Settings:
                g.DrawEllipse(pen, cx - 2.2f * u, cy - 2.2f * u, 4.4f * u, 4.4f * u);
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4.0;
                    g.DrawLine(pen,
                        cx + (float)(Math.Cos(ang) * 3.8f * u),
                        cy + (float)(Math.Sin(ang) * 3.8f * u),
                        cx + (float)(Math.Cos(ang) * 5.4f * u),
                        cy + (float)(Math.Sin(ang) * 5.4f * u));
                }
                break;

            case VectorIcon.MoreDots:
                float dotR = 1.15f * u;
                g.FillEllipse(brush, cx - dotR, cy - 4.2f * u - dotR, dotR * 2, dotR * 2);
                g.FillEllipse(brush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
                g.FillEllipse(brush, cx - dotR, cy + 4.2f * u - dotR, dotR * 2, dotR * 2);
                break;

            case VectorIcon.Folder:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx - 5.2f * u, cy - 3.6f * u),
                    new PointF(cx - 1.2f * u, cy - 3.6f * u),
                    new PointF(cx + 0.3f * u, cy - 2f * u),
                    new PointF(cx + 5.2f * u, cy - 2f * u),
                    new PointF(cx + 5.2f * u, cy + 4f * u),
                    new PointF(cx - 5.2f * u, cy + 4f * u)
                });
                break;

            case VectorIcon.Search:
                g.DrawEllipse(pen, cx - 4.6f * u, cy - 4.6f * u, 7.2f * u, 7.2f * u);
                g.DrawLine(pen, cx + 1.6f * u, cy + 1.6f * u, cx + 5f * u, cy + 5f * u);
                break;

            case VectorIcon.Sleep:
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 3.5f * u, cy - 3.5f * u),
                    new PointF(cx + 0.5f * u, cy - 3.5f * u),
                    new PointF(cx - 3.5f * u, cy + 1f * u),
                    new PointF(cx + 0.8f * u, cy + 1f * u)
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + 1.5f * u, cy + 0.5f * u),
                    new PointF(cx + 4.5f * u, cy + 0.5f * u),
                    new PointF(cx + 1.5f * u, cy + 4f * u),
                    new PointF(cx + 4.8f * u, cy + 4f * u)
                });
                break;

            case VectorIcon.ZoomIn:
                g.DrawEllipse(pen, cx - 4.6f * u, cy - 4.6f * u, 7.2f * u, 7.2f * u);
                g.DrawLine(pen, cx + 1.6f * u, cy + 1.6f * u, cx + 5f * u, cy + 5f * u);
                g.DrawLine(pen, cx - 2.6f * u, cy - 1f * u, cx + 0.6f * u, cy - 1f * u);
                g.DrawLine(pen, cx - 1f * u, cy - 2.6f * u, cx - 1f * u, cy + 0.6f * u);
                break;

            case VectorIcon.ZoomOut:
                g.DrawEllipse(pen, cx - 4.6f * u, cy - 4.6f * u, 7.2f * u, 7.2f * u);
                g.DrawLine(pen, cx + 1.6f * u, cy + 1.6f * u, cx + 5f * u, cy + 5f * u);
                g.DrawLine(pen, cx - 2.6f * u, cy - 1f * u, cx + 0.6f * u, cy - 1f * u);
                break;

            case VectorIcon.Sliders:
                g.DrawLine(pen, cx - 5f * u, cy - 3.2f * u, cx + 5f * u, cy - 3.2f * u);
                g.DrawLine(pen, cx - 5f * u, cy, cx + 5f * u, cy);
                g.DrawLine(pen, cx - 5f * u, cy + 3.2f * u, cx + 5f * u, cy + 3.2f * u);
                g.FillEllipse(brush, cx - 2.8f * u, cy - 4.6f * u, 2.8f * u, 2.8f * u);
                g.FillEllipse(brush, cx + 0.8f * u, cy - 1.4f * u, 2.8f * u, 2.8f * u);
                g.FillEllipse(brush, cx - 1.6f * u, cy + 1.8f * u, 2.8f * u, 2.8f * u);
                break;

            case VectorIcon.Info:
                g.DrawEllipse(pen, cx - 5f * u, cy - 5f * u, 10f * u, 10f * u);
                g.FillEllipse(brush, cx - 0.9f * u, cy - 3.4f * u, 1.8f * u, 1.8f * u);
                g.DrawLine(pen, cx, cy - 0.5f * u, cx, cy + 3.2f * u);
                break;
        }
    }
}

public sealed class IconButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private VectorIcon _icon = VectorIcon.None;
    private string _labelText = string.Empty;
    private bool _pillBorder;
    private Color _normalBg = Color.Transparent;
    private Color _hoverBg = Color.FromArgb(40, 255, 255, 255);
    private Color _iconColor = Color.White;
    private Color _borderColor = Color.Transparent;
    private readonly ToolTip _toolTip = new();

    public VectorIcon IconType
    {
        get => _icon;
        set { _icon = value; Invalidate(); }
    }

    public string LabelText
    {
        get => _labelText;
        set
        {
            _labelText = value ?? string.Empty;
            UpdateAutoWidth();
            Invalidate();
        }
    }

    public bool PillBorder
    {
        get => _pillBorder;
        set { _pillBorder = value; Invalidate(); }
    }

    public bool AutoWidthFromText { get; set; }
    public int CornerRadius { get; set; } = 8;
    public int IconSizePx { get; set; } = 15;
    public bool IsActive { get; set; }

    public Color NormalBg
    {
        get => _normalBg;
        set { _normalBg = value; Invalidate(); }
    }

    public Color HoverBg
    {
        get => _hoverBg;
        set { _hoverBg = value; Invalidate(); }
    }

    public Color IconColor
    {
        get => _iconColor;
        set { _iconColor = value; Invalidate(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    public IconButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
        Size = new Size(32, 32);
    }

    public void SetTooltip(string tip) => _toolTip.SetToolTip(this, tip);

    public void UpdateAutoWidth(int maxWidth = 180)
    {
        if (!AutoWidthFromText) return;
        float sc = GraphicsExt.DpiScale(this);
        int padX = (int)(10 * sc);
        int iconW = _icon != VectorIcon.None ? (int)(18 * sc) : 0;
        int gap = (_icon != VectorIcon.None && !string.IsNullOrEmpty(_labelText)) ? (int)(5 * sc) : 0;
        int txtW = string.IsNullOrEmpty(_labelText)
            ? 0
            : TextRenderer.MeasureText(_labelText, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding).Width;
        int target = Math.Min((int)(maxWidth * sc), padX * 2 + iconW + gap + txtW + 4);
        Width = Math.Max((int)(32 * sc), target);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float sc = GraphicsExt.DpiScale(this);
        var rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        float rad = _pillBorder ? rect.Height / 2f : CornerRadius * sc;

        Color bg = _pressed
            ? ControlPaint.Dark(_hoverBg, 0.08f)
            : (_hovered || IsActive) ? _hoverBg : _normalBg;

        using (var path = GraphicsExt.CreateRoundedRect(rect, rad))
        {
            if (bg.A > 0 && bg != Color.Transparent)
            {
                using var bgBrush = new SolidBrush(bg);
                g.FillPath(bgBrush, path);
            }
            if (_borderColor.A > 0 && _borderColor != Color.Transparent)
            {
                using var pen = new Pen(_borderColor, 1f);
                g.DrawPath(pen, path);
            }
        }

        Color fg = Enabled ? _iconColor : Color.FromArgb(95, _iconColor);
        float iconSz = IconSizePx * sc;

        if (string.IsNullOrEmpty(_labelText))
        {
            var iconRect = new RectangleF(
                (Width - iconSz) / 2f,
                (Height - iconSz) / 2f,
                iconSz,
                iconSz);
            IconPainter.Draw(g, _icon, iconRect, fg, 1.65f * Math.Max(1f, sc * 0.9f));
        }
        else
        {
            float padX = 9f * sc;
            float x = padX;
            if (_icon != VectorIcon.None)
            {
                var iconRect = new RectangleF(x, (Height - iconSz) / 2f, iconSz, iconSz);
                IconPainter.Draw(g, _icon, iconRect, fg, 1.6f * Math.Max(1f, sc * 0.9f));
                x += iconSz + 5f * sc;
            }
            var txtRect = new Rectangle((int)x, 0, Math.Max(10, Width - (int)x - (int)(6f * sc)), Height);
            TextRenderer.DrawText(g, _labelText, Font, txtRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

public sealed class ModernToggleSwitch : Control
{
    private bool _checked;
    private bool _hovered;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Color OnColorStart { get; set; } = Color.FromArgb(56, 189, 248);
    public Color OnColorEnd { get; set; } = Color.FromArgb(129, 140, 248);
    public Color OffTrackColor { get; set; } = Color.FromArgb(55, 62, 86);
    public Color ThumbColor { get; set; } = Color.White;

    public event EventHandler? CheckedChanged;

    public ModernToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(44, 24);
        TabStop = true;
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float sc = GraphicsExt.DpiScale(this);
        float w = Math.Max(36f * sc, Width - 4f);
        float h = Math.Max(20f * sc, Math.Min(Height - 4f, w * 0.54f));
        var track = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);

        using (var path = GraphicsExt.CreateRoundedRect(track, h / 2f))
        {
            if (_checked)
            {
                using var br = new LinearGradientBrush(track, OnColorStart, OnColorEnd, 0f);
                g.FillPath(br, path);
            }
            else
            {
                using var br = new SolidBrush(_hovered ? ControlPaint.Light(OffTrackColor, 0.1f) : OffTrackColor);
                g.FillPath(br, path);
            }
        }

        float pad = 3f * sc;
        float thumbD = h - pad * 2f;
        float thumbX = _checked ? (track.Right - pad - thumbD) : (track.X + pad);
        var thumbRect = new RectangleF(thumbX, track.Y + pad, thumbD, thumbD);
        using var thumbBr = new SolidBrush(ThumbColor);
        g.FillEllipse(thumbBr, thumbRect);
    }
}

public sealed class ModernButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public bool Primary { get; set; }
    public Color AccentStart { get; set; } = Color.FromArgb(56, 189, 248);
    public Color AccentEnd { get; set; } = Color.FromArgb(129, 140, 248);
    public Color SecondaryBg { get; set; } = Color.FromArgb(32, 34, 58);
    public Color SecondaryHoverBg { get; set; } = Color.FromArgb(46, 50, 82);
    public Color SecondaryBorder { get; set; } = Color.FromArgb(62, 66, 98);
    public Color SecondaryText { get; set; } = Color.FromArgb(226, 232, 240);
    public DialogResult DialogResult { get; set; }

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        Size = new Size(120, 34);
        TabStop = true;
    }

    public void ApplyTheme(ThemePalette p)
    {
        AccentStart = p.Accent;
        AccentEnd = p.AccentPurple;
        SecondaryBg = p.ElevatedBg;
        SecondaryHoverBg = p.HoverBg;
        SecondaryBorder = p.Border;
        SecondaryText = p.TextPrimary;
        Invalidate();
    }

    public void FitWidthToText(int minWidth = 90, int padX = 32)
    {
        float sc = GraphicsExt.DpiScale(this);
        int w = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding).Width;
        Width = Math.Max((int)(minWidth * sc), w + (int)(padX * sc));
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is Form f)
        {
            f.DialogResult = DialogResult;
            f.Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float sc = GraphicsExt.DpiScale(this);
        var rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        float rad = 8f * sc;

        using (var path = GraphicsExt.CreateRoundedRect(rect, rad))
        {
            if (Primary)
            {
                Color c1 = _pressed ? ControlPaint.Dark(AccentStart, 0.1f) : _hovered ? ControlPaint.Light(AccentStart, 0.08f) : AccentStart;
                Color c2 = _pressed ? ControlPaint.Dark(AccentEnd, 0.1f) : _hovered ? ControlPaint.Light(AccentEnd, 0.08f) : AccentEnd;
                using var br = new LinearGradientBrush(rect, c1, c2, 0f);
                g.FillPath(br, path);
            }
            else
            {
                Color bg = _pressed ? ControlPaint.Dark(SecondaryHoverBg, 0.06f) : _hovered ? SecondaryHoverBg : SecondaryBg;
                using var br = new SolidBrush(bg);
                g.FillPath(br, path);
                using var pen = new Pen(_hovered ? AccentStart : SecondaryBorder, 1f);
                g.DrawPath(pen, path);
            }
        }

        Color textColor = Primary ? Color.FromArgb(10, 14, 24) : SecondaryText;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

public sealed class RoundedTextBox : Panel
{
    public TextBox Inner { get; }
    private bool _focused;

    public bool PillShape { get; set; }
    public Color FillColor { get; set; } = Color.FromArgb(12, 12, 20);
    public Color BorderColor { get; set; } = Color.FromArgb(52, 56, 86);
    public Color FocusBorderColor { get; set; } = Color.FromArgb(56, 189, 248);

    public new string Text
    {
        get => Inner.Text;
        set => Inner.Text = value;
    }

    public string PlaceholderText
    {
        get => Inner.PlaceholderText;
        set => Inner.PlaceholderText = value;
    }

    public bool UseSystemPasswordChar
    {
        get => Inner.UseSystemPasswordChar;
        set => Inner.UseSystemPasswordChar = value;
    }

    public RoundedTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 36;
        Padding = new Padding(12, 6, 12, 6);

        Inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.75f),
            BackColor = FillColor,
            ForeColor = Color.FromArgb(226, 232, 240)
        };
        Inner.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        Inner.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        Inner.TextChanged += (_, e) => OnTextChanged(e);
        Inner.KeyDown += (_, e) => OnKeyDown(e);

        Controls.Add(Inner);
        Resize += (_, _) => LayoutInner();
        FontChanged += (_, _) => { Inner.Font = Font; LayoutInner(); };
        Click += (_, _) => Inner.Focus();
    }

    public void ApplyTheme(ThemePalette p, bool elevated = false)
    {
        FillColor = elevated ? p.ElevatedBg : p.BaseBg;
        BorderColor = p.Border;
        FocusBorderColor = p.Accent;
        Inner.BackColor = FillColor;
        Inner.ForeColor = p.TextPrimary;
        Invalidate();
    }

    private void LayoutInner()
    {
        int padX = GraphicsExt.Scale(this, 12);
        int ih = Inner.PreferredHeight;
        Inner.Location = new Point(padX, Math.Max(2, (Height - ih) / 2));
        Inner.Width = Math.Max(40, Width - padX * 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float sc = GraphicsExt.DpiScale(this);
        var rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        float rad = PillShape ? rect.Height / 2f : 8f * sc;

        using var path = GraphicsExt.CreateRoundedRect(rect, rad);
        using (var fill = new SolidBrush(FillColor))
            g.FillPath(fill, path);

        if (_focused)
        {
            using var glow = new Pen(Color.FromArgb(65, FocusBorderColor), 3f);
            g.DrawPath(glow, path);
        }
        using var pen = new Pen(_focused ? FocusBorderColor : BorderColor, 1.2f);
        g.DrawPath(pen, path);
    }
}

public sealed class ModernCardPanel : Panel
{
    public Color FillColor { get; set; } = Color.FromArgb(22, 22, 40);
    public Color BorderColor { get; set; } = Color.FromArgb(44, 48, 74);

    public ModernCardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float sc = GraphicsExt.DpiScale(this);
        var rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        using var path = GraphicsExt.CreateRoundedRect(rect, 10f * sc);
        using (var br = new SolidBrush(FillColor))
            g.FillPath(br, path);
        using var pen = new Pen(BorderColor, 1f);
        g.DrawPath(pen, path);
    }
}

public sealed class ModernTabItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool Active { get; init; }
    public bool Loading { get; init; }
    public bool Sleeping { get; init; }
    public bool DeepUnloaded { get; init; }
}

public sealed class ModernTabBar : Control
{
    private readonly List<ModernTabItem> _items = new();
    private readonly List<RectangleF> _tabRects = new();
    private readonly List<RectangleF> _closeRects = new();
    private RectangleF _plusRect;
    private int _hoverTab = -1;
    private int _hoverClose = -1;
    private bool _hoverPlus;
    private readonly ToolTip _tip = new();
    private int _lastTipTab = -1;

    public bool IsIncognito { get; set; }
    public ThemePalette Palette { get; private set; } = ThemeService.Dark;

    public event Action<string>? TabSelected;
    public event Action<string>? TabClosed;
    public event Action<string, Point>? TabRightClicked;
    public event Action? NewTabClicked;

    public ModernTabBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = 40;
        Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    private void RecalculateLayout()
    {
        _tabRects.Clear();
        _closeRects.Clear();

        float sc = GraphicsExt.DpiScale(this);
        float leftPad = (IsIncognito ? 116f : 10f) * sc;
        float plusSize = 28f * sc;
        float rightReserve = plusSize + 18f * sc;
        float avail = Math.Max(80f, Width - leftPad - rightReserve);

        int count = _items.Count;
        float gap = 4f * sc;
        float tabW = count == 0
            ? 180f * sc
            : Math.Clamp((avail - gap * Math.Max(0, count - 1)) / count, 96f * sc, 215f * sc);
        float tabH = Height - 6f * sc;
        float y = 6f * sc;
        float x = leftPad;

        for (int i = 0; i < count; i++)
        {
            var r = new RectangleF(x, y, tabW, tabH);
            _tabRects.Add(r);
            float cSz = 18f * sc;
            _closeRects.Add(new RectangleF(r.Right - cSz - 6f * sc, r.Y + (r.Height - cSz) / 2f, cSz, cSz));
            x += tabW + gap;
        }

        _plusRect = new RectangleF(x + 2f * sc, y + (tabH - plusSize) / 2f, plusSize, plusSize);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int newTab = -1, newClose = -1;
        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_closeRects[i].Contains(e.Location)) { newTab = i; newClose = i; break; }
            if (_tabRects[i].Contains(e.Location)) { newTab = i; break; }
        }
        bool newPlus = _plusRect.Contains(e.Location);

        if (newTab != _lastTipTab)
        {
            _lastTipTab = newTab;
            _tip.SetToolTip(this, newTab >= 0 && newTab < _items.Count ? _items[newTab].Title : (newPlus ? "Yeni Sekme (Ctrl+T)" : ""));
        }

        if (newTab != _hoverTab || newClose != _hoverClose || newPlus != _hoverPlus)
        {
            _hoverTab = newTab;
            _hoverClose = newClose;
            _hoverPlus = newPlus;
            Cursor = (newTab >= 0 || newPlus) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverTab = -1;
        _hoverClose = -1;
        _hoverPlus = false;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _plusRect.Contains(e.Location))
        {
            NewTabClicked?.Invoke();
            return;
        }

        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (!_tabRects[i].Contains(e.Location)) continue;
            var item = _items[i];

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _closeRects[i].Contains(e.Location)))
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
            g.DrawLine(sep, 0, Height - 1, Width, Height - 1);

        float sc = GraphicsExt.DpiScale(this);

        if (IsIncognito)
        {
            var badge = new RectangleF(8f * sc, 7f * sc, 100f * sc, Height - 12f * sc);
            using var gp = GraphicsExt.CreateRoundedRect(badge, badge.Height / 2f);
            using var br = new SolidBrush(Palette.ElevatedBg);
            g.FillPath(br, gp);
            IconPainter.Draw(g, VectorIcon.Incognito, new RectangleF(badge.X + 8f * sc, badge.Y + 4f * sc, 15f * sc, 15f * sc), Palette.Accent, 1.5f);
            TextRenderer.DrawText(g, "Gizli Mod", new Font(Font.FontFamily, 8.25f, FontStyle.Bold),
                new Rectangle((int)(badge.X + 26f * sc), (int)badge.Y, (int)(badge.Width - 28f * sc), (int)badge.Height),
                Palette.Accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var r = _tabRects[i];
            float rad = 8f * sc;

            using var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            path.CloseFigure();

            Color tabBg = item.Active
                ? Palette.BaseBg
                : (_hoverTab == i ? Palette.HoverBg : Palette.ElevatedBg);
            using (var br = new SolidBrush(tabBg))
                g.FillPath(br, path);

            if (item.Active)
            {
                using var borderPen = new Pen(Palette.Border, 1f);
                g.DrawPath(borderPen, path);

                var topBar = new RectangleF(r.X + rad, r.Y, Math.Max(4, r.Width - rad * 2), 2.5f * sc);
                using var accBr = new LinearGradientBrush(topBar, Palette.Accent, Palette.AccentPurple, 0f);
                g.FillRectangle(accBr, topBar);

                using var cover = new Pen(Palette.BaseBg, 2f);
                g.DrawLine(cover, r.X + 1, Height - 1, r.Right - 1, Height - 1);
            }

            float iconSz = 13f * sc;
            var iconRect = new RectangleF(r.X + 10f * sc, r.Y + (r.Height - iconSz) / 2f, iconSz, iconSz);
            VectorIcon glyph = item.Loading ? VectorIcon.Reload
                : (item.Sleeping || item.DeepUnloaded) ? VectorIcon.Sleep
                : VectorIcon.Globe;
            Color glyphColor = item.Loading ? Palette.Accent
                : (item.Sleeping || item.DeepUnloaded) ? Palette.AccentPurple
                : (item.Active ? Palette.Accent : Palette.TextMuted);
            IconPainter.Draw(g, glyph, iconRect, glyphColor, 1.45f);

            var titleRect = new Rectangle(
                (int)(iconRect.Right + 6f * sc),
                (int)r.Y,
                Math.Max(10, (int)(_closeRects[i].X - iconRect.Right - 8f * sc)),
                (int)r.Height);
            using var titleFont = new Font(Font.FontFamily, Font.Size, item.Active ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(g, item.Title, titleFont, titleRect,
                item.Active ? Palette.TextPrimary : Palette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var cr = _closeRects[i];
            if (_hoverClose == i)
            {
                using var cbg = new SolidBrush(Color.FromArgb(65, Palette.Red));
                g.FillEllipse(cbg, cr);
            }
            float pad = 4.2f * sc;
            IconPainter.Draw(g, VectorIcon.Close,
                new RectangleF(cr.X + pad, cr.Y + pad, cr.Width - pad * 2, cr.Height - pad * 2),
                _hoverClose == i ? Palette.Red : Palette.TextMuted, 1.45f);
        }

        if (_hoverPlus)
        {
            using var pbg = new SolidBrush(Palette.HoverBg);
            g.FillEllipse(pbg, _plusRect);
        }
        float ppad = 7f * sc;
        IconPainter.Draw(g, VectorIcon.Plus,
            new RectangleF(_plusRect.X + ppad, _plusRect.Y + ppad, _plusRect.Width - ppad * 2, _plusRect.Height - ppad * 2),
            _hoverPlus ? Palette.Accent : Palette.TextSecondary, 1.75f);
    }
}

public sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly ThemePalette _p;

    public ModernMenuRenderer(ThemePalette palette) : base(new PaletteColorTable(palette))
    {
        _p = palette;
        RoundedEdges = true;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _p.TextPrimary : _p.TextMuted;
        base.OnRenderItemText(e);
    }

    private sealed class PaletteColorTable : ProfessionalColorTable
    {
        private readonly ThemePalette _p;
        public PaletteColorTable(ThemePalette p) => _p = p;
        public override Color ToolStripDropDownBackground => _p.SurfaceBg;
        public override Color MenuBorder => _p.Border;
        public override Color MenuItemBorder => _p.HoverBg;
        public override Color MenuItemSelected => _p.HoverBg;
        public override Color MenuItemSelectedGradientBegin => _p.HoverBg;
        public override Color MenuItemSelectedGradientEnd => _p.HoverBg;
        public override Color ImageMarginGradientBegin => _p.SurfaceBg;
        public override Color ImageMarginGradientMiddle => _p.SurfaceBg;
        public override Color ImageMarginGradientEnd => _p.SurfaceBg;
        public override Color SeparatorDark => _p.Border;
        public override Color SeparatorLight => _p.Border;
    }
}
