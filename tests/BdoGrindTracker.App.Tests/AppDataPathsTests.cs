using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Updates;
using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void PhysicalLocalAppDataResolverDisablesPackageRedirectionWithoutCreatingOrProbingFolders()
    {
        var missing = Path.Combine(Path.GetTempPath(), "Grindcrest.Paths", Guid.NewGuid().ToString("N"));
        var calls = 0;
        int Resolve(ref Guid folderId, uint flags, IntPtr token, out IntPtr path)
        {
            calls++;
            Assert.Equal(new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"), folderId);
            Assert.Equal(0x00014000u, flags);
            Assert.Equal(0u, flags & 0x00008000u); // No KF_FLAG_CREATE.
            Assert.Equal(IntPtr.Zero, token);
            path = Marshal.StringToCoTaskMemUni(missing);
            return 0;
        }

        Assert.Equal(missing, AppDataPaths.ReadPhysicalLocalAppData(Resolve));
        Assert.Equal(1, calls);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void FailedPhysicalPathResolutionDoesNotFallBackToARedirectedLocation()
    {
        static int Resolve(ref Guid folderId, uint flags, IntPtr token, out IntPtr path)
        {
            path = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }

        Assert.Throws<IOException>(() => AppDataPaths.ReadPhysicalLocalAppData(Resolve));
    }

    [Fact]
    public void UnpackagedInstallationKeepsItsExistingPathWithoutQueryingAStoreFamily()
    {
        var local = Path.Combine(Path.GetTempPath(), "Grindcrest.Paths", Guid.NewGuid().ToString("N"));
        var paths = AppDataPaths.Create(AppPackageIdentity.Unpackaged, local,
            () => throw new InvalidOperationException("No package query is needed."));

        Assert.Equal(Path.Combine(local, "BdoGrindTracker"), paths.BaseDirectory);
        Assert.Equal(paths.BaseDirectory, paths.LegacyDirectory);
        Assert.False(paths.IsPackaged);
        Assert.False(Directory.Exists(local));
    }

    [Fact]
    public void PackagedInstallationUsesItsActualFamilyLocalStateWithoutCreatingOrReadingUserFiles()
    {
        var local = Path.Combine(Path.GetTempPath(), "Grindcrest.Paths", Guid.NewGuid().ToString("N"));
        const string family = "Grindcrest.Grindcrest_psjzbyy00rv1m";
        var paths = AppDataPaths.Create(AppPackageIdentity.Packaged, local, () => family);

        Assert.Equal(Path.Combine(local, "Packages", family, "LocalState"), paths.BaseDirectory);
        Assert.Equal(Path.Combine(local, "BdoGrindTracker"), paths.LegacyDirectory);
        Assert.True(paths.IsPackaged);
        Assert.False(Directory.Exists(local));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("Grindcrest.Grindcrest_short")]
    [InlineData("../Grindcrest.Grindcrest_psjzbyy00rv1m")]
    [InlineData("C:\\Grindcrest.Grindcrest_psjzbyy00rv1m")]
    public void InvalidFamilyCannotEscapeThePackageDataDirectory(string family)
    {
        Assert.Throws<IOException>(() => AppDataPaths.Create(AppPackageIdentity.Packaged,
            Path.GetTempPath(), () => family));
    }

    [Fact]
    public void UnknownIdentityNeverFallsBackToSharedLegacyWrites()
    {
        Assert.Throws<IOException>(() => AppDataPaths.Create(AppPackageIdentity.Unknown,
            Path.GetTempPath(), () => throw new InvalidOperationException()));
    }
}
