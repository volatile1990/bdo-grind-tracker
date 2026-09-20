using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Lossless compression keeps complete per-drop and recognition evidence out of the history
/// file's size budget. Plain arrays remain readable for exports and early platform checkpoints.</summary>
public sealed class RotationTimelineJsonConverter : JsonConverter<IReadOnlyList<RotationTimelineEntry>>
{
    private const int MaximumDecodedBytes = 64 * 1024 * 1024;
    public override IReadOnlyList<RotationTimelineEntry> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
            return JsonSerializer.Deserialize<RotationTimelineEntry[]>(ref reader, options) ?? [];
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("encoding", out var encoding) || encoding.GetString() != "gzip-json-v1" ||
            !root.TryGetProperty("data", out var data)) throw new JsonException("Unbekanntes Zeitstrahlformat.");
        try
        {
            using var compressed = new MemoryStream(data.GetBytesFromBase64());
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = gzip.Read(buffer)) > 0)
            {
                if (decoded.Length + count > MaximumDecodedBytes) throw new JsonException("Zeitstrahl überschreitet das Sessionlimit.");
                decoded.Write(buffer, 0, count);
            }
            return JsonSerializer.Deserialize<RotationTimelineEntry[]>(decoded.ToArray(), options) ?? [];
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or InvalidOperationException)
        { throw new JsonException("Gespeicherter Zeitstrahl ist beschädigt.", e); }
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<RotationTimelineEntry> value, JsonSerializerOptions options)
    {
        if (value.Count == 0) { writer.WriteStartArray(); writer.WriteEndArray(); return; }
        // Do not indent the compressed payload, even when the surrounding history is human-readable.
        var payload = JsonSerializer.SerializeToUtf8Bytes(value.ToArray());
        if (payload.Length > MaximumDecodedBytes) throw new InvalidDataException("Zeitstrahl überschreitet das Sessionlimit.");
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(payload);
        writer.WriteStartObject(); writer.WriteString("encoding", "gzip-json-v1");
        writer.WriteBase64String("data", compressed.ToArray()); writer.WriteEndObject();
    }
}
