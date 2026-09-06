using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grindcrest.Live;

namespace BdoGrindTracker.App.Persistence;

internal sealed record LiveCredential(string Endpoint, string Token);

internal sealed class LiveCredentialStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "BdoGrindTracker", "live-session.dpapi");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Grindcrest/LiveSession/v1");

    public string Load(Uri endpoint)
    {
        if (!File.Exists(_path)) return "";
        if (new FileInfo(_path).Length > 16_384) throw new CryptographicException("Live-Schlüssel ungültig.");
        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var credential = JsonSerializer.Deserialize<LiveCredential>(plaintext);
            return credential is not null && credential.Endpoint == endpoint.AbsoluteUri &&
                LiveEndpoint.IsValidToken(credential.Token) ? credential.Token : "";
        }
        catch (JsonException) { throw new CryptographicException("Live-Schlüssel ungültig."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Save(Uri? endpoint, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            try { File.Delete(_path); }
            catch (DirectoryNotFoundException) { }
            return;
        }
        if (endpoint is null || !LiveEndpoint.IsValidToken(token)) throw new ArgumentException("Live-Schlüssel ungültig.");
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new LiveCredential(endpoint.AbsoluteUri, token));
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, _path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
