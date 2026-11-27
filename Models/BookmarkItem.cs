namespace EDBrowser.Models;

public sealed class BookmarkItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "bookmark"; // "bookmark" | "folder"
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<BookmarkItem> Children { get; set; } = new();

    public bool IsFolder => string.Equals(Type, "folder", StringComparison.OrdinalIgnoreCase);
}
