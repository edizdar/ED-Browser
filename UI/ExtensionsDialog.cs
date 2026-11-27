using EDBrowser.Models;
using EDBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EDBrowser.UI;

/// <summary>
/// Chromium Extension Store & Manager dialog supporting Chrome Web Store URLs/IDs,
/// Microsoft Edge Add-ons URLs, local .crx/.zip packages, and unpacked extension folders.
/// </summary>
public sealed class ExtensionsDialog : Form
{
    private readonly BrowserContext _ctx;
    private readonly CoreWebView2Profile? _profile;
    private readonly Action<string> _openTabAction;
    private readonly ThemePalette _p;

    private readonly RoundedTextBox _urlOrIdBox;
    private readonly Label _statusLbl;
    private readonly Panel _listHost;

    public ExtensionsDialog(
        BrowserContext ctx,
        CoreWebView2Profile? profile,
        Action<string> openTabAction,
        ThemePalette? palette = null)
    {
        _ctx = ctx;
        _profile = profile;
        _openTabAction = openTabAction;
        _p = palette ?? ThemeService.Dark;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Eklentiler ve Uzantı Mağazası";
        Size = new Size(820, 580);
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = _p.BaseBg;
        ForeColor = _p.TextPrimary;
        Padding = new Padding(18);
        ThemeService.ApplyNativeTitleBarTheme(Handle, _p != ThemeService.Light);

        // 1. Top Store Action Bar
        var storeCard = new ModernCardPanel
        {
            Dock = DockStyle.Top,
            Height = 132,
            FillColor = _p.SurfaceBg,
            BorderColor = _p.Border,
            Padding = new Padding(14)
        };

        var storeTitle = new Label
        {
            Text = "Chromium Eklenti Mağazaları & Paket Yükleme",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = _p.TextPrimary,
            UseMnemonic = false
        };

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(0, 4, 0, 4),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        ModernButton MakeTopBtn(string text, bool primary, EventHandler click)
        {
            var b = new ModernButton { Text = text, Primary = primary, Height = 32, Margin = new Padding(0, 0, 8, 0) };
            b.ApplyTheme(_p);
            b.FitWidthToText(100, 22);
            b.Click += click;
            return b;
        }

        var cwsBtn = MakeTopBtn("Chrome Web Store'u Aç", true, (_, _) =>
        {
            _openTabAction("https://chromewebstore.google.com/");
            Close();
        });
        var edgeBtn = MakeTopBtn("Edge Eklentileri'ni Aç", false, (_, _) =>
        {
            _openTabAction("https://microsoftedge.microsoft.com/addons");
            Close();
        });
        var crxBtn = MakeTopBtn("CRX / ZIP Yükle", false, async (_, _) => await InstallLocalCrxAsync());
        var folderBtn = MakeTopBtn("Klasör Yükle", false, async (_, _) => await InstallUnpackedFolderAsync());

        btnRow.Controls.AddRange(new Control[] { cwsBtn, edgeBtn, crxBtn, folderBtn });

        var installRow = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            ColumnCount = 2
        };
        installRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        installRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _urlOrIdBox = new RoundedTextBox
        {
            Dock = DockStyle.Fill,
            Height = 34,
            PlaceholderText = "Chrome Web Store / Edge Add-ons bağlantısı veya 32 haneli eklenti ID yapıştır…"
        };
        _urlOrIdBox.ApplyTheme(_p, elevated: false);

