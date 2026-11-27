using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

public static class PromptDialog
{
    public static string? Show(IWin32Window? owner, string title, string prompt, string defaultValue = "", ThemePalette? palette = null)
    {
        var p = palette ?? ThemeService.Dark;
        using var form = new Form
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            Text = title,
            Size = new Size(400, 175),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = p.BaseBg,
            ForeColor = p.TextPrimary,
            Padding = new Padding(16)
        };
        ThemeService.ApplyNativeTitleBarTheme(form.Handle, p != ThemeService.Light);

        var lbl = new Label
        {
            Text = prompt,
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 8)
        };
        var box = new RoundedTextBox
        {
            Text = defaultValue,
            Dock = DockStyle.Top,
            Height = 36
        };
        box.ApplyTheme(p);

        var btns = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new ModernButton { Text = "İptal", DialogResult = DialogResult.Cancel, Width = 86, Height = 32 };
        cancel.ApplyTheme(p);
        var ok = new ModernButton { Text = "Tamam", Primary = true, Width = 96, Height = 32 };
        ok.ApplyTheme(p);
        ok.Click += (_, _) => { form.DialogResult = DialogResult.OK; form.Close(); };

        box.Inner.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { form.DialogResult = DialogResult.OK; form.Close(); }
            if (e.KeyCode == Keys.Escape) { form.DialogResult = DialogResult.Cancel; form.Close(); }
        };

        btns.Controls.Add(cancel);
        btns.Controls.Add(ok);
        form.Controls.Add(btns);
        form.Controls.Add(box);
        form.Controls.Add(lbl);
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}

public sealed class BookmarkEditDialog : Form
{
    private readonly List<BookmarkItem> _bookmarks;
    private readonly BookmarkItem? _editing;
    private readonly ThemePalette _p;
    private readonly Action? _onFoldersChanged;
    private readonly RoundedTextBox _titleBox;
    private readonly RoundedTextBox _urlBox;
    private readonly ComboBox _folderCombo;
    private readonly List<BookmarkItem?> _folderTargets = new();

    public bool Removed { get; private set; }

