using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EDBrowser.Models;

namespace EDBrowser.Services;

public static partial class ImportService
{
    [GeneratedRegex(@"<DT><A[^>]+HREF=""([^""]+)""[^>]*>([^<]*)</A>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBookmarkRegex();

    public static int ImportBookmarksFromFile(string filePath, List<BookmarkItem> targetBookmarks)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var content = File.ReadAllText(filePath);
        var imported = new List<BookmarkItem>();

        if (ext == ".json")
        {
            using var doc = JsonDocument.Parse(content);
            WalkFirefoxJson(doc.RootElement, imported);
        }
        else
        {
            foreach (Match m in HtmlBookmarkRegex().Matches(content))
            {
                var url = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                var title = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    imported.Add(new BookmarkItem
                    {
                        Type = "bookmark",
                        Name = string.IsNullOrWhiteSpace(title) ? url : title,
                        Url = url
                    });
                }
            }
        }

        if (imported.Count > 0)
        {
            targetBookmarks.Add(new BookmarkItem
            {
                Type = "folder",
                Name = $"İçe Aktarılanlar ({DateTime.Now:dd.MM.yyyy})",
                Children = imported
            });
        }

        return imported.Count;
    }

    private static void WalkFirefoxJson(JsonElement el, List<BookmarkItem> list)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                WalkFirefoxJson(item, list);
            return;
        }
        if (el.ValueKind != JsonValueKind.Object)
            return;

        if (el.TryGetProperty("type", out var tProp) &&
            tProp.GetString() == "bookmark" &&
            el.TryGetProperty("uri", out var uriProp))
        {
            var uri = uriProp.GetString() ?? "";
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var title = el.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? uri : uri;
                list.Add(new BookmarkItem { Type = "bookmark", Name = title, Url = uri });
            }
        }

        if (el.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                WalkFirefoxJson(child, list);
        }
    }

    public static int ImportPasswordsFromCsv(string filePath, PasswordVaultService vault)
    {
        var lines = File.ReadAllLines(filePath);
        int count = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var fields = ParseCsvLine(lines[i]);
            if (fields.Count < 4)
                continue;

            // Chrome/Edge CSV columns: name, url, username, password [, note]
            var name = fields[0].Trim();
            var url = fields[1].Trim();
            var user = fields[2].Trim();
            var pass = fields[3];

            var domain = PasswordVaultService.NormalizeDomain(!string.IsNullOrWhiteSpace(url) ? url : name);
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(pass))
                continue;

            vault.SaveOrUpdate(null, domain, user, pass, "İçe Aktarılan");
            count++;
        }

        return count;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
