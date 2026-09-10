using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

internal sealed class OverlaySettingsStore(string? directory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private readonly string _path = Path.Combine(directory ?? AppDataPaths.Current.BaseDirectory, "overlay.json");

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 512 * 1024) return new();
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var settings = document.RootElement.Deserialize<OverlaySettings>(JsonOptions);
            var version = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().FirstOrDefault(property =>
                    property.Name.Equals(nameof(OverlaySettings.HotkeySettingsVersion), StringComparison.OrdinalIgnoreCase)).Value
                : default;
            var migrate = version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) ||
                number < OverlaySettings.CurrentHotkeySettingsVersion;
            if (migrate) settings = (settings ?? new()) with { HotkeysEnabled = true };
            var normalized = OverlayLayout.Normalize(settings);
            if (migrate)
            {
                // Persist the marker now so a later deliberate disable stays disabled.
                // A read-only settings folder must not discard the recovered layout.
                try { Save(normalized); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            return normalized;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(OverlaySettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(OverlayLayout.Normalize(settings), JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
