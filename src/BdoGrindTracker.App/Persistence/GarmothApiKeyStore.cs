using System.Security.Cryptography;
using System.Text;

namespace BdoGrindTracker.App.Persistence;

/// <summary>
/// Persists only CurrentUser-DPAPI ciphertext, separate from ordinary settings.
/// Decryption is tied to this Windows account. No Companion credentials, browser
/// cookies, or other credential stores are consulted.
/// </summary>
internal sealed class GarmothApiKeyStore
{
    public const int MaximumApiKeyLength = 4096;
    private const int MaximumProtectedLength = 65_536;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BdoGrindTracker/GarmothApiKey/v1");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _keyPath;
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;

    public GarmothApiKeyStore(
        string? keyPath = null,
        Func<byte[], byte[]>? protect = null,
        Func<byte[], byte[]>? unprotect = null)
    {
        _keyPath = Path.GetFullPath(keyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BdoGrindTracker", "garmoth-api-key.dpapi"));
        _protect = protect ?? (bytes => ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        _unprotect = unprotect ?? (bytes => ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
    }

    public static bool IsValidApiKey(string? apiKey) =>
        apiKey is { Length: > 0 and <= MaximumApiKeyLength } &&
        apiKey.All(static character => character is >= (char)0x21 and <= (char)0x7e);

    public string Load()
    {
        byte[] encrypted;
        try
        {
            using var stream = new FileStream(_keyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaximumProtectedLength)
                throw new CryptographicException("Der gespeicherte Garmoth-Schlüssel ist ungültig.");
            encrypted = new byte[checked((int)stream.Length)];
            stream.ReadExactly(encrypted);
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return string.Empty;
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = _unprotect(encrypted);
            if (plaintext is null || plaintext.Length > MaximumApiKeyLength)
                throw new CryptographicException();
            var apiKey = StrictUtf8.GetString(plaintext);
            if (!IsValidApiKey(apiKey))
                throw new CryptographicException();
            return apiKey;
        }
        catch (Exception exception) when (exception is CryptographicException or DecoderFallbackException)
        {
            // Never retain provider messages/inner exceptions that might include
            // a credential. The UI can handle this explicit failure category.
            throw new CryptographicException("Der Garmoth-Schlüssel konnte für dieses Windows-Konto nicht entschlüsselt werden.");
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        var normalized = apiKey.Trim();
        if (normalized.Length == 0)
        {
            // An empty explicitly saved value means forgetting this exact key.
            try { File.Delete(_keyPath); }
            catch (DirectoryNotFoundException) { }
            return;
        }
        if (!IsValidApiKey(normalized))
            throw new ArgumentException("Der API-Schlüssel muss aus 1 bis 4096 sichtbaren ASCII-Zeichen ohne Leerzeichen bestehen.", nameof(apiKey));

        var plaintext = StrictUtf8.GetBytes(normalized);
        byte[] encrypted;
        try
        {
            encrypted = _protect(plaintext);
            if (encrypted is null || encrypted.Length is <= 0 or > MaximumProtectedLength)
                throw new CryptographicException();
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Der Garmoth-Schlüssel konnte nicht für dieses Windows-Konto geschützt werden.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var directory = Path.GetDirectoryName(_keyPath)
            ?? throw new IOException("Der Speicherort für den geschützten Schlüssel ist ungültig.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".garmoth-key-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _keyPath, overwrite: true);
        }
        finally
        {
            // The temporary file contains only ciphertext. Clean up a failed
            // atomic replacement without masking the original I/O failure.
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
