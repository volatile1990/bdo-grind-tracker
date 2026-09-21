using System.Text.Json;
using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BuffProfileStoreTests
{
    [Theory]
    [InlineData("harmony-draught", "immortal-harmony-draught")]
    [InlineData("tent-body-enhancement-60", "tent-body-enhancement-120")]
    public void AmbiguousPriceOrDurationVariantsCannotShareAProfile(string first, string second)
    {
        var profile = Profile([
            new(first, "icon.png", new(0, 32, 40, 16)),
            new(second, "icon.png", new(0, 32, 40, 16)),
        ]);
        Assert.False(profile.IsValid);
        Assert.Contains("Buff-Familie", profile.ValidationError);
    }

    [Fact]
    public void PartyBuffsRequireExplicitAttributionAndIndependentGroupsRemainUsable()
    {
        var party = new BuffIconTemplate("harmony-draught-demihuman", "icon.png", new(0, 32, 40, 16));
        var profile = Profile([party, new("simple-cron-meal", "meal.png", new(0, 32, 40, 16))]);
        Assert.False(profile.IsValid);
        Assert.Contains("eigenen Verbrauch", profile.ValidationError);
        Assert.True((profile with
        { Templates = [party with { ConsumptionAttributionConfirmed = true }, profile.Templates[1]] }).IsValid);
    }

    private static BuffRecognitionProfile Profile(IReadOnlyList<BuffIconTemplate> templates) => new()
    { ScreenWidth = 1920, ScreenHeight = 1080, Region = new(20, 30, 300, 64), Templates = templates };

    [Theory]
    [InlineData("unknown-buff")]
    [InlineData("invalid-dimensions")]
    [InlineData("missing-template")]
    public void UnusableProfileReturnsAnActionableErrorInsteadOfThrowing(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "buff-profile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var profile = new BuffRecognitionProfile
            {
                ScreenWidth = scenario == "invalid-dimensions" ? 0 : 1920,
                ScreenHeight = 1080, Region = new(20, 30, 300, 64),
                Templates = [new(scenario == "unknown-buff" ? "unknown-buff" : "harmony-draught", "icon.png", new(0, 32, 40, 16))],
            };
            var path = Path.Combine(directory, "profile.json");
            File.WriteAllText(path, JsonSerializer.Serialize(profile));
            var store = new BuffRecognitionProfileStore(path);
            Assert.Null(store.Load());
            Assert.False(string.IsNullOrWhiteSpace(store.LastError));
        }
        finally { Directory.Delete(directory, true); }
    }
}
