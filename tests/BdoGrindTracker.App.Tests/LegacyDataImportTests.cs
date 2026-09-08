using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed class LegacyDataImportTests
{
    [Fact]
    public void ImportCopiesPersistentStateAndDpapiKeyWithoutChangingSourceOrImportingCaches()
    {
        using var fixture = new Fixture();
        fixture.WriteSource("settings.json", "{\"AutoPauseMinutes\":12}");
        fixture.WriteSource("loot-history-v1.json", "{\"Version\":1,\"Entries\":[]}");
        fixture.WriteSource("window-placement.json", "{\"X\":120}");
        var keyPath = fixture.SourceFile("garmoth-api-key.dpapi");
        var key = "synthetic-migration-key-" + Guid.NewGuid().ToString("N");
        new GarmothApiKeyStore(keyPath).Save(key);
        fixture.WriteSource("update-settings.json", "{\"IsBeta\":true}");
        fixture.WriteSource("market-prices-v1.json", "cache");
        fixture.WriteSource("webview2/Default/Cookies", "cache");
        fixture.WriteSource("diagnostics/recording.json", "debug");
        fixture.WriteSource(".settings.tmp", "partial");
        var before = fixture.SnapshotSource();

        LegacyDataImport.Import(fixture.Source, fixture.Destination);

        foreach (var name in new[] { "settings.json", "loot-history-v1.json", "window-placement.json", "garmoth-api-key.dpapi" })
            Assert.Equal(File.ReadAllBytes(fixture.SourceFile(name)), File.ReadAllBytes(fixture.TargetFile(name)));
        Assert.Equal(key, new GarmothApiKeyStore(fixture.TargetFile("garmoth-api-key.dpapi")).Load());
        Assert.Equal(12, new SettingsStore(fixture.Destination).Load().AutoPauseMinutes);
        Assert.Equal(5, Directory.GetFiles(fixture.Destination, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetDirectories(fixture.Destination));
        fixture.AssertSourceUnchanged(before);
    }

    [Fact]
    public void CompletedImportDoesNotBringBackDeletedStoreFilesOrLaterLegacyChanges()
    {
        using var fixture = new Fixture();
        fixture.WriteSource("settings.json", "legacy settings");
        fixture.WriteSource("garmoth-api-key.dpapi", "legacy ciphertext");
        LegacyDataImport.Import(fixture.Source, fixture.Destination);
        File.WriteAllText(fixture.TargetFile("settings.json"), "Store settings");
        File.Delete(fixture.TargetFile("garmoth-api-key.dpapi"));
        fixture.WriteSource("loot-history-v1.json", "later legacy session");

        LegacyDataImport.Import(fixture.Source, fixture.Destination);

        Assert.Equal("Store settings", File.ReadAllText(fixture.TargetFile("settings.json")));
        Assert.False(File.Exists(fixture.TargetFile("garmoth-api-key.dpapi")));
        Assert.False(File.Exists(fixture.TargetFile("loot-history-v1.json")));
    }

    [Fact]
    public void ExistingStoreDataAlwaysWinsWhileMissingFilesAreImported()
    {
        using var fixture = new Fixture();
        fixture.WriteSource("settings.json", "legacy settings");
        fixture.WriteSource("garmoth-api-key.dpapi", "legacy ciphertext");
        Directory.CreateDirectory(fixture.Destination);
        File.WriteAllText(fixture.TargetFile("settings.json"), "Store settings");
        var before = fixture.SnapshotSource();

        LegacyDataImport.Import(fixture.Source, fixture.Destination);

        Assert.Equal("Store settings", File.ReadAllText(fixture.TargetFile("settings.json")));
        Assert.Equal("legacy ciphertext", File.ReadAllText(fixture.TargetFile("garmoth-api-key.dpapi")));
        fixture.AssertSourceUnchanged(before);
    }

    [Fact]
    public void LockedSourceFailsVisiblyAndRetryCompletesWithoutReplacingAlreadyImportedFiles()
    {
        using var fixture = new Fixture();
        fixture.WriteSource("settings.json", "legacy settings");
        fixture.WriteSource("loot-history-v1.json", "legacy history");
        fixture.WriteSource("garmoth-api-key.dpapi", "legacy ciphertext");
        var before = fixture.SnapshotSource();
        using (var locked = new FileStream(fixture.SourceFile("loot-history-v1.json"), FileMode.Open,
            FileAccess.Read, FileShare.None))
        {
            var error = Assert.Throws<IOException>(() => LegacyDataImport.Import(fixture.Source, fixture.Destination));
            Assert.Contains("nicht vollständig", error.Message);
        }
        Assert.False(File.Exists(fixture.TargetFile(LegacyDataImport.CompletionFile)));
        Assert.Empty(Directory.GetFiles(fixture.Destination, "*.tmp"));
        File.WriteAllText(fixture.TargetFile("settings.json"), "existing Store settings");

        LegacyDataImport.Import(fixture.Source, fixture.Destination);

        Assert.Equal("existing Store settings", File.ReadAllText(fixture.TargetFile("settings.json")));
        Assert.Equal("legacy history", File.ReadAllText(fixture.TargetFile("loot-history-v1.json")));
        Assert.Equal("legacy ciphertext", File.ReadAllText(fixture.TargetFile("garmoth-api-key.dpapi")));
        Assert.True(File.Exists(fixture.TargetFile(LegacyDataImport.CompletionFile)));
        fixture.AssertSourceUnchanged(before);
    }

    [Fact]
    public void MissingLegacyInstallationCompletesWithoutCreatingIt()
    {
        using var fixture = new Fixture();

        LegacyDataImport.Import(fixture.Source, fixture.Destination);

        Assert.False(Directory.Exists(fixture.Source));
        Assert.Single(Directory.GetFiles(fixture.Destination));
    }

    [Fact]
    public void DestinationDirectoryInPlaceOfAFileStopsImportWithoutOverwritingAnything()
    {
        using var fixture = new Fixture();
        fixture.WriteSource("settings.json", "legacy settings");
        Directory.CreateDirectory(fixture.TargetFile("settings.json"));
        var before = fixture.SnapshotSource();

        Assert.Throws<IOException>(() => LegacyDataImport.Import(fixture.Source, fixture.Destination));

        Assert.True(Directory.Exists(fixture.TargetFile("settings.json")));
        Assert.False(File.Exists(fixture.TargetFile(LegacyDataImport.CompletionFile)));
        fixture.AssertSourceUnchanged(before);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Grindcrest.DataImport", Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_root, "legacy");
        public string Destination => Path.Combine(_root, "store");
        public string SourceFile(string name) => Path.Combine(Source, name);
        public string TargetFile(string name) => Path.Combine(Destination, name);
        public void WriteSource(string name, string content)
        {
            var path = SourceFile(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public Dictionary<string, (byte[] Bytes, DateTime Modified)> SnapshotSource() => Directory.GetFiles(Source, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));
        public void AssertSourceUnchanged(Dictionary<string, (byte[] Bytes, DateTime Modified)> before)
        {
            Assert.Equal(before.Count, Directory.GetFiles(Source, "*", SearchOption.AllDirectories).Length);
            foreach (var (path, previous) in before)
            {
                Assert.Equal(previous.Bytes, File.ReadAllBytes(path));
                Assert.Equal(previous.Modified, File.GetLastWriteTimeUtc(path));
            }
        }
        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
