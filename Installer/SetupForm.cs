using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EDBrowser.Setup;

public sealed class SetupForm : Form
{
    private const string AppName = "ED Browser";
    private const string AppVersion = "5.0.1";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EDBrowser";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly Color _bg = Color.FromArgb(12, 12, 20);
    private readonly Color _surface = Color.FromArgb(17, 17, 32);
    private readonly Color _elevated = Color.FromArgb(24, 26, 48);
    private readonly Color _border = Color.FromArgb(46, 50, 78);
    private readonly Color _accent = Color.FromArgb(56, 189, 248);
    private readonly Color _purple = Color.FromArgb(129, 140, 248);
    private readonly Color _text = Color.FromArgb(226, 232, 240);
    private readonly Color _muted = Color.FromArgb(148, 163, 184);

    private readonly bool _uninstallMode;
    private readonly TextBox _dirBox;
    private readonly CheckBox _chkDesktop;
    private readonly CheckBox _chkStartMenu;
    private readonly CheckBox _chkLaunch;
    private readonly CheckBox _chkDeleteData;
    private readonly ProgressBar _progress;
    private readonly Label _statusLbl;
    private readonly Button _actionBtn;
    private readonly Button _cancelBtn;

    public SetupForm(bool uninstallMode = false, string? initialDirOverride = null)
    {
        _uninstallMode = uninstallMode;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = uninstallMode ? "ED Browser — Kaldır" : $"ED Browser v{AppVersion} Kurulum Sihirbazı";
        Size = new Size(580, uninstallMode ? 360 : 490);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _bg;
        ForeColor = _text;
        Icon = LoadIcon();

        var defaultDir = initialDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EDBrowser");

        // Top hero header
        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = _surface, Padding = new Padding(22, 16, 22, 14) };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(_border, 1f);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        Image? logoPng = null;
        try
        {
            using var ls = Assembly.GetExecutingAssembly().GetManifestResourceStream("edbrowser.png");
            if (ls is not null) logoPng = Image.FromStream(ls);
        }
        catch { }

