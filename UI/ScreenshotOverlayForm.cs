namespace EDBrowser.UI;

/// <summary>
/// Full-area dimmed overlay allowing drag-to-crop screenshot selection (Enter = full page, Esc = cancel).
/// </summary>
public sealed class ScreenshotOverlayForm : Form
{
    private readonly Bitmap _sourceBitmap;
    private Point _start;
    private Rectangle _selection;
    private bool _dragging;

    public Rectangle? SelectedArea { get; private set; }
    public bool FullPageSelected { get; private set; }

    public ScreenshotOverlayForm(Bitmap sourceBitmap, Rectangle bounds)
    {
        _sourceBitmap = sourceBitmap;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        DoubleBuffered = true;
        TopMost = true;
        Cursor = Cursors.Cross;
        ShowInTaskbar = false;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                FullPageSelected = true;
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _selection = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        };

        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            int x = Math.Min(_start.X, e.X);
            int y = Math.Min(_start.Y, e.Y);
            int w = Math.Abs(e.X - _start.X);
            int h = Math.Abs(e.Y - _start.Y);
            _selection = new Rectangle(x, y, w, h);
            Invalidate();
        };

        MouseUp += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            if (_selection.Width > 10 && _selection.Height > 10)
                SelectedArea = _selection;
            else
                FullPageSelected = true;
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImage(_sourceBitmap, 0, 0, ClientSize.Width, ClientSize.Height);
        using var dim = new SolidBrush(Color.FromArgb(115, 0, 0, 0));
        e.Graphics.FillRectangle(dim, ClientRectangle);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            var srcRect = new RectangleF(
                _selection.X * (float)_sourceBitmap.Width / ClientSize.Width,
                _selection.Y * (float)_sourceBitmap.Height / ClientSize.Height,
                _selection.Width * (float)_sourceBitmap.Width / ClientSize.Width,
                _selection.Height * (float)_sourceBitmap.Height / ClientSize.Height);
            e.Graphics.DrawImage(_sourceBitmap, _selection, srcRect, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(56, 189, 248), 2);
            e.Graphics.DrawRectangle(pen, _selection);
        }

        const string hint = "Alan seç veya Enter = tüm sayfa  •  ESC = iptal";
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        var textSz = e.Graphics.MeasureString(hint, font);
        var box = new RectangleF((ClientSize.Width - textSz.Width - 28) / 2f, ClientSize.Height - 54, textSz.Width + 28, 32);
        using var pill = new SolidBrush(Color.FromArgb(200, 12, 12, 20));
        e.Graphics.FillRectangle(pill, box);
        e.Graphics.DrawString(hint, font, Brushes.White, box.X + 14, box.Y + 7);
    }
}