    public BookmarkEditDialog(
        List<BookmarkItem> bookmarks,
        BookmarkItem? editing = null,
        string initialTitle = "",
        string initialUrl = "",
        ThemePalette? palette = null,
        Action? onFoldersChanged = null)
    {
        _bookmarks = bookmarks;
        _editing = editing;
        _p = palette ?? ThemeService.Dark;
        _onFoldersChanged = onFoldersChanged;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = editing is null ? "Yer İmi Ekle" : "Yer İmi Düzenle";
        Size = new Size(510, 390);
        MinimumSize = new Size(460, 350);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = _p.BaseBg;
        ForeColor = _p.TextPrimary;
        Padding = new Padding(20);
        ThemeService.ApplyNativeTitleBarTheme(Handle, _p != ThemeService.Light);

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _titleBox = new RoundedTextBox { Text = editing?.Name ?? initialTitle, Dock = DockStyle.Top, Height = 36 };
        _titleBox.ApplyTheme(_p);

        _urlBox = new RoundedTextBox { Text = editing?.Url ?? initialUrl, Dock = DockStyle.Top, Height = 36 };
        _urlBox.ApplyTheme(_p);

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = 2 };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _folderCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 26,
            Height = 34,
            BackColor = _p.SurfaceBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 9.5f),
            FlatStyle = FlatStyle.Flat
        };
        _folderCombo.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= _folderCombo.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var br = new SolidBrush(selected ? _p.HoverBg : _p.SurfaceBg);
            e.Graphics.FillRectangle(br, e.Bounds);
            TextRenderer.DrawText(e.Graphics, _folderCombo.Items[e.Index]?.ToString() ?? "", _folderCombo.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
                _p.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        var newFolderBtn = new ModernButton { Text = "+ Yeni Klasör", Height = 34, Margin = new Padding(8, 0, 0, 0) };
        newFolderBtn.ApplyTheme(_p);
        newFolderBtn.FitWidthToText(118, 28);
        newFolderBtn.Click += (_, _) =>
        {
            var fname = PromptDialog.Show(this, "Yeni Klasör", "Klasör adı:", "", _p);
            if (string.IsNullOrWhiteSpace(fname)) return;
            var parent = _folderCombo.SelectedIndex >= 0 && _folderCombo.SelectedIndex < _folderTargets.Count
                ? _folderTargets[_folderCombo.SelectedIndex]
                : null;
            var folder = new BookmarkItem { Type = "folder", Name = fname.Trim() };
            if (parent is not null)
                parent.Children.Add(folder);
            else
                _bookmarks.Add(folder);
            _onFoldersChanged?.Invoke();
            PopulateFolders(folder);
        };
        folderRow.Controls.Add(_folderCombo, 0, 0);
        folderRow.Controls.Add(newFolderBtn, 1, 0);

        AddRow(tbl, "Başlık", _titleBox);
        AddRow(tbl, "URL", _urlBox);
        AddRow(tbl, "Klasör", folderRow);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 10, 0, 0) };

        var cancelBtn = new ModernButton { Text = "İptal", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 88, Height = 34 };
        cancelBtn.ApplyTheme(_p);
        var gap = new Panel { Dock = DockStyle.Right, Width = 8 };
        var saveBtn = new ModernButton { Text = "Kaydet", Primary = true, Dock = DockStyle.Right, Width = 108, Height = 34 };
        saveBtn.ApplyTheme(_p);
        saveBtn.Click += (_, _) => SaveAndClose();

        footer.Controls.Add(saveBtn);
        footer.Controls.Add(gap);
        footer.Controls.Add(cancelBtn);

        if (_editing is not null)
        {
            var removeBtn = new ModernButton { Text = "Yer İmini Kaldır", Dock = DockStyle.Left, Height = 34 };
            removeBtn.ApplyTheme(_p);
            removeBtn.SecondaryText = _p.Red;
            removeBtn.FitWidthToText(130, 26);
            removeBtn.Click += (_, _) =>
            {
                SidePanelHost.RemoveBookmarkById(_bookmarks, _editing.Id);
                Removed = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            footer.Controls.Add(removeBtn);
        }

        Controls.Add(tbl);
        Controls.Add(footer);
        PopulateFolders(FindParentFolder(_bookmarks, editing?.Id));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeService.ApplyNativeTitleBarTheme(Handle, _p != ThemeService.Light);
    }

    private void AddRow(TableLayoutPanel tbl, string caption, Control ctl)
    {
        var lbl = new Label
        {
            Text = caption,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = _p.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 3)
        };
        ctl.Margin = new Padding(0, 0, 0, 8);
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(lbl);
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(ctl);
    }

    private void PopulateFolders(BookmarkItem? selectedFolder)
    {
        _folderCombo.Items.Clear();
        _folderTargets.Clear();
        _folderCombo.Items.Add("— Kök Dizin —");
        _folderTargets.Add(null);

        void Walk(List<BookmarkItem> items, int depth)
        {
            foreach (var it in items.Where(x => x.IsFolder))
            {
                _folderCombo.Items.Add($"{new string(' ', depth * 2)}📁 {it.Name}");
                _folderTargets.Add(it);
                Walk(it.Children, depth + 1);
            }
        }
        Walk(_bookmarks, 0);

        int idx = selectedFolder is null ? 0 : _folderTargets.IndexOf(selectedFolder);
        _folderCombo.SelectedIndex = Math.Max(0, idx);
    }

    private static BookmarkItem? FindParentFolder(List<BookmarkItem> list, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var item in list)
        {
            if (item.IsFolder)
            {
                if (item.Children.Any(c => c.Id == id)) return item;
                var deeper = FindParentFolder(item.Children, id);
                if (deeper is not null) return deeper;
            }
        }
        return null;
    }

    private void SaveAndClose()
    {
        var url = _urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;
        var title = string.IsNullOrWhiteSpace(_titleBox.Text) ? url : _titleBox.Text.Trim();
        var targetFolder = _folderTargets[_folderCombo.SelectedIndex];

        if (_editing is not null)
        {
            _editing.Name = title;
            _editing.Url = url;
            SidePanelHost.RemoveBookmarkById(_bookmarks, _editing.Id);
            if (targetFolder is null) _bookmarks.Add(_editing);
            else targetFolder.Children.Add(_editing);
        }
        else
        {
            var bm = new BookmarkItem { Type = "bookmark", Name = title, Url = url };
            if (targetFolder is null) _bookmarks.Add(bm);
            else targetFolder.Children.Add(bm);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

public sealed class PasswordManagerDialog : Form
{
    private readonly BrowserContext _ctx;
    private readonly ThemePalette _p;
    private readonly RoundedTextBox _searchBox;
    private readonly ListView _listView;

    public PasswordManagerDialog(BrowserContext ctx, ThemePalette? palette = null)
    {
        _ctx = ctx;
        _p = palette ?? ThemeService.Dark;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Şifre Yöneticisi (AES-256-GCM + DPAPI)";
        Size = new Size(700, 480);
        MinimumSize = new Size(560, 380);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = _p.BaseBg;
        ForeColor = _p.TextPrimary;
        Padding = new Padding(16);
        ThemeService.ApplyNativeTitleBarTheme(Handle, _p != ThemeService.Light);

        var topRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 2, Padding = new Padding(0, 0, 0, 8) };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _searchBox = new RoundedTextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Site, kullanıcı veya klasör ara…",
            Height = 36
        };
        _searchBox.ApplyTheme(_p, elevated: true);
        _searchBox.Inner.TextChanged += (_, _) => LoadItems();

        var addBtn = new ModernButton
        {
            Text = "+ Şifre Ekle",
            Primary = true,
            Width = 128,
            Height = 36,
            Margin = new Padding(10, 0, 0, 0)
        };
        addBtn.ApplyTheme(_p);
        addBtn.Click += (_, _) => OpenEditDialog(null);

        topRow.Controls.Add(_searchBox, 0, 0);
        topRow.Controls.Add(addBtn, 1, 0);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _p.SurfaceBg,
            ForeColor = _p.TextPrimary,
            Font = new Font("Segoe UI", 9.25f)
        };
        _listView.Columns.Add("Site", 190);
        _listView.Columns.Add("Kullanıcı", 190);
        _listView.Columns.Add("Klasör", 120);
        _listView.Columns.Add("Şifre", 120);

        var bottomRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 10, 0, 0)
        };
        bottomRow.Controls.Add(MakeBtn("Kopyala", 100, (_, _) =>
        {
            if (_listView.SelectedItems.Count == 0) return;
            var entry = (DecryptedPasswordEntry)_listView.SelectedItems[0].Tag!;
            Clipboard.SetText(entry.Password);
        }));
        bottomRow.Controls.Add(MakeBtn("Düzenle", 100, (_, _) =>
        {
            if (_listView.SelectedItems.Count == 0) return;
            OpenEditDialog((DecryptedPasswordEntry)_listView.SelectedItems[0].Tag!);
        }));
        bottomRow.Controls.Add(MakeBtn("Sil", 88, (_, _) =>
        {
            if (_listView.SelectedItems.Count == 0) return;
            var entry = (DecryptedPasswordEntry)_listView.SelectedItems[0].Tag!;
            if (MessageBox.Show($"{entry.Domain} kaydı silinsin mi?", "Sil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _ctx.Vault.Delete(entry.Id);
                LoadItems();
            }
        }));
        bottomRow.Controls.Add(MakeBtn("CSV İçe Aktar", 136, (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "CSV Dosyaları|*.csv|Tüm Dosyalar|*.*" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                int n = ImportService.ImportPasswordsFromCsv(ofd.FileName, _ctx.Vault);
                MessageBox.Show($"{n} şifre başarıyla içe aktarıldı.", "İçe Aktarım", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadItems();
            }
        }));

        Controls.Add(_listView);
        Controls.Add(bottomRow);
        Controls.Add(topRow);
        LoadItems();
    }

    private ModernButton MakeBtn(string text, int width, EventHandler click)
    {
        var b = new ModernButton { Text = text, Width = width, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        b.ApplyTheme(_p);
        b.Click += click;
        return b;
    }

    private void LoadItems()
    {
        var q = _searchBox.Text.Trim();
        var items = _ctx.Vault.GetAll()
            .Where(x => string.IsNullOrEmpty(q) ||
                        x.Domain.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        x.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        x.Folder.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _listView.Items.Clear();
        foreach (var e in items)
        {
            var lvi = new ListViewItem(new[] { e.Domain, e.Username, e.Folder, new string('•', Math.Min(12, Math.Max(6, e.Password.Length))) })
            {
                Tag = e
            };
            _listView.Items.Add(lvi);
        }
    }

    private void OpenEditDialog(DecryptedPasswordEntry? entry)
    {
        using var f = new Form
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            Text = entry is null ? "Şifre Ekle" : "Şifre Düzenle",
            Size = new Size(410, 340),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = _p.BaseBg,
            ForeColor = _p.TextPrimary,
            Padding = new Padding(18)
        };
        ThemeService.ApplyNativeTitleBarTheme(f.Handle, _p != ThemeService.Light);

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var dBox = AddField(tbl, "Site (domain)", entry?.Domain ?? "");
        var uBox = AddField(tbl, "Kullanıcı Adı / E-posta", entry?.Username ?? "");
        var fBox = AddField(tbl, "Klasör", entry?.Folder ?? "Genel");
        var pBox = AddField(tbl, "Şifre", entry?.Password ?? "", isPassword: true);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var cancel = new ModernButton { Text = "İptal", DialogResult = DialogResult.Cancel, Width = 86, Height = 34 };
        cancel.ApplyTheme(_p);
        var save = new ModernButton { Text = "Kaydet", Primary = true, Width = 96, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        save.ApplyTheme(_p);
        save.Click += (_, _) =>
        {
            _ctx.Vault.SaveOrUpdate(entry?.Id, dBox.Text, uBox.Text, pBox.Text, fBox.Text);
            f.DialogResult = DialogResult.OK;
            f.Close();
        };
        var visBtn = new ModernButton { Text = "Göster/Gizle", Width = 104, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        visBtn.ApplyTheme(_p);
        visBtn.Click += (_, _) => pBox.UseSystemPasswordChar = !pBox.UseSystemPasswordChar;

        footer.Controls.Add(cancel);
        footer.Controls.Add(save);
        footer.Controls.Add(visBtn);
        f.Controls.Add(tbl);
        f.Controls.Add(footer);

        if (f.ShowDialog(this) == DialogResult.OK)
            LoadItems();
    }

    private RoundedTextBox AddField(TableLayoutPanel tbl, string label, string val, bool isPassword = false)
    {
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            ForeColor = _p.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 3)
        });
        var tb = new RoundedTextBox
        {
            Text = val,
            Dock = DockStyle.Top,
            Height = 34,
            UseSystemPasswordChar = isPassword,
            Margin = new Padding(0, 0, 0, 6)
        };
        tb.ApplyTheme(_p);
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(tb);
        return tb;
    }
}

