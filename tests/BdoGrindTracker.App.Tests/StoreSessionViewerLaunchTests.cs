using System.Text.Json;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.Tests;

public sealed class StoreSessionViewerLaunchTests
{
    [Fact]
    public void AdjacentConfigurationSelectsViewerOnOrdinaryUnpackagedLaunch()
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(fixture.SourceDirectory);

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, AppPackageIdentity.Unpackaged, []);

        Assert.Equal(fixture.SourceDirectory, result);
        Assert.False(File.Exists(Path.Combine(fixture.SourceDirectory, "current-session-v1.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void EnvironmentAloneDoesNotTurnOrdinaryUnpackagedLaunchIntoViewer()
    {
        using var fixture = new Fixture();

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, AppPackageIdentity.Unpackaged, [], fixture.SourceDirectory);

        Assert.Null(result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.LaunchDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void ExplicitViewerArgumentSelectsEnvironmentWithoutReadingIrrelevantConfiguration()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationPath, "{broken");

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, AppPackageIdentity.Unpackaged,
            ["--VIEW-STORE-SESSION"], fixture.SourceDirectory);

        Assert.Equal(fixture.SourceDirectory, result);
        Assert.Equal("{broken", File.ReadAllText(fixture.ConfigurationPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExplicitViewerArgumentFallsBackToAdjacentConfiguration(string? environment)
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(fixture.SourceDirectory);

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, AppPackageIdentity.Unpackaged,
            [StoreSessionViewerLaunch.ViewerArgument], environment);

        Assert.Equal(fixture.SourceDirectory, result);
    }

    [Fact]
    public void ExplicitViewerArgumentWithoutSourceFailsInsteadOfOpeningNormalUserData()
    {
        using var fixture = new Fixture();

        var error = Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [StoreSessionViewerLaunch.ViewerArgument], null));

        Assert.Contains("fehlt der Datenordner", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.LaunchDirectory));
    }

    [Theory]
    [InlineData((int)AppPackageIdentity.Packaged)]
    [InlineData((int)AppPackageIdentity.Unknown)]
    public void NonLocalLaunchIgnoresEvenAnUnreadableAdjacentMarker(int identity)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationPath, "{broken");
        using var locked = new FileStream(fixture.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, (AppPackageIdentity)identity, [], fixture.SourceDirectory);

        Assert.Null(result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Theory]
    [InlineData((int)AppPackageIdentity.Packaged)]
    [InlineData((int)AppPackageIdentity.Unknown)]
    public void NonLocalLaunchRejectsExplicitViewerModeBeforeReadingTheMarker(int identity)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationPath, "{broken");
        using var locked = new FileStream(fixture.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            (AppPackageIdentity)identity, [StoreSessionViewerLaunch.ViewerArgument], fixture.SourceDirectory));

        Assert.Contains("nur mit einer lokalen", error.Message);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"DataDirectory\":\"relative-directory\"}")]
    [InlineData("{\"DataDirectory\":123}")]
    public void InvalidAdjacentConfigurationCannotFallBackToNormalTracker(string json)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationPath, json);

        Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [], null));

        Assert.Equal(json, File.ReadAllText(fixture.ConfigurationPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void LockedConfigurationFailsWithoutWritingOrFallingBack()
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(fixture.SourceDirectory);
        using var locked = new FileStream(fixture.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [], null));

        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void SourceMustExistEvenWhenExplicitlySelectedThroughTheEnvironment()
    {
        using var fixture = new Fixture();

        Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [StoreSessionViewerLaunch.ViewerArgument], Path.Combine(fixture.SourceDirectory, "missing")));
        Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [StoreSessionViewerLaunch.ViewerArgument], "relative-directory"));

        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void ExplicitTakeoverArgumentUsesEnvironmentSourceBeforeAdjacentViewerConfiguration()
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(Path.Combine(fixture.SourceDirectory, "missing"));

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, AppPackageIdentity.Unpackaged,
            ["--TAKE-OVER-STORE-SESSION"], fixture.SourceDirectory);

        Assert.Equal(fixture.SourceDirectory, result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExplicitTakeoverArgumentCanUseTheDefaultViewerMarkerSource(string? environment)
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(fixture.SourceDirectory);
        var originalConfiguration = File.ReadAllText(fixture.ConfigurationPath);

        var result = StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory, AppPackageIdentity.Unpackaged,
            [StoreSessionViewerLaunch.TakeoverArgument], environment);

        Assert.Equal(fixture.SourceDirectory, result);
        Assert.Equal(originalConfiguration, File.ReadAllText(fixture.ConfigurationPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void ExplicitTakeoverArgumentWithoutSourceDoesNotFallBackToNormalTrackerData()
    {
        using var fixture = new Fixture();

        var error = Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [StoreSessionViewerLaunch.TakeoverArgument], null));

        Assert.Contains("fehlt der Datenordner", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.LaunchDirectory));
    }

    [Theory]
    [InlineData((int)AppPackageIdentity.Packaged)]
    [InlineData((int)AppPackageIdentity.Unknown)]
    public void NonLocalLaunchRejectsTakeoverBeforeReadingTheViewerMarker(int identity)
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(fixture.SourceDirectory);
        using var locked = new FileStream(fixture.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            (AppPackageIdentity)identity, [StoreSessionViewerLaunch.TakeoverArgument], fixture.SourceDirectory));

        Assert.Contains("nur mit einer lokalen", error.Message);
    }

    [Theory]
    [InlineData((int)AppPackageIdentity.Unpackaged)]
    [InlineData((int)AppPackageIdentity.Packaged)]
    [InlineData((int)AppPackageIdentity.Unknown)]
    public void ViewerAndTakeoverFlagsAreMutuallyExclusiveEvenWithAValidSource(int identity)
    {
        using var fixture = new Fixture();
        fixture.WriteConfiguration(fixture.SourceDirectory);
        using var locked = new FileStream(fixture.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            (AppPackageIdentity)identity, [StoreSessionViewerLaunch.ViewerArgument, "--TAKE-OVER-STORE-SESSION"], fixture.SourceDirectory));

        Assert.Contains("nicht gleichzeitig", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.SourceDirectory));
    }

    [Fact]
    public void OversizedConfigurationFailsBeforeParsing()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigurationPath, new string(' ', 16 * 1024 + 1));

        Assert.Throws<IOException>(() => StoreSessionViewerLaunch.Resolve(fixture.LaunchDirectory,
            AppPackageIdentity.Unpackaged, [], null));

        Assert.Equal(16 * 1024 + 1, new FileInfo(fixture.ConfigurationPath).Length);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Grindcrest.ViewerLaunch.Tests", Guid.NewGuid().ToString("N"));
        public string LaunchDirectory { get; }
        public string SourceDirectory { get; }
        public string ConfigurationPath => Path.Combine(LaunchDirectory, StoreSessionViewerLaunch.ConfigurationFileName);
        public Fixture()
        {
            LaunchDirectory = Directory.CreateDirectory(Path.Combine(_root, "local-build")).FullName;
            SourceDirectory = Directory.CreateDirectory(Path.Combine(_root, "store-source")).FullName;
        }
        public void WriteConfiguration(string source) => File.WriteAllText(ConfigurationPath,
            JsonSerializer.Serialize(new { DataDirectory = source }));
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
