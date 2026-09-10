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
            return OverlayLayout.Normalize(JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(_path), JsonOptions));
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
