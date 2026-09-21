using System.Text.Json;
using System.Text.Json.Serialization;

namespace BdoGrindTracker.App.UI;

public enum CombatStatsCategory
{
    General,
    Edania,
    Demihuman,
    Kamasylvian,
}

/// <summary>AP, DP and the selected damage category read together from the game's HUD.</summary>
[JsonConverter(typeof(CombatStatsStateJsonConverter))]
public sealed record CombatStatsState(
    int? Ap = null,
    int? Dp = null,
    CombatStatsCategory? Category = null,
    DateTimeOffset? ObservedAt = null)
{
    public const int MaximumStatValue = 10_000;
    public static CombatStatsState Unknown { get; } = new();
    public bool IsKnown => Ap is > 0 and <= MaximumStatValue && Dp is > 0 and <= MaximumStatValue &&
        Category is { } category && Enum.IsDefined(category) && ObservedAt > DateTimeOffset.UnixEpoch;

    internal static CombatStatsState? ConfirmedOrNull(CombatStatsState? observation) =>
        observation is { IsKnown: true } ? observation : null;
}

/// <summary>Optional HUD metadata must not make otherwise valid loot history unreadable.</summary>
internal sealed class CombatStatsStateJsonConverter : JsonConverter<CombatStatsState>
{
    public override CombatStatsState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object) return CombatStatsState.Unknown;
        int? ap = null, dp = null;
        CombatStatsCategory? category = null;
        DateTimeOffset? observedAt = null;
        string Name(string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        foreach (var property in value.EnumerateObject())
        {
            var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (property.Name.Equals(Name(nameof(CombatStatsState.Ap)), comparison))
                ap = ReadInteger(property.Value);
            else if (property.Name.Equals(Name(nameof(CombatStatsState.Dp)), comparison))
                dp = ReadInteger(property.Value);
            else if (property.Name.Equals(Name(nameof(CombatStatsState.Category)), comparison))
            {
                category = ReadInteger(property.Value) is { } number ? (CombatStatsCategory)number : null;
                if (property.Value.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<CombatStatsCategory>(property.Value.GetString(), ignoreCase: true, out var named))
                    category = named;
            }
            else if (property.Name.Equals(Name(nameof(CombatStatsState.ObservedAt)), comparison))
                observedAt = property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetDateTimeOffset(out var date)
                    ? date : null;
        }
        var observation = new CombatStatsState(ap, dp, category, observedAt);
        return observation.IsKnown ? observation : CombatStatsState.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, CombatStatsState value, JsonSerializerOptions options)
    {
        if (!value.IsKnown) { writer.WriteNullValue(); return; }
        string Name(string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        writer.WriteStartObject();
        writer.WriteNumber(Name(nameof(CombatStatsState.Ap)), value.Ap!.Value);
        writer.WriteNumber(Name(nameof(CombatStatsState.Dp)), value.Dp!.Value);
        writer.WriteNumber(Name(nameof(CombatStatsState.Category)), (int)value.Category!.Value);
        writer.WriteString(Name(nameof(CombatStatsState.ObservedAt)), value.ObservedAt!.Value);
        writer.WriteEndObject();
    }

    private static int? ReadInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
}
