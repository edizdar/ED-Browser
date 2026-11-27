namespace EDBrowser.Models;

public sealed class DownloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public string State { get; set; } = "InProgress"; // InProgress | Completed | Interrupted
    public long StartedAtUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
