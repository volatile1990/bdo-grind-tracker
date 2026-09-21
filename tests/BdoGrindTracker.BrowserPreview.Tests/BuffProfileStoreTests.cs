using System.Text.Json;
using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BuffProfileStoreTests
{
    [Theory]
    [InlineData("perfume-of-courage", "immortal-perfume-of-courage")]
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
    public void NormalAndImmortalPartyBuffsRemainDistinctWithoutOwnConsumptionConfirmation()
    {
        var party = new BuffIconTemplate("harmony-draught-demihuman", "icon.png", new(0, 32, 40, 16));
        var profile = Profile([party,
            new("immortal-harmony-draught-demihuman", "immortal.png", new(0, 32, 40, 16)),
            new("simple-cron-meal", "meal.png", new(0, 32, 40, 16))]);
        Assert.True(profile.IsValid);
        Assert.All(profile.Templates, template => Assert.False(template.ConsumptionAttributionConfirmed));
    }

    private static BuffRecognitionProfile Profile(IReadOnlyList<BuffIconTemplate> templates) => new()
    { ScreenWidth = 1920, ScreenHeight = 1080, Region = new(20, 30, 300, 64), Templates = templates };

    [Theory]
    [InlineData("immortal-harmony-draught")]
    [InlineData("immortal-harmony-draught-human")]
    [InlineData("immortal-harmony-draught-demihuman")]
    [InlineData("immortal-harmony-draught-kamasylvia")]
    [InlineData("immortal-harmony-draught-edania")]
    public void ImmortalHarmonyProfilesRetainTheirVariantWithoutRewritingTheFile(string id)
    {
        var directory = Path.Combine(Path.GetTempPath(), "buff-profile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var profile = Profile([new(id, "icon.png", new(0, 32, 40, 16))]);
            Assert.True(profile.IsValid);
            var path = Path.Combine(directory, "profile.json");
            var json = JsonSerializer.Serialize(profile);
            File.WriteAllText(path, json);
            File.WriteAllBytes(Path.Combine(directory, "icon.png"), [1]);

            var loaded = Assert.IsType<BuffRecognitionProfile>(new BuffRecognitionProfileStore(path).Load());

            Assert.Equal(id, Assert.Single(loaded.Templates).BuffId);
            Assert.True(loaded.IsValid);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

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