        var installBtn = new ModernButton { Text = "İndir ve Kur", Primary = true, Height = 34, Margin = new Padding(8, 0, 0, 0) };
        installBtn.ApplyTheme(_p);
        installBtn.FitWidthToText(118, 24);
        installBtn.Click += async (_, _) => await InstallFromStoreInputAsync();
        _urlOrIdBox.Inner.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await InstallFromStoreInputAsync(); }
        };

        installRow.Controls.Add(_urlOrIdBox, 0, 0);
        installRow.Controls.Add(installBtn, 1, 0);

        storeCard.Controls.Add(installRow);
        storeCard.Controls.Add(btnRow);
        storeCard.Controls.Add(storeTitle);

        _statusLbl = new Label
        {
            Text = "İpucu: Chrome Web Store / Edge Add-ons sayfasında üstte 'ED Browser'a Ekle' çubuğu otomatik belirir.",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = _p.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };

        _listHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 4, 4, 4)
        };

        Controls.Add(_listHost);
        Controls.Add(_statusLbl);
        Controls.Add(storeCard);

        RenderExtensionList();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeService.ApplyNativeTitleBarTheme(Handle, _p != ThemeService.Light);
    }

    private async Task InstallFromStoreInputAsync()
    {
        var input = _urlOrIdBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        SetStatus("Eklenti indiriliyor ve açılıyor…", _p.Accent);
        try
        {
            var ext = await _ctx.Extensions.InstallFromStoreUrlOrIdAsync(input, _profile);
            _urlOrIdBox.Text = string.Empty;
            SetStatus($"'{ext.Name}' (v{ext.Version}) başarıyla kuruldu!", _p.Green);
            RenderExtensionList();
        }
        catch (Exception ex)
        {
            SetStatus("Kurulum hatası: " + ex.Message, _p.Red);
        }
    }

    private async Task InstallLocalCrxAsync()
    {
        using var ofd = new OpenFileDialog { Filter = "Eklenti Paketleri (*.crx;*.zip)|*.crx;*.zip|Tüm Dosyalar|*.*" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        SetStatus("Paket çıkarılıyor…", _p.Accent);
        try
        {
            var ext = await _ctx.Extensions.InstallFromCrxOrZipFileAsync(ofd.FileName, _profile);
            SetStatus($"'{ext.Name}' başarıyla yüklendi.", _p.Green);
            RenderExtensionList();
        }
        catch (Exception ex)
        {
            SetStatus("Paket hatası: " + ex.Message, _p.Red);
        }
    }

    private async Task InstallUnpackedFolderAsync()
    {
        using var fbd = new FolderBrowserDialog { Description = "manifest.json içeren eklenti klasörünü seçin" };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;

        SetStatus("Klasör yükleniyor…", _p.Accent);
        try
        {
            var ext = await _ctx.Extensions.InstallFromUnpackedFolderAsync(fbd.SelectedPath, _profile);
            SetStatus($"'{ext.Name}' yüklendi.", _p.Green);
            RenderExtensionList();
        }
        catch (Exception ex)
        {
            SetStatus("Klasör hatası: " + ex.Message, _p.Red);
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusLbl.Text = text;
        _statusLbl.ForeColor = color;
    }

    private void RenderExtensionList()
    {
        _listHost.SuspendLayout();
        _listHost.Controls.Clear();

        if (_ctx.Extensions.Items.Count == 0)
        {
            _listHost.Controls.Add(new Label
            {
                Text = "Henüz kurulu eklenti yok.\nYukarıdan Chrome Web Store veya Edge Add-ons'u açabilir ya da CRX/ZIP yükleyebilirsiniz.",
                Dock = DockStyle.Top,
                Height = 120,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = _p.TextMuted,
                Font = new Font("Segoe UI", 9.5f),
                UseMnemonic = false
            });
            _listHost.ResumeLayout();
            return;
        }

        foreach (var ext in _ctx.Extensions.Items.ToList())
        {
            var card = new ModernCardPanel
            {
                Dock = DockStyle.Top,
                Height = 86,
                FillColor = _p.SurfaceBg,
                BorderColor = _p.Border,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(14, 10, 14, 10)
            };

            var nameLbl = new Label
            {
                Text = $"{ext.Name}  (v{ext.Version})  •  {ext.SourceStore}",
                Location = new Point(14, 10),
                Size = new Size(440, 22),
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
                ForeColor = _p.TextPrimary,
                AutoEllipsis = true,
                UseMnemonic = false
            };

            var descLbl = new Label
            {
                Text = string.IsNullOrWhiteSpace(ext.Description) ? ext.FolderPath : ext.Description,
                Location = new Point(14, 34),
                Size = new Size(440, 38),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _p.TextSecondary,
                AutoEllipsis = true,
                UseMnemonic = false
            };

            var toggle = new ModernToggleSwitch
            {
                Checked = ext.Enabled,
                Location = new Point(card.Width - 62, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                OnColorStart = _p.Accent,
                OnColorEnd = _p.AccentPurple,
                OffTrackColor = _p.HoverBg
            };
            toggle.CheckedChanged += async (_, _) =>
            {
                await _ctx.Extensions.ToggleExtensionAsync(_profile, ext, toggle.Checked);
            };

            var rmBtn = new ModernButton
            {
                Text = "Kaldır",
                Size = new Size(76, 28),
                Location = new Point(card.Width - 94, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            rmBtn.ApplyTheme(_p);
            rmBtn.SecondaryText = _p.Red;
            rmBtn.Click += async (_, _) =>
            {
                await _ctx.Extensions.RemoveExtensionAsync(_profile, ext);
                RenderExtensionList();
            };

            string uiRel = !string.IsNullOrEmpty(ext.PopupPath) ? ext.PopupPath : ext.OptionsPath;
            if (!string.IsNullOrEmpty(uiRel) && !string.IsNullOrEmpty(ext.RuntimeId))
            {
                var openUiBtn = new ModernButton
                {
                    Text = "Arayüz",
                    Size = new Size(74, 28),
                    Location = new Point(card.Width - 174, 46),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                openUiBtn.ApplyTheme(_p);
                openUiBtn.Click += (_, _) =>
                {
                    _openTabAction($"chrome-extension://{ext.RuntimeId}/{uiRel}");
                    Close();
                };
                card.Controls.Add(openUiBtn);
            }

            card.Controls.AddRange(new Control[] { nameLbl, descLbl, toggle, rmBtn });

            var spacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };
            _listHost.Controls.Add(spacer);
            _listHost.Controls.Add(card);
        }

        _listHost.ResumeLayout();
    }
}
