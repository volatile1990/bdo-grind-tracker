using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData("Unknown")]
    [InlineData("Ambiguous")]
    [InlineData("Unavailable")]
    public Task RecoveredClassIsUsedWhenTheSessionIsCompleted(string detectionStatus) =>
        RunOnHostContextAsync(async () =>
        {
            await using var fixture = new Fixture();
            fixture.Begin();
            SetField(fixture.Service, "_sessionClass", null!);
            fixture.ClassDetection = new(null, Enum.Parse<CharacterClassDetectionStatus>(detectionStatus));
            SetField(fixture.Service, "_classDetection", fixture.ClassDetection);

            await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
            await fixture.Service.TickAsync();
            await AwaitClassRefresh(fixture.Service);
            await fixture.Service.TickAsync();

            Assert.Empty(fixture.Requests);
            Assert.False(fixture.Service.State.AutomaticSuspended);
            Assert.Null(fixture.Service.State.CharacterClassId);

            await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 5));
            await fixture.Service.TickAsync();

            Assert.Empty(fixture.Requests);
            Assert.False(fixture.Service.State.AutomaticSuspended);

            fixture.ClassDetection = DetectedClass("hashashin-awakening");
            SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MinValue);
            await fixture.Service.TickAsync();
            await AwaitClassRefresh(fixture.Service);
            Assert.Equal("hashashin-awakening", fixture.Service.State.CharacterClassId);
            await fixture.Service.TickAsync();
            Assert.Empty(fixture.Requests);
            fixture.AssertTracking(TimeSpan.FromHours(2), 7);
            await fixture.Service.PauseAsync();
            Assert.Empty(fixture.Requests);
            Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
            var requests = fixture.Requests.ToArray();
            AssertPayload(Assert.Single(requests), 120, 7);
            Assert.True(GarmothCatalog.TryGetClass("Hashashin", GarmothSpecialization.Awakening,
                out var classId, out var specialization));
            Assert.All(requests, request =>
            {
                Assert.Equal(classId, request.GetProperty("class_id").GetInt32());
                Assert.Equal(specialization, request.GetProperty("spec").GetInt32());
            });

            await fixture.Service.TickAsync();
            Assert.Single(fixture.Requests);
            Assert.False(fixture.Service.State.AutomaticSuspended);
            Assert.False(fixture.Service.State.HasSession);
        });
}
