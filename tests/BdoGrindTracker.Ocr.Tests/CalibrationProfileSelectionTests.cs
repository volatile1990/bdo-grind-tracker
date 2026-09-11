namespace BdoGrindTracker.Ocr.Tests;

public sealed class CalibrationProfileSelectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "grindcrest-profile-selection-" + Guid.NewGuid().ToString("N"));
    private static DateTime Written => new(2026, 9, 11, 16, 22, 5, DateTimeKind.Utc);

    [Fact]
    public void ExistingXmlUpdateWinsOverANewerDirectoryTimestamp()
    {
        var old = Profile("4137537", Written.AddDays(-38), Written.AddDays(-38));
        var active = Profile("993113", Written, Written.AddDays(-64));

        Assert.True(Directory.GetLastWriteTimeUtc(old) > Directory.GetLastWriteTimeUtc(active));
        Assert.Equal(active, CompanionCalibrationReader.SelectActiveProfileDirectory(root));
    }

    [Fact]
    public void NewlyCreatedDirectoryWithoutConfigurationCannotDisplaceActiveProfile()
    {
        var active = Profile("42", Written, Written.AddDays(-1));
        var empty = Directory.CreateDirectory(Path.Combine(root, "43")).FullName;
        Directory.SetLastWriteTimeUtc(empty, Written.AddDays(1));

        Assert.Equal(active, CompanionCalibrationReader.SelectActiveProfileDirectory(root));
    }

    [Fact]
    public void EqualXmlDatesUseStablePathOrderInsteadOfDirectoryCreationOrder()
    {
        var expected = Profile("42", Written, Written.AddDays(-10));
        Profile("41", Written, Written.AddDays(1));

        Assert.Equal(expected, CompanionCalibrationReader.SelectActiveProfileDirectory(root));
    }

    [Fact]
    public void NewestMalformedConfigIsNotSilentlyReplacedWithAnOldUsersLayout()
    {
        Profile("41", Written.AddDays(-1), Written);
        var latest = Profile("42", Written, Written.AddDays(-1));
        File.WriteAllText(Path.Combine(latest, "gameVariable.xml"), "<incomplete");
        File.SetLastWriteTimeUtc(Path.Combine(latest, "gameVariable.xml"), Written);

        Assert.Equal(latest, CompanionCalibrationReader.SelectActiveProfileDirectory(root));
    }

    private string Profile(string id, DateTime fileTime, DateTime directoryTime)
    {
        var path = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
        var file = Path.Combine(path, "gameVariable.xml");
        File.WriteAllText(file, "<UIData/>");
        File.SetLastWriteTimeUtc(file, fileTime);
        Directory.SetLastWriteTimeUtc(path, directoryTime);
        return path;
    }

    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file);
        foreach (var directory in Directory.GetDirectories(root)) Directory.Delete(directory);
        Directory.Delete(root);
    }
}
