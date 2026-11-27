using System.Runtime.InteropServices;
using EDBrowser.Models;
using EDBrowser.Services;
using Microsoft.Web.WebView2.WinForms;

namespace EDBrowser.UI;

/// <summary>
/// Hosts a Chromium extension's popup UI (chrome-extension://{id}/{popupPath})
/// anchored below the toolbar extension icon button.
/// </summary>
public sealed class ExtensionPopupForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly BrowserContext _ctx;
    private readonly InstalledExtension _ext;
    private readonly ThemePalette _p;
    private WebView2? _wv;

    public ExtensionPopupForm(BrowserContext ctx, InstalledExtension ext, ThemePalette palette, Point anchorTopRightScreen)
    {
        _ctx = ctx;
        _ext = ext;
        _p = palette;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(380, 480);
        BackColor = _p.SurfaceBg;
        ForeColor = _p.TextPrimary;
        KeyPreview = true;

        var wa = Screen.FromPoint(anchorTopRightScreen).WorkingArea;
        int x = Math.Clamp(anchorTopRightScreen.X - Width, wa.Left + 8, wa.Right - Width - 8);
        int y = Math.Clamp(anchorTopRightScreen.Y, wa.Top + 8, wa.Bottom - Height - 8);
        Location = new Point(x, y);

        var header = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = _p.ElevatedBg, Padding = new Padding(10, 4, 8, 4) };
        var lbl = new Label
        {
            Text = $"{_ext.Name} (v{_ext.Version})",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        var closeBtn = new IconButton
        {
            IconType = VectorIcon.Close,
            Dock = DockStyle.Right,
            Size = new Size(26, 26),
            IconSizePx = 11,
            NormalBg = _p.ElevatedBg,
            HoverBg = _p.HoverBg,
            IconColor = _p.TextMuted
        };
        closeBtn.Click += (_, _) => Close();
        header.Controls.Add(lbl);
        header.Controls.Add(closeBtn);

        _wv = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_wv);
        Controls.Add(header);

        Deactivate += (_, _) => Close();
        Shown += async (_, _) => await InitPopupAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int pref = 2; _ = DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int)); } catch { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task InitPopupAsync()
    {
        if (_wv is null || IsDisposed) return;
        try
        {
            var env = await _ctx.GetSharedEnvironmentAsync();
            if (IsDisposed) return;
            await _wv.EnsureCoreWebView2Async(env);
            if (IsDisposed || _wv.CoreWebView2 is null) return;
            await _ctx.Extensions.SyncWithProfileAsync(_wv.CoreWebView2.Profile);
            if (string.IsNullOrWhiteSpace(_ext.RuntimeId) || string.IsNullOrWhiteSpace(_ext.PopupPath))
            {
                Close();
                return;
            }
            _wv.CoreWebView2.Navigate($"chrome-extension://{_ext.RuntimeId}/{_ext.PopupPath}");
        }
        catch
        {
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _wv?.Dispose();
        base.Dispose(disposing);
    }
}
