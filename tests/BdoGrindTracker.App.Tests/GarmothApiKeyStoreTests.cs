using System.Security.Cryptography;
using System.Text;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothApiKeyStoreTests
{
    [Fact]
    public void MissingCredentialIsEmptyAndLoadingCreatesNothing()
    {
        using var fixture = new IsolatedKeyPath();
        var store = fixture.CreateFakeStore();

        Assert.Empty(store.Load());
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath));
    }

    [Fact]
    public void InjectedProtectionWritesOnlyCiphertextAndClearsPlaintextBuffers()
    {
        using var fixture = new IsolatedKeyPath();
        byte[]? protectedInput = null;
        byte[]? decryptedOutput = null;
        var store = new GarmothApiKeyStore(fixture.KeyPath,
            bytes => { protectedInput = bytes; return Transform(bytes); },
            bytes => { decryptedOutput = Transform(bytes); return decryptedOutput; });

        store.Save("  synthetic-unit-test-key  ");

        Assert.Equal(Transform(Encoding.UTF8.GetBytes("synthetic-unit-test-key")), File.ReadAllBytes(fixture.KeyPath));
        Assert.NotNull(protectedInput);
        Assert.All(protectedInput, value => Assert.Equal(0, value));
        Assert.Equal("synthetic-unit-test-key", store.Load());
        Assert.NotNull(decryptedOutput);
        Assert.All(decryptedOutput, value => Assert.Equal(0, value));
        Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
    }

    [Fact]
    public void CurrentUserDpapiRoundTripsASyntheticKeyWithoutPlaintextOnDisk()
    {
        using var fixture = new IsolatedKeyPath();
        var key = "synthetic-dpapi-test-" + Guid.NewGuid().ToString("N");
        var store = new GarmothApiKeyStore(fixture.KeyPath);

        store.Save(key);

        var encrypted = File.ReadAllBytes(fixture.KeyPath);
        Assert.NotEqual(Encoding.UTF8.GetBytes(key), encrypted);
        Assert.DoesNotContain(key, Encoding.UTF8.GetString(encrypted));
        Assert.Equal(key, new GarmothApiKeyStore(fixture.KeyPath).Load());
        Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
    }

    [Fact]
    public void EmptySaveForgetsOnlyTheCredentialAndKeepsOrdinarySettings()
    {
        using var fixture = new IsolatedKeyPath();
        var settingsPath = Path.Combine(fixture.DirectoryPath, "settings.json");
        File.WriteAllText(settingsPath, "{}");
        var store = fixture.CreateFakeStore();
        store.Save("synthetic-test-key");

        store.Save(string.Empty);

        Assert.False(File.Exists(fixture.KeyPath));
        Assert.True(File.Exists(settingsPath));
        Assert.Empty(store.Load());
        File.Delete(settingsPath);
    }

    [Fact]
    public void RemovingAKeyFromAMissingDirectoryDoesNotCreateIt()
    {
        using var fixture = new IsolatedKeyPath();
        var missingDirectory = Path.Combine(fixture.DirectoryPath, "not-created");
        var store = new GarmothApiKeyStore(Path.Combine(missingDirectory, "key.dpapi"));

        store.Save(string.Empty);

        Assert.False(Directory.Exists(missingDirectory));
    }

    [Theory]
    [InlineData("bad key")]
    [InlineData("bad\nkey")]
    [InlineData("schlüssel")]
    public void InvalidKeysCannotReplaceAnExistingCredential(string invalidKey)
    {
        using var fixture = new IsolatedKeyPath();
        var store = fixture.CreateFakeStore();
        store.Save("original-test-key");

        var error = Assert.Throws<ArgumentException>(() => store.Save(invalidKey));

        Assert.DoesNotContain(invalidKey, error.Message);
        Assert.Equal("original-test-key", store.Load());
    }

    [Fact]
    public void OversizedKeyIsRejectedBeforeProtection()
    {
        using var fixture = new IsolatedKeyPath();
        var store = new GarmothApiKeyStore(fixture.KeyPath,
            _ => throw new InvalidOperationException("Protection must not run."), Transform);

        Assert.Throws<ArgumentException>(() => store.Save(new string('x', 4097)));
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void CryptographicFailureIsSanitizedAndPreservesOldCiphertext()
    {
        using var fixture = new IsolatedKeyPath();
        fixture.CreateFakeStore().Save("original-test-key");
        const string secret = "synthetic-secret-in-provider-error";
        var store = new GarmothApiKeyStore(fixture.KeyPath,
            _ => throw new CryptographicException(secret), Transform);

        var error = Assert.Throws<CryptographicException>(() => store.Save(secret));

        Assert.DoesNotContain(secret, error.ToString());
        Assert.Equal("original-test-key", fixture.CreateFakeStore().Load());
        Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
    }

    [Fact]
    public void AtomicReplacementFailureLeavesPreviousKeyAndNoTemporaryFiles()
    {
        using var fixture = new IsolatedKeyPath();
        var store = fixture.CreateFakeStore();
        store.Save("original-test-key");
        using (var locked = new FileStream(fixture.KeyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var error = Record.Exception(() => store.Save("replacement-test-key"));
            Assert.True(error is IOException or UnauthorizedAccessException,
                "A sharing-locked credential must produce a typed filesystem failure.");
        }

        Assert.Equal("original-test-key", store.Load());
        Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void CorruptCiphertextLengthFailsBeforeDecryption(int length)
    {
        using var fixture = new IsolatedKeyPath();
        File.WriteAllBytes(fixture.KeyPath, new byte[length]);
        var store = new GarmothApiKeyStore(fixture.KeyPath, Transform,
            _ => throw new InvalidOperationException("Decryption must not run."));

        Assert.Throws<CryptographicException>(() => store.Load());
    }

    [Fact]
    public void BadDpapiDataFailsWithTypedSanitizedError()
    {
        using var fixture = new IsolatedKeyPath();
        File.WriteAllBytes(fixture.KeyPath, [1, 2, 3, 4]);
        var store = new GarmothApiKeyStore(fixture.KeyPath);

        var error = Assert.Throws<CryptographicException>(() => store.Load());

        Assert.Null(error.InnerException);
    }

    [Fact]
    public void InvalidDecryptedTextIsRejectedAndWiped()
    {
        using var fixture = new IsolatedKeyPath();
        File.WriteAllBytes(fixture.KeyPath, [1, 2, 3]);
        var decrypted = new byte[] { 0xff, 0xfe };
        var store = new GarmothApiKeyStore(fixture.KeyPath, Transform, _ => decrypted);

        Assert.Throws<CryptographicException>(() => store.Load());
        Assert.All(decrypted, value => Assert.Equal(0, value));
    }

    private static byte[] Transform(byte[] bytes) => bytes.Select(value => (byte)(value ^ 0xa5)).ToArray();

    private sealed class IsolatedKeyPath : IDisposable
    {
        public IsolatedKeyPath()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            KeyPath = Path.Combine(DirectoryPath, "test-key.dpapi");
        }

        public string DirectoryPath { get; }
        public string KeyPath { get; }
        public GarmothApiKeyStore CreateFakeStore() => new(KeyPath, Transform, Transform);

        public void Dispose()
        {
            if (File.Exists(KeyPath)) File.Delete(KeyPath);
            Directory.Delete(DirectoryPath);
        }
    }
}
