using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData("Unknown")]
    [InlineData("Ambiguous")]
    [InlineData("Unavailable")]
    public Task MissingClassKeepsCompletedHoursQueuedUntilDetectionRecovers(string detectionStatus) =>
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
            await Assert.IsAssignableFrom<Task>(ReadClassRefreshField(fixture.Service, "_automaticUploadTask"))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("hashashin-awakening", fixture.Service.State.CharacterClassId);
            // Detection may finish before or after the recovery tick reaches
            // uploads. Two further ticks drain both hours in either ordering.
            for (var tick = 0; tick < 2; tick++)
            {
                await fixture.Service.TickAsync();
                await Assert.IsAssignableFrom<Task>(ReadClassRefreshField(fixture.Service, "_automaticUploadTask"))
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            var requests = fixture.Requests.ToArray();
            Assert.Equal(2, requests.Length);
            AssertPayload(requests[0], 60, 2);
            AssertPayload(requests[1], 60, 5);
            Assert.True(GarmothCatalog.TryGetClass("Hashashin", GarmothSpecialization.Awakening,
                out var classId, out var specialization));
            Assert.All(requests, request =>
            {
                Assert.Equal(classId, request.GetProperty("class_id").GetInt32());
                Assert.Equal(specialization, request.GetProperty("spec").GetInt32());
            });

            await fixture.Service.TickAsync();
            Assert.Equal(2, fixture.Requests.Count);
            Assert.False(fixture.Service.State.AutomaticSuspended);
            fixture.AssertTracking(TimeSpan.FromHours(2), 7);
        });
}