public sealed class ImportDialog : Form
{
    public ImportDialog(BrowserContext ctx, Action onBookmarksImported, ThemePalette? palette = null)
    {
        var p = palette ?? ThemeService.Dark;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Diğer Tarayıcıdan İçe Aktar";
        Size = new Size(470, 310);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = p.BaseBg;
        ForeColor = p.TextPrimary;
        Padding = new Padding(20);
        ThemeService.ApplyNativeTitleBarTheme(Handle, p != ThemeService.Light);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        var bmLbl = new Label
        {
            Text = "Yer İmleri (Chrome, Edge, Firefox HTML veya JSON):",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        var bmRes = new Label { AutoSize = true, ForeColor = p.Green, Font = new Font("Segoe UI", 8.75f), Margin = new Padding(0, 6, 0, 14) };
        var bmBtn = new ModernButton { Text = "Yer İmi Dosyası Seç", Primary = true, Width = 410, Height = 38 };
        bmBtn.ApplyTheme(p);
        bmBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Yer İmi Dosyaları|*.html;*.htm;*.json|Tüm Dosyalar|*.*" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                int count = ImportService.ImportBookmarksFromFile(ofd.FileName, ctx.Bookmarks);
                ctx.Storage.SaveBookmarks(ctx.Bookmarks);
                onBookmarksImported();
                bmRes.ForeColor = p.Green;
                bmRes.Text = $"{count} yer imi başarıyla aktarıldı.";
            }
            catch (Exception ex)
            {
                bmRes.ForeColor = p.Red;
                bmRes.Text = "Hata: " + ex.Message;
            }
        };

