using System.Text.Json;
using System.Text.Json.Serialization;
using BdoGrindTracker.Core;
using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Diagnostics;

internal static class LootDiagnosticFormat
{
    public const int Version = 2;
    public const string EngineVersion = "companion-0.7.4-overcount-fix-v2";
    public const string PreviousEngineVersion = "companion-0.7.4-restore-v1";
    public const string RecordingFileName = "observations.jsonl";
    public const int MaximumObservationsPerFrame = 32;
    public const int MaximumTextLength = 2048;
    public const int MaximumJsonLineBytes = 512 * 1024;
    public const long MaximumReplayJsonBytes = 64L * 1024 * 1024;
    public const int MaximumActions = DiagnosticRecordingSession.DefaultMaximumFrames * 3;

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
    // Metadata is embedded so a later installation cannot change rare-item classification.
    // Icon paths are classification strings only; replay never opens them.
    public IReadOnlyList<CompanionRareCatalogEntry> Catalog { get; init; } = [];
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NormalLootRecoveryDiagnostics? Recovery { get; init; }
}

internal sealed record LootDiagnosticCrop(string Source, string FileName, int Width, int Height);
