using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EDBrowser.Models;

namespace EDBrowser.Services;

/// <summary>
/// Local password vault using AES-256-GCM with a per-user Windows DPAPI-protected 256-bit key.
/// Zero cloud sync; zero hardcoded secret keys.
/// </summary>
public sealed class PasswordVaultService
{
    private readonly StorageService _storage;
    private readonly byte[] _masterKey;
    private List<PasswordEntry> _entries;

    public PasswordVaultService(StorageService storage)
    {
        _storage = storage;
        _masterKey = LoadOrCreateMasterKey();
        _entries = _storage.LoadJson(_storage.PasswordsPath, new List<PasswordEntry>());
    }

    private byte[] LoadOrCreateMasterKey()
    {
        try
        {
            if (File.Exists(_storage.KeyPath))
            {
                var protectedBytes = File.ReadAllBytes(_storage.KeyPath);
                var key = DpapiUnprotect(protectedBytes);
                if (key.Length == 32)
                    return key;
            }
        }
        catch
        {
            // Fall through to generate new key if key file cannot be read
        }

        var freshKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var protectedFresh = DpapiProtect(freshKey);
            File.WriteAllBytes(_storage.KeyPath, protectedFresh);
        }
        catch
        {
            // In-memory fallback if disk write fails
        }
        return freshKey;
    }

    public IReadOnlyList<DecryptedPasswordEntry> GetAll()
    {
        return _entries.Select(e => new DecryptedPasswordEntry
        {
            Id = e.Id,
            Domain = e.Domain,
            Username = e.Username,
            Password = Decrypt(e.EncryptedPassword),
            Folder = string.IsNullOrWhiteSpace(e.Folder) ? "Genel" : e.Folder,
            UpdatedAtUnixMs = e.UpdatedAtUnixMs
        }).ToList();
    }

    public IReadOnlyList<DecryptedPasswordEntry> GetSuggestionsForUrl(string url)
    {
        var host = NormalizeDomain(url);
        if (string.IsNullOrEmpty(host))
            return Array.Empty<DecryptedPasswordEntry>();

        return _entries
            .Where(e =>
            {
                var d = NormalizeDomain(e.Domain);
                return !string.IsNullOrEmpty(d) &&
                       (host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase) ||
                        d.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
            })
            .Select(e => new DecryptedPasswordEntry
            {
                Id = e.Id,
                Domain = e.Domain,
                Username = e.Username,
                Password = Decrypt(e.EncryptedPassword),
                Folder = string.IsNullOrWhiteSpace(e.Folder) ? "Genel" : e.Folder,
                UpdatedAtUnixMs = e.UpdatedAtUnixMs
            })
            .ToList();
    }

    public void SaveOrUpdate(string? id, string domain, string username, string password, string folder = "Genel")
    {
        var normDomain = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normDomain) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return;

        var normFolder = string.IsNullOrWhiteSpace(folder) ? "Genel" : folder.Trim();
        var encrypted = Encrypt(password);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (!string.IsNullOrWhiteSpace(id))
        {
            var byId = _entries.FirstOrDefault(e => e.Id == id);
            if (byId is not null)
            {
                byId.Domain = normDomain;
                byId.Username = username.Trim();
                byId.EncryptedPassword = encrypted;
                byId.Folder = normFolder;
                byId.UpdatedAtUnixMs = now;
                Persist();
                return;
            }
        }

        var existing = _entries.FirstOrDefault(e =>
            e.Domain.Equals(normDomain, StringComparison.OrdinalIgnoreCase) &&
            e.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.EncryptedPassword = encrypted;
            existing.Folder = normFolder;
            existing.UpdatedAtUnixMs = now;
        }
        else
        {
            _entries.Insert(0, new PasswordEntry
            {
                Domain = normDomain,
                Username = username.Trim(),
                EncryptedPassword = encrypted,
                Folder = normFolder,
                UpdatedAtUnixMs = now
            });
        }
        Persist();
    }

    public void Delete(string id)
    {
        _entries.RemoveAll(e => e.Id == id);
        Persist();
    }

    public bool HasCredential(string url, string username)
    {
        var host = NormalizeDomain(url);
        return _entries.Any(e =>
            e.Domain.Equals(host, StringComparison.OrdinalIgnoreCase) &&
            e.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeDomain(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var trimmed = input.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            trimmed = uri.Host;
        else
        {
            trimmed = trimmed.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                             .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
            var slash = trimmed.IndexOf('/');
            if (slash >= 0) trimmed = trimmed[..slash];
        }
        if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..];
        return trimmed.ToLowerInvariant();
    }

    private void Persist() => _storage.SaveJson(_storage.PasswordsPath, _entries);

    private string Encrypt(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_masterKey, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        return $"gcm:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipherBytes)}";
    }

    private string Decrypt(string payload)
    {
        try
        {
            if (!payload.StartsWith("gcm:", StringComparison.Ordinal))
                return string.Empty;
            var parts = payload.Split(':');
            if (parts.Length != 4)
                return string.Empty;
            var nonce = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            var cipher = Convert.FromBase64String(parts[3]);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_masterKey, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }

    #region Windows DPAPI P/Invoke (no extra NuGet dependency)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static byte[] DpapiProtect(byte[] data)
    {
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        try
        {
            inBlob.cbData = data.Length;
            inBlob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            if (!CryptProtectData(ref inBlob, "EDBrowserVault", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    private static byte[] DpapiUnprotect(byte[] data)
    {
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        try
        {
            inBlob.cbData = data.Length;
            inBlob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }
    #endregion
}