        var pwLbl = new Label
        {
            Text = "Şifreler (Chrome / Edge dışa aktarılan CSV):",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        var pwRes = new Label { AutoSize = true, ForeColor = p.Green, Font = new Font("Segoe UI", 8.75f), Margin = new Padding(0, 6, 0, 0) };
        var pwBtn = new ModernButton { Text = "Şifre CSV Dosyası Seç", Width = 410, Height = 38 };
        pwBtn.ApplyTheme(p);
        pwBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "CSV Dosyaları|*.csv|Tüm Dosyalar|*.*" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                int count = ImportService.ImportPasswordsFromCsv(ofd.FileName, ctx.Vault);
                pwRes.ForeColor = p.Green;
                pwRes.Text = $"{count} şifre başarıyla aktarıldı.";
            }
            catch (Exception ex)
            {
                pwRes.ForeColor = p.Red;
                pwRes.Text = "Hata: " + ex.Message;
            }
        };

        stack.Controls.AddRange(new Control[] { bmLbl, bmBtn, bmRes, pwLbl, pwBtn, pwRes });
        Controls.Add(stack);
    }
}

public sealed class ShortcutEditDialog : Form
{
    private static readonly string[] PaletteColors =
    {
        "#38bdf8", "#818cf8", "#34d399", "#f59e0b",
        "#ef4444", "#ec4899", "#0ea5e9", "#64748b"
    };