        var logo = new Panel { Location = new Point(22, 16), Size = new Size(60, 60), BackColor = Color.Transparent };
        logo.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (logoPng is not null)
            {
                g.DrawImage(logoPng, new Rectangle(0, 0, 60, 60));
                return;
            }
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(1, 1, 58, 58);
            using var path = RoundedRect(r, 14);
            using var br = new LinearGradientBrush(r, Color.FromArgb(16, 26, 54), Color.FromArgb(26, 36, 74), 45f);
            g.FillPath(br, path);
            using var pen = new Pen(_accent, 2f);
            g.DrawPath(pen, path);
            using var f = new Font("Segoe UI", 18f, FontStyle.Bold);
            TextRenderer.DrawText(g, "ED", f, Rectangle.Round(r), _accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        var titleLbl = new Label
        {
            Text = uninstallMode ? "ED Browser'ı Kaldır" : $"ED Browser v{AppVersion} Kurulumu",
            Location = new Point(92, 18),
            Size = new Size(450, 30),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = _text
        };
        var subLbl = new Label
        {
            Text = uninstallMode
                ? "Uygulama dosyaları, kısayollar ve sistem kayıtları kaldırılacak."
                : "Ultra Düşük RAM  •  Chromium (WebView2) Motoru  •  Erol Dizdar",
            Location = new Point(92, 50),
            Size = new Size(450, 22),
            Font = new Font("Segoe UI", 9f),
            ForeColor = _muted
        };
        header.Controls.AddRange(new Control[] { logo, titleLbl, subLbl });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), BackColor = _bg };

        _dirBox = new TextBox
        {
            Text = defaultDir,
            Location = new Point(24, 44),
            Size = new Size(406, 28),
            BackColor = _elevated,
            ForeColor = _text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f),
            ReadOnly = uninstallMode
        };

        var browseBtn = MakeBtn("Gözat…", primary: false);
        browseBtn.Location = new Point(438, 42);
        browseBtn.Size = new Size(96, 30);
        browseBtn.Visible = !uninstallMode;
        browseBtn.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog
            {
                Description = "ED Browser kurulum klasörünü seçin",
                SelectedPath = _dirBox.Text
            };
            if (fbd.ShowDialog(this) == DialogResult.OK)
                _dirBox.Text = Path.Combine(fbd.SelectedPath, "EDBrowser");
        };

        _chkDesktop = MakeCheck("Masaüstüne kısayol oluştur", true, 92);
        _chkStartMenu = MakeCheck("Başlat Menüsüne kısayol ekle", true, 122);
        _chkLaunch = MakeCheck("Kurulum tamamlanınca ED Browser'ı başlat", true, 152);
        _chkDeleteData = MakeCheck("Kişisel profil verilerini (%LOCALAPPDATA%\\EDBrowser4) de sil", false, 92);

        if (uninstallMode)
        {
            _chkDesktop.Visible = false;
            _chkStartMenu.Visible = false;
            _chkLaunch.Visible = false;
            body.Controls.Add(new Label
            {
                Text = "Kurulum Konumu",
                Location = new Point(24, 18),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = _muted
            });
            body.Controls.Add(_dirBox);
            body.Controls.Add(_chkDeleteData);
        }
        else
        {
            body.Controls.Add(new Label
            {
                Text = "Kurulum Klasörü",
                Location = new Point(24, 18),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = _muted
            });
            body.Controls.AddRange(new Control[] { _dirBox, browseBtn, _chkDesktop, _chkStartMenu, _chkLaunch });
        }

        int progY = uninstallMode ? 142 : 206;
        _statusLbl = new Label
        {
            Text = uninstallMode ? "Kaldırmaya hazır." : "Kuruluma hazır (~1,8 MB). WebView2 sistemdeki motorla paylaşılır.",
            Location = new Point(24, progY),
            Size = new Size(510, 22),
            Font = new Font("Segoe UI", 8.75f),
            ForeColor = _muted
        };
        _progress = new ProgressBar
        {
            Location = new Point(24, progY + 26),
            Size = new Size(510, 14),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100
        };
        body.Controls.Add(_statusLbl);
        body.Controls.Add(_progress);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = _surface, Padding = new Padding(24, 13, 24, 13) };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(_border, 1f);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        _cancelBtn = MakeBtn("İptal", primary: false);
        _cancelBtn.Dock = DockStyle.Right;
        _cancelBtn.Width = 96;
        _cancelBtn.Click += (_, _) => Close();

        var gap = new Panel { Dock = DockStyle.Right, Width = 10 };

        _actionBtn = MakeBtn(uninstallMode ? "Kaldır" : "Kurulumu Başlat", primary: true);
        _actionBtn.Dock = DockStyle.Right;
        _actionBtn.Width = 172;
        _actionBtn.Click += async (_, _) =>
        {
            if (_uninstallMode) await RunUninstallAsync();
            else await RunInstallAsync();
        };

        footer.Controls.Add(_actionBtn);
        footer.Controls.Add(gap);
        footer.Controls.Add(_cancelBtn);

        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(footer);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int val = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref val, sizeof(int));
        }
        catch { }
    }

    private CheckBox MakeCheck(string text, bool isChecked, int y) => new()
    {
        Text = text,
        Checked = isChecked,
        Location = new Point(24, y),
        Size = new Size(510, 24),
        Font = new Font("Segoe UI", 9.25f),
        ForeColor = _text,
        BackColor = Color.Transparent
    };

    private Button MakeBtn(string text, bool primary)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? _accent : _elevated,
            ForeColor = primary ? Color.FromArgb(10, 14, 24) : _text,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = _border;
        return b;
    }

    private async Task RunInstallAsync()
    {
        var installDir = _dirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installDir))
        {
            MessageBox.Show(this, "Lütfen geçerli bir kurulum klasörü seçin.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _actionBtn.Enabled = false;
        _cancelBtn.Enabled = false;

        try
        {
            SetStep(15, "Çalışan ED Browser süreçleri kontrol ediliyor…");
            await Task.Run(() => StopRunningBrowserProcesses(installDir));

            SetStep(40, "Uygulama dosyaları çıkarılıyor…");
            Directory.CreateDirectory(installDir);
            var targetExe = Path.Combine(installDir, "ED Browser.exe");
            var flagPath = Path.Combine(installDir, "installed.flag");
            var uninstallExe = Path.Combine(installDir, "Uninstall.exe");

            await Task.Run(() =>
            {
                ExtractPayload(targetExe);
                File.WriteAllText(flagPath, AppVersion);
                var selfExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(selfExe) && File.Exists(selfExe) &&
                    !selfExe.Equals(uninstallExe, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(selfExe, uninstallExe, overwrite: true);
                }
            });

            SetStep(70, "Kısayollar oluşturuluyor…");
            if (_chkDesktop.Checked)
            {
                var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ED Browser.lnk");
                ShortcutHelper.CreateShortcut(desktopLnk, targetExe, installDir, "ED Browser — Ultra Düşük RAM Web Tarayıcısı");
            }
            if (_chkStartMenu.Checked)
            {
                var startMenuLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "ED Browser",
                    "ED Browser.lnk");
                ShortcutHelper.CreateShortcut(startMenuLnk, targetExe, installDir, "ED Browser — Ultra Düşük RAM Web Tarayıcısı");
            }

            SetStep(90, "Windows Uygulamalar listesine kaydediliyor…");
            RegisterUninstallAndProtocol(installDir, targetExe, uninstallExe);

            SetStep(100, "Kurulum başarıyla tamamlandı!");

            if (_chkLaunch.Checked)
                Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = installDir, UseShellExecute = true });

            MessageBox.Show(this, $"ED Browser v{AppVersion} başarıyla kuruldu.\nKonum: {installDir}", "Kurulum Tamamlandı",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _actionBtn.Enabled = true;
            _cancelBtn.Enabled = true;
            SetStep(0, "Kurulum hatası: " + ex.Message);
            MessageBox.Show(this, "Kurulum sırasında hata oluştu:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunUninstallAsync()
    {
        var installDir = _dirBox.Text.Trim();
        _actionBtn.Enabled = false;
        _cancelBtn.Enabled = false;

        try
        {
            SetStep(25, "ED Browser kapatılıyor…");
            await Task.Run(() => StopRunningBrowserProcesses(installDir));

            SetStep(50, "Kısayollar ve kayıt defteri girdileri siliniyor…");
            try
            {
                var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ED Browser.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
                var startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ED Browser");
                if (Directory.Exists(startDir)) Directory.Delete(startDir, recursive: true);
            }
            catch { }

            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\EDBrowser", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\EDBrowser", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\EDBrowser.HTML", false); } catch { }
            try
            {
                using var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
                reg?.DeleteValue("EDBrowser", false);
            }
            catch { }

            SetStep(75, "Uygulama dosyaları kaldırılıyor…");
            var exePath = Path.Combine(installDir, "ED Browser.exe");
            var flagPath = Path.Combine(installDir, "installed.flag");
            if (File.Exists(exePath)) File.Delete(exePath);
            if (File.Exists(flagPath)) File.Delete(flagPath);

            if (_chkDeleteData.Checked)
            {
                var profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EDBrowser4");
                if (Directory.Exists(profileDir))
                    Directory.Delete(profileDir, recursive: true);
            }

            SetStep(100, "Kaldırma tamamlandı!");
            ScheduleSelfDelete(installDir);
            MessageBox.Show(this, "ED Browser sisteminizden kaldırıldı.", "Kaldırma Tamamlandı",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _actionBtn.Enabled = true;
            _cancelBtn.Enabled = true;
            MessageBox.Show(this, "Kaldırma hatası:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetStep(int pct, string msg)
    {
        _progress.Value = Math.Clamp(pct, 0, 100);
        _statusLbl.Text = msg;
        Application.DoEvents();
    }

    private static void StopRunningBrowserProcesses(string installDir)
    {
        foreach (var proc in Process.GetProcessesByName("ED Browser"))
        {
            try
            {
                var p = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(p) || p.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                {
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(2000))
                        proc.Kill();
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }
    }

    private static void ExtractPayload(string destinationExe)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("EDBrowser.Payload.exe")
            ?? throw new InvalidOperationException("Gömülü kurulum paketi (EDBrowser.Payload.exe) bulunamadı.");
        using var fs = File.Create(destinationExe);
        stream.CopyTo(fs);
    }

    private static void RegisterUninstallAndProtocol(string installDir, string exePath, string uninstallExe)
    {
        long sizeKb = File.Exists(exePath) ? new FileInfo(exePath).Length / 1024 : 1800;
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
        {
            k.SetValue("DisplayName", "ED Browser");
            k.SetValue("DisplayVersion", AppVersion);
            k.SetValue("Publisher", "Erol Dizdar");
            k.SetValue("InstallLocation", installDir);
            k.SetValue("DisplayIcon", exePath);
            k.SetValue("UninstallString", $"\"{uninstallExe}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
            k.SetValue("URLInfoAbout", "https://github.com/edizdar/ED-Browser");
        }
    }

    private static void ScheduleSelfDelete(string installDir)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{installDir}\"")
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch { }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Icon LoadIcon()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("edbrowser.ico");
            if (s is not null) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    public static void RenderSetupPreview(string outPath)
    {
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var f = new SetupForm(uninstallMode: false);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-32000, -32000);
        f.Show();
        Application.DoEvents();
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(outPath, ImageFormat.Png);
        f.Close();
    }
}
