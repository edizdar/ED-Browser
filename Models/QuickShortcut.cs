namespace EDBrowser.Models;

public sealed class QuickShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#38bdf8";
}