    private readonly RoundedTextBox _nameBox;
    private readonly RoundedTextBox _urlBox;
    private readonly QuickShortcut? _editing;
    private string _selectedColor;

    public QuickShortcut? ResultShortcut { get; private set; }

    public ShortcutEditDialog(QuickShortcut? editing, ThemePalette? palette = null)
    {
        _editing = editing;
        _selectedColor = editing?.ColorHex ?? PaletteColors[0];
        var p = palette ?? ThemeService.Dark;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = editing is null ? "Site Ekle" : "Site Düzenle";
        Size = new Size(440, 290);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = p.BaseBg;
        ForeColor = p.TextPrimary;
        Padding = new Padding(18);
        ThemeService.ApplyNativeTitleBarTheme(Handle, p != ThemeService.Light);

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _nameBox = new RoundedTextBox { Text = editing?.Name ?? "", PlaceholderText = "Örnek: YouTube", Dock = DockStyle.Top, Height = 36 };
        _nameBox.ApplyTheme(p);

        _urlBox = new RoundedTextBox { Text = editing?.Url ?? "", PlaceholderText = "https://ornek.com", Dock = DockStyle.Top, Height = 36 };
        _urlBox.ApplyTheme(p);

        var colorFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        var colorBtns = new List<Panel>();
        foreach (var hex in PaletteColors)
        {
            var swatch = new Panel
            {
                Size = new Size(28, 28),
                Margin = new Padding(0, 2, 8, 2),
                Cursor = Cursors.Hand,
                Tag = hex
            };
            swatch.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(2, 2, swatch.Width - 4, swatch.Height - 4);
                using var br = new SolidBrush(ColorTranslator.FromHtml(hex));
                e.Graphics.FillEllipse(br, r);
                if (string.Equals(_selectedColor, hex, StringComparison.OrdinalIgnoreCase))
                {
                    using var pen = new Pen(p.TextPrimary, 2.2f);
                    e.Graphics.DrawEllipse(pen, 1, 1, swatch.Width - 2, swatch.Height - 2);
                }
            };
            swatch.Click += (_, _) =>
            {
                _selectedColor = hex;
                foreach (var cb in colorBtns) cb.Invalidate();
            };
            colorBtns.Add(swatch);
            colorFlow.Controls.Add(swatch);
        }

        AddRow(tbl, "Site Adı", _nameBox, p);
        AddRow(tbl, "Adres (URL)", _urlBox, p);
        AddRow(tbl, "Simge Rengi", colorFlow, p);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var cancel = new ModernButton { Text = "İptal", DialogResult = DialogResult.Cancel, Width = 86, Height = 34 };
        cancel.ApplyTheme(p);
        var save = new ModernButton { Text = "Kaydet", Primary = true, Width = 104, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        save.ApplyTheme(p);
        save.Click += (_, _) => SaveAndClose();

        _urlBox.Inner.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) SaveAndClose(); };

        footer.Controls.Add(cancel);
        footer.Controls.Add(save);
        Controls.Add(tbl);
        Controls.Add(footer);
    }

    private static void AddRow(TableLayoutPanel tbl, string label, Control ctl, ThemePalette p)
    {
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            ForeColor = p.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 3)
        });
        ctl.Margin = new Padding(0, 0, 0, 8);
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(ctl);
    }

    private void SaveAndClose()
    {
        var rawUrl = _urlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            MessageBox.Show(this, "Lütfen geçerli bir URL girin.", "Eksik Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rawUrl = "https://" + rawUrl;
        }

        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Uri.TryCreate(rawUrl, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : rawUrl;
        }

        if (_editing is not null)
        {
            _editing.Name = name;
            _editing.Url = rawUrl;
            _editing.ColorHex = _selectedColor;
            ResultShortcut = _editing;
        }
        else
        {
            ResultShortcut = new QuickShortcut
            {
                Name = name,
                Url = rawUrl,
                ColorHex = _selectedColor
            };
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
