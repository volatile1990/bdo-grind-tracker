using System.Text.Json;
using System.Text.Json.Serialization;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.UI;

/// <summary>Invalid optional buff metadata must not make a valid loot session unreadable.</summary>
internal sealed class BuffLedgerSnapshotJsonConverter : JsonConverter<BuffLedgerSnapshot>
{
    public override BuffLedgerSnapshot? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            var snapshot = document.RootElement.Deserialize<BuffLedgerSnapshot>(options);
            if (snapshot is null) return null;
            var validator = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions));
            validator.Restore(snapshot);
            return validator.Snapshot;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, BuffLedgerSnapshot value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
