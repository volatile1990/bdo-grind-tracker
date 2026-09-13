using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class UnreadableVisualRecordedSessionTests
{
    [Theory]
    [InlineData(false, false, 2288)]
    [InlineData(true, false, 2284)]
    [InlineData(false, true, 2284)]
    [InlineData(true, true, 2280)]
    public void EachImageVerifiedDropIndependentlyAddsFourWithoutChangingOtherItems(
        bool suppressFirstDropEvidence, bool suppressSecondDropEvidence, int expectedHelmets)
    {
        // Original OCR and timestamps, with independently recomputed glyph masks.
        // The in-game reference is 2288; the historical recording counted 2280.
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime", "recording5-unreadable.raw.txt");
        LifetimeNormalReconciliationAdapter? current = null, historical = null;
        var frameCount = 0;
        foreach (var line in File.ReadLines(path).Where(line => !line.StartsWith('#')))
        {
            using var document = JsonDocument.Parse(line);
            var frame = document.RootElement;
            if (frame.TryGetProperty("lifetimeParsingContext", out var contextElement) && contextElement.ValueKind != JsonValueKind.Null)
            {
                var context = contextElement.Deserialize<LifetimeParsingContext>(LootDiagnosticFormat.JsonOptions)!;
                current ??= new(context, true, useUnreadableSlotCoverage: true);
                historical ??= new(context, true);
                current.UpdateParsingContext(context);
                historical.UpdateParsingContext(context);
            }
            var rows = frame.GetProperty("observations").Deserialize<LootObservation[]>(LootDiagnosticFormat.JsonOptions)!;
            var at = frame.GetProperty("timestamp").GetDateTimeOffset();
            var sequence = frameCount + 1;
            // Remove only the unreadable oldest row's pixel evidence. Its raw
            // OCR, every readable row and all other frames remain identical.
            var suppressEvidence = (sequence == 428 && suppressFirstDropEvidence)
                || (sequence == 2384 && suppressSecondDropEvidence);
            var currentRows = suppressEvidence
                ? rows.Select(row => row.Slot == 4 ? row with { OccupancyEvidence = null } : row).ToArray()
                : rows;
            current!.ProcessObservations(currentRows, at);
            // Restore the recorded v3 input: anonymous pixel probes did not
            // exist there and rejected OCR could not carry occupancy evidence.
            var originalRows = rows.Where(row => row.RejectionReason != "visual-occupancy-only")
                .Select(row => row.ItemName is null ? row with { OccupancyEvidence = null } : row).ToArray();
            historical!.ProcessObservations(originalRows, at);
            frameCount++;
        }
        current!.Complete();
        historical!.Complete();
        Assert.Equal(3115, frameCount);
        Assert.Equal(expectedHelmets, current.Projection!.Totals["Elion Follower's Helmet"]);
        Assert.Equal(2280, historical.Projection!.Totals["Elion Follower's Helmet"]);
        Assert.Equal(historical.Projection.SupportedDropCount + (expectedHelmets - 2280) / 4,
            current.Projection.SupportedDropCount);
        Assert.Equal(historical.Projection.Totals.Where(pair => pair.Key != "Elion Follower's Helmet").OrderBy(pair => pair.Key),
            current.Projection.Totals.Where(pair => pair.Key != "Elion Follower's Helmet").OrderBy(pair => pair.Key));
        Assert.Equal(0, current.Projection.VisualCoverageFallbackCount);
    }
}
