using System.Diagnostics;
using EDBrowser.Models;
using EDBrowser.Services;

namespace EDBrowser.UI;

public sealed class SidePanelHost : Panel
{
    private readonly Label _titleLabel;
    private readonly ModernButton _actionBtn;
    private readonly IconButton _closeBtn;
    private readonly Panel _header;
    private readonly Panel _body;

    private string _activeMode = string.Empty;
    private readonly BrowserContext _ctx;
    private ThemePalette _palette = ThemeService.Dark;
    private Action? _currentAction;

    public event Action<string>? NavigateRequested;
    public event Action<string>? OpenNewTabRequested;
    public event Action? BookmarksChanged;

    public string ActiveMode => Visible ? _activeMode : string.Empty;

    public SidePanelHost(BrowserContext ctx)
    {
        _ctx = ctx;
        Dock = DockStyle.Right;
        Width = 360;
        Visible = false;
        Padding = new Padding(1, 0, 0, 0);

        _header = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(14, 8, 10, 8) };
        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _actionBtn = new ModernButton
        {
            Dock = DockStyle.Right,
            Width = 92,
            Height = 30,
            Visible = false,
            Margin = new Padding(0, 0, 6, 0)
        };
        _actionBtn.Click += (_, _) => _currentAction?.Invoke();

