namespace EDBrowser.Models;

public sealed class InstalledExtension
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = string.Empty;
    public string RuntimeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string SourceStore { get; set; } = "Yerel";
    public string PopupPath { get; set; } = string.Empty;
    public string OptionsPath { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public long InstalledAtUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
