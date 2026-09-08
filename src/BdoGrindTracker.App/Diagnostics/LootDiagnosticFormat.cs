using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using BdoGrindTracker.Core;
using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Diagnostics;

internal static class LootDiagnosticFormat
{
    public const int Version = 2;
    public const string EngineVersion = "companion-0.7.4-minimum-quantity-v4";
    public const string RecoveryEngineVersion = "companion-0.7.4-recovery-fix-v3";
    public const string PreviousEngineVersion = "companion-0.7.4-restore-v1";
    public const string ExperimentalEngineVersion = "companion-0.7.4-overcount-fix-v2";
    public const string RecordingFileName = "observations.jsonl";
    public const int MaximumObservationsPerFrame = 32;
    public const int MaximumTextLength = 2048;
    public const int MaximumJsonLineBytes = 512 * 1024;
    public const string MinimumQuantityEstimateReason = "companion-minimum-quantity-estimate";

    public static IReadOnlyDictionary<string, uint> SnapshotMinimumTrashQuantities(
        IReadOnlyDictionary<string, uint>? minimumQuantities)
    {
        if (minimumQuantities is null || minimumQuantities.Count > 6)
            throw new InvalidDataException("Ungültige Mindestmengen-Tabelle in der Diagnose-Aufnahme.");
        var snapshot = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var (name, quantity) in minimumQuantities)
        {
            var spot = new AutomaticLootSpotLock();
            spot.Observe([name]);
            if (spot.Spot is null || quantity == 0 || quantity > int.MaxValue)
                throw new InvalidDataException("Ungültiges Trashloot-Item oder Mindestmenge in der Diagnose-Aufnahme.");
            snapshot.Add(name, quantity);
        }
        return new ReadOnlyDictionary<string, uint>(snapshot);
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16,
    };
}

internal sealed record LootDiagnosticHeader(
    string Kind,
    int FormatVersion,
    string EngineVersion,
    DateTimeOffset StartedAt,
    string? SpotId,
    string ReplayScope)
{
    private IReadOnlyDictionary<string, uint> _minimumTrashQuantities =
        LootDiagnosticFormat.SnapshotMinimumTrashQuantities(new Dictionary<string, uint>());

    // Metadata is embedded so a later installation cannot change rare-item classification.
    // Icon paths are classification strings only; replay never opens them.
    public IReadOnlyList<CompanionRareCatalogEntry> Catalog { get; init; } = [];

    // Historical recordings default to no minimum fallback. The immutable copy
    // keeps replay independent of future catalog updates and caller mutations.
    public IReadOnlyDictionary<string, uint> MinimumTrashQuantities
    {
        get => _minimumTrashQuantities;
        init => _minimumTrashQuantities = LootDiagnosticFormat.SnapshotMinimumTrashQuantities(value);
    }
}

internal sealed record LootDiagnosticEntry(
    string Kind,
    int Sequence,
    DateTimeOffset Timestamp,
    IReadOnlyList<LootObservation> Observations,
    IReadOnlyList<TrackedLootEvent> Events,
    IReadOnlyList<LootTrackingDecision> Decisions,
    IReadOnlyList<LootDiagnosticCrop> Crops)
{
    public bool RareEnabled { get; init; }

    // Missing in older recordings: absence must not be interpreted as SDR.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsHdr { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NormalLootRecoveryDiagnostics? Recovery { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatQuantityRecoveryDiagnostics? ChatRecovery { get; init; }
}

internal sealed record LootDiagnosticCrop(string Source, string FileName, int Width, int Height);
