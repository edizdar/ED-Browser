using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

/// <summary>
/// Chrome/Edge-style Browser Task Manager (Shift+Esc) showing live tab states,
/// per-process memory/CPU tree usage, and 1-click Sleep / Deep Unload / Close actions.
/// </summary>
public sealed class TaskManagerDialog : Form
{
    private readonly ThemePalette _p;
    private readonly Func<IReadOnlyList<BrowserTab>> _getTabs;
    private readonly Func<BrowserTab, Task> _closeTabAsync;
    private readonly Action _refreshMemoryBadge;
    private readonly ListView _tabList;
    private readonly ListView _procList;
    private readonly Label _summaryLbl;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public TaskManagerDialog(
        ThemePalette palette,
        Func<IReadOnlyList<BrowserTab>> getTabs,
        Func<BrowserTab, Task> closeTabAsync,
        Action refreshMemoryBadge)
    {
        _p = palette;
        _getTabs = getTabs;
        _closeTabAsync = closeTabAsync;
        _refreshMemoryBadge = refreshMemoryBadge;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "ED Browser — Görev Yöneticisi (Shift+Esc)";
        Size = new Size(880, 580);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = _p.BaseBg;
        ForeColor = _p.TextPrimary;
        ThemeService.ApplyNativeTitleBarTheme(Handle, _p.BaseBg.GetBrightness() < 0.4f);

        _summaryLbl = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _p.Accent,
            Padding = new Padding(14, 8, 14, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };

        _tabList = new ListView
        {
            Dock = DockStyle.Top,
            Height = 230,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _p.SurfaceBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 8.75f)
        };
        _tabList.Columns.Add("Sekme", 250);
        _tabList.Columns.Add("Grup", 90);
        _tabList.Columns.Add("Durum", 110);
        _tabList.Columns.Add("Engel", 65);
        _tabList.Columns.Add("Adres", 230);

        var procTitle = new Label
        {
            Text = "Çalışan Tarayıcı & WebView2 İşlemleri (RAM / CPU)",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = _p.TextSecondary,
            Padding = new Padding(14, 6, 14, 0),
            UseMnemonic = false
        };

        _procList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _p.SurfaceBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 8.75f)
        };
        _procList.Columns.Add("PID", 75);
        _procList.Columns.Add("İşlem Adı", 270);
        _procList.Columns.Add("RAM (Çalışma)", 130);
        _procList.Columns.Add("Özel Bellek", 130);
        _procList.Columns.Add("CPU Süresi", 110);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 9, 12, 9),
            BackColor = _p.SurfaceBg
        };

        var sleepBtn = MakeBtn("Sekmeyi Uyut", false, async (_, _) =>
        {
            if (SelectedTab() is { } t) { await t.TrySleepAsync(); RefreshData(); _refreshMemoryBadge(); }
        });
        var unloadBtn = MakeBtn("Derin Boşalt (0 MB)", false, (_, _) =>
        {
            if (SelectedTab() is { } t) { t.DeepUnload(); RefreshData(); _refreshMemoryBadge(); }
        });
        var closeBtn = MakeBtn("Sekmeyi Kapat", false, async (_, _) =>
        {
            if (SelectedTab() is { } t) { await _closeTabAsync(t); RefreshData(); _refreshMemoryBadge(); }
        });
        var trimAllBtn = MakeBtn("Tüm Pasifleri Sıkıştır", true, async (_, _) =>
        {
            foreach (var t in _getTabs().Where(x => x.HasNavigated && !x.IsSleeping && !x.IsDeepUnloaded))
                _ = await t.TrySleepAsync();
            MemoryManagerService.TrimHostWorkingSet();
            RefreshData();
            _refreshMemoryBadge();
        });
        var refreshBtn = MakeBtn("Yenile", false, (_, _) => RefreshData());

        footer.Controls.AddRange(new Control[] { sleepBtn, unloadBtn, closeBtn, trimAllBtn, refreshBtn });

        Controls.Add(_procList);
        Controls.Add(procTitle);
        Controls.Add(_tabList);
        Controls.Add(_summaryLbl);
        Controls.Add(footer);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshData();
        Shown += (_, _) => { RefreshData(); _refreshTimer.Start(); };
        FormClosed += (_, _) => _refreshTimer.Dispose();
    }

    private ModernButton MakeBtn(string text, bool primary, EventHandler click)
    {
        var b = new ModernButton { Text = text, Primary = primary, Height = 32, Margin = new Padding(0, 0, 6, 0) };
        b.ApplyTheme(_p);
        b.FitWidthToText(90, 18);
        b.Click += click;
        return b;
    }

    private BrowserTab? SelectedTab() =>
        _tabList.SelectedItems.Count > 0 ? _tabList.SelectedItems[0].Tag as BrowserTab : null;

    public void RefreshData()
    {
        if (IsDisposed) return;

        var tabs = _getTabs();
        _tabList.BeginUpdate();
        _tabList.Items.Clear();
        foreach (var t in tabs)
        {
            string state = t.IsDeepUnloaded ? "Boşaltıldı (0 MB)"
                : t.IsSleeping ? "Uyuyor (💤)"
                : t.IsPlayingAudio ? (t.IsMuted ? "Sessiz (🔇)" : "Ses Çalıyor")
                : t.IsLoading ? "Yükleniyor…"
                : "Aktif";
            var item = new ListViewItem(string.IsNullOrWhiteSpace(t.Title) ? "Yeni Sekme" : t.Title) { Tag = t };
            item.SubItems.Add(string.IsNullOrEmpty(t.GroupName) ? "—" : t.GroupName);
            item.SubItems.Add(state);
            item.SubItems.Add(t.BlockedOnTab.ToString());
            item.SubItems.Add(string.IsNullOrEmpty(t.Url) ? "Yeni Sekme" : t.Url);
            _tabList.Items.Add(item);
        }
        _tabList.EndUpdate();

        var procs = MemoryManagerService.CaptureDetailedProcessList();
        long totalWsMb = procs.Sum(p => p.WorkingSetBytes) / (1024 * 1024);
        _summaryLbl.Text = $"Açık Sekmeler: {tabs.Count}   •   Toplam İşlem: {procs.Count}   •   Toplam RAM: {totalWsMb} MB";

        _procList.BeginUpdate();
        _procList.Items.Clear();
        foreach (var pr in procs)
        {
            var lvi = new ListViewItem(pr.Pid.ToString());
            lvi.SubItems.Add(pr.Name);
            lvi.SubItems.Add($"{pr.WorkingSetBytes / (1024.0 * 1024.0):0.0} MB");
            lvi.SubItems.Add($"{pr.PrivateBytes / (1024.0 * 1024.0):0.0} MB");
            lvi.SubItems.Add($"{pr.CpuSeconds:0.1} sn");
            _procList.Items.Add(lvi);
        }
        _procList.EndUpdate();
    }
}
