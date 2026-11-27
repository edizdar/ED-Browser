namespace EDBrowser.Models;

public sealed class PasswordEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Domain { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string Folder { get; set; } = "Genel";
    public long UpdatedAtUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class DecryptedPasswordEntry
{
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Folder { get; set; } = "Genel";
    public long UpdatedAtUnixMs { get; set; }
}