        _closeBtn = new IconButton
        {
            IconType = VectorIcon.Close,
            Dock = DockStyle.Right,
            Size = new Size(32, 32),
            IconSizePx = 13
        };
        _closeBtn.Click += (_, _) => HidePanel();

        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_actionBtn);
        _header.Controls.Add(_closeBtn);

        _body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };

        Controls.Add(_body);
        Controls.Add(_header);
    }

    public void Toggle(string mode)
    {
        if (Visible && _activeMode == mode)
        {
            HidePanel();
            return;
        }
        ShowMode(mode);
    }

    public void ShowMode(string mode)
    {
        _activeMode = mode;
        Visible = true;
        RefreshCurrent();
    }

    public void HidePanel()
    {
        Visible = false;
        _activeMode = string.Empty;
    }

    public void RefreshIfOpen(string mode)
    {
        if (Visible && _activeMode == mode)
            RefreshCurrent();
    }

    public void ApplyTheme(ThemePalette p)
    {
        _palette = p;
        BackColor = p.Border;
        _header.BackColor = p.SurfaceBg;
        _body.BackColor = p.SurfaceBg;
        _titleLabel.ForeColor = p.TextPrimary;
        _closeBtn.NormalBg = p.SurfaceBg;
        _closeBtn.HoverBg = p.HoverBg;
        _closeBtn.IconColor = p.TextSecondary;
        _actionBtn.ApplyTheme(p);
        if (Visible) RefreshCurrent();
    }

    private void RefreshCurrent()
    {
        _body.Controls.Clear();
        _actionBtn.Visible = false;

        switch (_activeMode)
        {
            case "bm":
                RenderBookmarks();
                break;
            case "dl":
                RenderDownloads();
                break;
            case "hist":
                RenderHistory();
                break;
        }
    }

    private void RenderBookmarks()
    {
        _titleLabel.Text = "Yer İmleri";
        SetActionButton("Yeni Klasör", () =>
        {
            var name = PromptDialog.Show(FindForm(), "Yeni Klasör", "Klasör adı:", "", _palette);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _ctx.Bookmarks.Add(new BookmarkItem { Type = "folder", Name = name.Trim() });
                _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                BookmarksChanged?.Invoke();
                RenderBookmarks();
            }
        });

        if (_ctx.Bookmarks.Count == 0)
        {
            AddEmptyLabel("Henüz yer imi eklenmemiş.");
            return;
        }

        var tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = _palette.SurfaceBg,
            ForeColor = _palette.TextPrimary,
            Font = new Font("Segoe UI", 9.5f),
            ItemHeight = 26,
            ShowLines = true,
            FullRowSelect = true,
            AllowDrop = true
        };

        void Populate(List<BookmarkItem> items, TreeNodeCollection nodes)
        {
            foreach (var item in items)
            {
                var label = item.IsFolder ? $"📁  {item.Name}" : $"🔖  {(string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name)}";
                var tn = new TreeNode(label) { Tag = item };
                if (item.IsFolder)
                    Populate(item.Children, tn.Nodes);
                nodes.Add(tn);
            }
        }

        Populate(_ctx.Bookmarks, tree.Nodes);
        tree.ExpandAll();

        var ctxMenu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette) };
        tree.NodeMouseClick += (_, e) =>
        {
            if (e.Node is null) return;
            tree.SelectedNode = e.Node;
            if (e.Button == MouseButtons.Left && e.Node.Tag is BookmarkItem bm && !bm.IsFolder)
            {
                NavigateRequested?.Invoke(bm.Url);
                HidePanel();
            }
            else if (e.Button == MouseButtons.Right && e.Node.Tag is BookmarkItem rightBm)
            {
                ctxMenu.Items.Clear();
                if (!rightBm.IsFolder)
                {
                    ctxMenu.Items.Add("Aç", null, (_, _) => NavigateRequested?.Invoke(rightBm.Url));
                    ctxMenu.Items.Add("URL Kopyala", null, (_, _) => Clipboard.SetText(rightBm.Url));
                    ctxMenu.Items.Add("Düzenle", null, (_, _) =>
                    {
                        using var dlg = new BookmarkEditDialog(_ctx.Bookmarks, rightBm, palette: _palette);
                        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                        {
                            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                            BookmarksChanged?.Invoke();
                            RenderBookmarks();
                        }
                    });
                }
                else
                {
                    ctxMenu.Items.Add("Yeniden Adlandır", null, (_, _) =>
                    {
                        var n = PromptDialog.Show(FindForm(), "Klasörü Yeniden Adlandır", "Yeni ad:", rightBm.Name, _palette);
                        if (!string.IsNullOrWhiteSpace(n))
                        {
                            rightBm.Name = n.Trim();
                            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                            BookmarksChanged?.Invoke();
                            RenderBookmarks();
                        }
                    });
                }
                ctxMenu.Items.Add("Sil", null, (_, _) =>
                {
                    RemoveBookmarkById(_ctx.Bookmarks, rightBm.Id);
                    _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
                    BookmarksChanged?.Invoke();
                    RenderBookmarks();
                });
                ctxMenu.Show(tree, e.Location);
            }
        };

        tree.ItemDrag += (_, e) => { if (e.Item is TreeNode tn) tree.DoDragDrop(tn, DragDropEffects.Move); };
        tree.DragEnter += (_, e) => e.Effect = DragDropEffects.Move;
        tree.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode draggedNode) return;
            if (draggedNode.Tag is not BookmarkItem draggedItem) return;

            var pt = tree.PointToClient(new Point(e.X, e.Y));
            var targetNode = tree.GetNodeAt(pt);
            if (targetNode == draggedNode) return;

            if (targetNode?.Tag is BookmarkItem targetItem)
            {
                if (draggedItem.Id == targetItem.Id || IsDescendant(draggedItem, targetItem.Id))
                    return;

                RemoveBookmarkById(_ctx.Bookmarks, draggedItem.Id);
                if (targetItem.IsFolder)
                    targetItem.Children.Add(draggedItem);
                else
                {
                    var list = FindParentList(_ctx.Bookmarks, targetItem.Id) ?? _ctx.Bookmarks;
                    var idx = list.FindIndex(x => x.Id == targetItem.Id);
                    list.Insert(Math.Max(0, idx), draggedItem);
                }
            }
            else
            {
                RemoveBookmarkById(_ctx.Bookmarks, draggedItem.Id);
                _ctx.Bookmarks.Add(draggedItem);
            }

            _ctx.Storage.SaveBookmarks(_ctx.Bookmarks);
            BookmarksChanged?.Invoke();
            RenderBookmarks();
        };

        _body.Controls.Add(tree);
    }

    private void RenderDownloads()
    {
        _titleLabel.Text = "İndirilenler";
        SetActionButton("Temizle", () =>
        {
            _ctx.Downloads.Clear();
            _ctx.Storage.SaveDownloads(_ctx.Downloads);
            RenderDownloads();
        });

        if (_ctx.Downloads.Count == 0)
        {
            AddEmptyLabel("İndirme kaydı yok.");
            return;
        }

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        foreach (var dl in _ctx.Downloads.Take(60))
        {
            var card = new ModernCardPanel
            {
                Width = 320,
                Height = 76,
                FillColor = _palette.ElevatedBg,
                BorderColor = _palette.Border,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(10)
            };

            var pct = dl.TotalBytes > 0 ? (int)(dl.ReceivedBytes * 100 / dl.TotalBytes) : 0;
            var status = dl.State == "Completed"
                ? $"Tamamlandı • {FormatBytes(dl.TotalBytes > 0 ? dl.TotalBytes : dl.ReceivedBytes)}"
                : dl.State == "Interrupted" ? "İptal edildi / Hata" : $"İndiriliyor • %{pct}";

            var nameLbl = new Label
            {
                Text = dl.FileName,
                Location = new Point(10, 8),
                Size = new Size(300, 20),
                ForeColor = _palette.TextPrimary,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                AutoEllipsis = true
            };
            var infoLbl = new Label
            {
                Text = status,
                Location = new Point(10, 30),
                Size = new Size(168, 18),
                ForeColor = dl.State == "Completed" ? _palette.Green : dl.State == "Interrupted" ? _palette.Red : _palette.Accent,
                Font = new Font("Segoe UI", 8.25f)
            };

            card.Controls.Add(nameLbl);
            card.Controls.Add(infoLbl);

            if (dl.State == "Completed")
            {
                var openBtn = new ModernButton { Text = "Aç", Location = new Point(176, 38), Size = new Size(62, 26), Font = new Font("Segoe UI", 8.25f, FontStyle.Bold) };
                openBtn.ApplyTheme(_palette);
                openBtn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(dl.FullPath) { UseShellExecute = true }); } catch { } };

                var folderBtn = new ModernButton { Text = "Klasör", Location = new Point(242, 38), Size = new Size(68, 26), Font = new Font("Segoe UI", 8.25f, FontStyle.Bold) };
                folderBtn.ApplyTheme(_palette);
                folderBtn.Click += (_, _) => { try { Process.Start("explorer.exe", $"/select,\"{dl.FullPath}\""); } catch { } };

                card.Controls.Add(openBtn);
                card.Controls.Add(folderBtn);
            }

            stack.Controls.Add(card);
        }

        _body.Controls.Add(stack);
    }

    private void RenderHistory()
    {
        _titleLabel.Text = "Geçmiş";
        SetActionButton("Tümünü Sil", () =>
        {
            if (MessageBox.Show("Tüm gezinme geçmişi silinsin mi?", "Geçmişi Temizle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _ctx.History.Clear();
                _ctx.Storage.SaveHistory(_ctx.History);
                RenderHistory();
            }
        });

        if (_ctx.History.Count == 0)
        {
            AddEmptyLabel("Gezinme geçmişi boş.");
            return;
        }

        var searchWrap = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(0, 0, 0, 10)
        };
        var searchBox = new RoundedTextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Geçmişte ara…"
        };
        searchBox.ApplyTheme(_palette, elevated: true);
        searchWrap.Controls.Add(searchBox);

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = _palette.SurfaceBg,
            ForeColor = _palette.TextPrimary,
            Font = new Font("Segoe UI", 9f),
            ItemHeight = 48,
            DrawMode = DrawMode.OwnerDrawFixed
        };

        var displayed = new List<HistoryEntry>();
        int hoverIdx = -1;
        bool hoverDel = false;

        Rectangle GetDelRect(Rectangle itemBounds) =>
            new(itemBounds.Right - 30, itemBounds.Y + (itemBounds.Height - 24) / 2, 24, 24);

        void RebuildItems()
        {
            int topIdx = list.TopIndex;
            var q = searchBox.Text.Trim();
            var filtered = _ctx.History
                .Where(h => string.IsNullOrEmpty(q) ||
                            h.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            h.Url.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(250)
                .ToList();

            list.BeginUpdate();
            list.Items.Clear();
            displayed.Clear();
            displayed.AddRange(filtered);
            foreach (var h in filtered)
                list.Items.Add(h);
            if (topIdx >= 0 && topIdx < list.Items.Count)
                list.TopIndex = topIdx;
            list.EndUpdate();
        }

        searchBox.Inner.TextChanged += (_, _) => RebuildItems();

        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= displayed.Count) return;
            var h = displayed[e.Index];
            bool hovered = e.Index == hoverIdx;
            using var bg = new SolidBrush(hovered ? _palette.HoverBg : _palette.SurfaceBg);
            e.Graphics.FillRectangle(bg, e.Bounds);

            int textRightPad = hovered ? 36 : 10;
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(h.VisitedAtUnixMs).LocalDateTime.ToString("dd.MM HH:mm");
            TextRenderer.DrawText(e.Graphics, h.Title, new Font("Segoe UI", 9.25f),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 4, e.Bounds.Width - textRightPad - 6, 20),
                _palette.TextPrimary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, $"{dt}  •  {h.Url}", new Font("Segoe UI", 8f),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 24, e.Bounds.Width - textRightPad - 6, 20),
                _palette.TextMuted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (hovered)
            {
                var delR = GetDelRect(e.Bounds);
                if (hoverDel)
                {
                    using var dbr = new SolidBrush(Color.FromArgb(65, _palette.Red));
                    e.Graphics.FillEllipse(dbr, delR);
                }
                IconPainter.Draw(e.Graphics, VectorIcon.Close,
                    new RectangleF(delR.X + 6, delR.Y + 6, delR.Width - 12, delR.Height - 12),
                    hoverDel ? _palette.Red : _palette.TextSecondary, 1.5f);
            }
        };

        list.MouseMove += (_, e) =>
        {
            int idx = list.IndexFromPoint(e.Location);
            bool del = idx >= 0 && idx < displayed.Count && GetDelRect(list.GetItemRectangle(idx)).Contains(e.Location);
            if (idx != hoverIdx || del != hoverDel)
            {
                hoverIdx = idx;
                hoverDel = del;
                list.Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                list.Invalidate();
            }
        };
        list.MouseLeave += (_, _) =>
        {
            if (hoverIdx != -1)
            {
                hoverIdx = -1;
                hoverDel = false;
                list.Invalidate();
            }
        };

        list.MouseUp += (_, e) =>
        {
            int idx = list.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= displayed.Count) return;
            var entry = displayed[idx];

            if (e.Button == MouseButtons.Left)
            {
                if (GetDelRect(list.GetItemRectangle(idx)).Contains(e.Location))
                {
                    _ctx.History.Remove(entry);
                    _ctx.Storage.SaveHistory(_ctx.History);
                    if (_ctx.History.Count == 0) RenderHistory();
                    else RebuildItems();
                }
                else
                {
                    NavigateRequested?.Invoke(entry.Url);
                    HidePanel();
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                OpenNewTabRequested?.Invoke(entry.Url);
            }
            else if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(_palette), ShowImageMargin = false, Padding = new Padding(4) };
                menu.Items.Add("Aç", null, (_, _) => { NavigateRequested?.Invoke(entry.Url); HidePanel(); });
                menu.Items.Add("Yeni Sekmede Aç", null, (_, _) => OpenNewTabRequested?.Invoke(entry.Url));
                menu.Items.Add("URL Kopyala", null, (_, _) => Clipboard.SetText(entry.Url));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Bu Kaydı Sil", null, (_, _) =>
                {
                    _ctx.History.Remove(entry);
                    _ctx.Storage.SaveHistory(_ctx.History);
                    if (_ctx.History.Count == 0) RenderHistory();
                    else RebuildItems();
                });

                if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var hostUri) && !string.IsNullOrEmpty(hostUri.Host))
                {
                    var host = hostUri.Host;
                    menu.Items.Add($"'{host}' Geçmişini Sil", null, (_, _) =>
                    {
                        _ctx.History.RemoveAll(x =>
                            Uri.TryCreate(x.Url, UriKind.Absolute, out var u) &&
                            u.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
                        _ctx.Storage.SaveHistory(_ctx.History);
                        if (_ctx.History.Count == 0) RenderHistory();
                        else RebuildItems();
                    });
                }
                menu.Show(list, e.Location);
            }
        };

        RebuildItems();
        _body.Controls.Add(list);
        _body.Controls.Add(searchWrap);
    }

    private void SetActionButton(string text, Action onClick)
    {
        _actionBtn.Text = text;
        _actionBtn.FitWidthToText(84, 24);
        _actionBtn.Visible = true;
        _currentAction = onClick;
    }

    private void AddEmptyLabel(string text)
    {
        _body.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 90,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = _palette.TextMuted,
            Font = new Font("Segoe UI", 9.5f)
        });
    }

    public static bool RemoveBookmarkById(List<BookmarkItem> list, string id)
    {
        int idx = list.FindIndex(x => x.Id == id);
        if (idx >= 0) { list.RemoveAt(idx); return true; }
        foreach (var item in list)
        {
            if (item.IsFolder && RemoveBookmarkById(item.Children, id))
                return true;
        }
        return false;
    }

    private static List<BookmarkItem>? FindParentList(List<BookmarkItem> list, string id)
    {
        foreach (var item in list)
        {
            if (item.Id == id) return list;
            if (item.IsFolder)
            {
                var found = FindParentList(item.Children, id);
                if (found is not null) return found;
            }
        }
        return null;
    }

    public static bool IsDescendant(BookmarkItem node, string targetId)
    {
        foreach (var c in node.Children)
        {
            if (c.Id == targetId || IsDescendant(c, targetId))
                return true;
        }
        return false;
    }

    private static string FormatBytes(long b) =>
        b < 1024 ? $"{b} B" :
        b < 1048576 ? $"{b / 1024.0:F1} KB" :
        b < 1073741824 ? $"{b / 1048576.0:F1} MB" :
        $"{b / 1073741824.0:F2} GB";
}
