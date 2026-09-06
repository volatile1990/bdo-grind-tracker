using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed class LiveSharingSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SettingsVersion\":6}")]
    [InlineData("{\"LiveSharingEnabled\":true,\"LiveApiUrl\":\"http://insecure.example\",\"LiveDisplayName\":\"Name\"}")]
    [InlineData("{\"LiveSharingEnabled\":true,\"LiveApiUrl\":null,\"LiveDisplayName\":null}")]
    public void OldOrInvalidSettingsNeverEnablePublicSharing(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.UpgradeDefaults();
        Assert.False(settings.LiveSharingEnabled);
    }

    [Fact]
    public void CredentialIsEncryptedAndBoundToExactApiEndpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "grindcrest-credential-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "live.dpapi");
            var store = new LiveCredentialStore(path);
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var endpoint = new Uri("https://api.example/live/");
            store.Save(endpoint, token);
            Assert.DoesNotContain(token, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(token, store.Load(endpoint));
            Assert.Empty(store.Load(new Uri("https://other.example/live/")));
            Assert.Empty(store.Load(new Uri("https://api.example/other/")));
            store.Save(null, "");
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
