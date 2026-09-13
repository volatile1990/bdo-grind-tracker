using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

internal sealed class OverlaySettingsStore(string? directory = null)
{
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private readonly string _path = Path.Combine(directory ?? AppDataPaths.Current.BaseDirectory, "overlay.json");
    private bool _loaded;
    public string? LoadError { get; private set; }

    private JsonDocument ReadDocument()
    {
        using var stream = File.OpenRead(_path);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Die Overlay-Datei ist zu groß.");
        var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException("Das Format der Overlay-Datei wird nicht unterstützt.");
        }
        return document;
    }

    private void RecordLoadFailure(Exception exception) => LoadError =
        "Overlays konnten nicht geladen werden. Die gespeicherte Datei bleibt unverändert. " + exception.Message;

    private void EnsureWritable()
    {
        if (!_loaded) LoadCollection();
        if (LoadError is not null) throw new IOException(LoadError);
    }

    public OverlaySettings Load()
    {
        _loaded = true;
        try
        {
            using var document = ReadDocument();
            LoadError = null;
            if (Property(document.RootElement, nameof(OverlayCollectionSettings.Overlays)).ValueKind != JsonValueKind.Undefined)
            {
                var collection = ReadCollection(document.RootElement);
                return collection.Overlays.First(overlay => overlay.Id == collection.SelectedOverlayId).Settings;
            }
            var (normalized, migrate) = ReadLegacy(document.RootElement);
            if (migrate)
            {
                // Persist the marker now so a later deliberate disable stays disabled.
                // A read-only settings folder must not discard the recovered layout.
                try { Save(normalized); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            return normalized;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LoadError = null;
            return new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            RecordLoadFailure(exception);
            return new();
        }
    }

    public void Save(OverlaySettings settings)
    {
        EnsureWritable();
        // Compatibility callers still edit the selected layout without discarding other windows.
        if (File.Exists(_path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_path));
                if (Property(document.RootElement, nameof(OverlayCollectionSettings.Overlays)).ValueKind != JsonValueKind.Undefined)
                {
                    var collection = ReadCollection(document.RootElement);
                    SaveCollection(collection with { Hotkeys = OverlayHotkeySettings.FromLegacy(settings),
                        Overlays = collection.Overlays.Select(overlay =>
                        overlay.Id == collection.SelectedOverlayId ? overlay with { Settings = settings } : overlay).ToArray() });
                    return;
                }
            }
            catch (JsonException exception)
            {
                RecordLoadFailure(exception);
                throw new IOException(LoadError, exception);
            }
        }
        Write(OverlayLayout.Normalize(settings));
    }

    public OverlayCollectionSettings LoadCollection()
    {
        _loaded = true;
        try
        {
            using var document = ReadDocument();
            LoadError = null;
            if (Property(document.RootElement, nameof(OverlayCollectionSettings.Overlays)).ValueKind != JsonValueKind.Undefined)
            {
                var collection = ReadCollection(document.RootElement);
                if (Property(document.RootElement, nameof(OverlayCollectionSettings.Hotkeys)).ValueKind != JsonValueKind.Object)
                {
                    // Retain the recovered settings even when migration cannot be written yet.
                    try { SaveCollection(collection); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
                return collection;
            }

            var (legacy, _) = ReadLegacy(document.RootElement);
            var migrated = new OverlayCollectionSettings
            {
                Hotkeys = OverlayHotkeySettings.FromLegacy(legacy),
                Overlays = Array.AsReadOnly(new[] { new OverlayInstance { Settings = legacy } }),
            };
            // A read-only settings folder must not discard the recovered layout.
            try { SaveCollection(migrated); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            return migrated;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LoadError = null;
            return new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            RecordLoadFailure(exception);
            return new();
        }
    }

    public void SaveCollection(OverlayCollectionSettings collection)
    {
        EnsureWritable();
        var document = JsonSerializer.SerializeToNode(NormalizeCollection(collection), JsonOptions)!;
        // Legacy layout fields remain readable, but the shared pair is persisted only once.
        foreach (var overlay in document[nameof(OverlayCollectionSettings.Overlays)]!.AsArray())
        {
            var settings = overlay![nameof(OverlayInstance.Settings)]!.AsObject();
            settings.Remove(nameof(OverlaySettings.HotkeysEnabled));
            settings.Remove(nameof(OverlaySettings.HotkeySettingsVersion));
            settings.Remove(nameof(OverlaySettings.ToggleOverlayHotkey));
            settings.Remove(nameof(OverlaySettings.ToggleInteractionHotkey));
        }
        Write(document);
    }

    private static OverlayCollectionSettings ReadCollection(JsonElement root)
    {
        var version = Property(root, nameof(OverlayCollectionSettings.Version));
        if (version.ValueKind != JsonValueKind.Undefined &&
            (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var value) || value is < 1 or > 2))
            throw new InvalidDataException("Diese Version der Overlay-Datei wird nicht unterstützt.");
        if (Property(root, nameof(OverlayCollectionSettings.Overlays)).ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Die Overlay-Liste fehlt oder ist beschädigt.");
        return NormalizeCollection(root.Deserialize<OverlayCollectionSettings>(JsonOptions) ?? new());
    }

    private static (OverlaySettings Settings, bool Migrate) ReadLegacy(JsonElement root)
    {
        var settings = root.Deserialize<OverlaySettings>(JsonOptions);
        var version = Property(root, nameof(OverlaySettings.HotkeySettingsVersion));
        var migrate = version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) ||
            number < OverlaySettings.CurrentHotkeySettingsVersion;
        if (migrate) settings = (settings ?? new()) with { HotkeysEnabled = true };
        return (OverlayLayout.Normalize(settings), migrate);
    }

    internal static OverlayCollectionSettings NormalizeCollection(OverlayCollectionSettings collection)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var overlays = new List<OverlayInstance>();
        foreach (var overlay in collection.Overlays ?? [])
        {
            if (overlay is null) continue;
            var id = overlay.Id;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(char.IsControl) || !ids.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                ids.Add(id);
            }
            string name;
            try { name = OverlayInstance.NormalizeName(overlay.Name); }
            catch (ArgumentException) { name = $"Overlay {overlays.Count + 1}"; }
            overlays.Add(overlay with { Id = id, Name = name, Settings = OverlayLayout.Normalize(overlay.Settings) });
        }
        if (overlays.Count == 0) overlays.Add(new());
        var hotkeys = collection.Hotkeys is null
            ? OverlayHotkeySettings.FromLegacy((overlays.FirstOrDefault(overlay => overlay.Settings.HotkeysEnabled) ?? overlays[0]).Settings)
            : OverlayHotkeySettings.Normalize(collection.Hotkeys);
        return collection with
        {
            Version = 2,
            Hotkeys = hotkeys,
            Overlays = Array.AsReadOnly(overlays.Select(overlay => overlay with { Settings = hotkeys.ApplyTo(overlay.Settings) }).ToArray()),
            SelectedOverlayId = overlays.Any(overlay => overlay.Id == collection.SelectedOverlayId)
                ? collection.SelectedOverlayId : overlays[0].Id,
        };
    }

    private static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        ? element.EnumerateObject().FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value
        : default;

    private void Write<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (json.Length > MaximumFileBytes)
            throw new IOException("Die Overlay-Konfiguration ist zu groß. Bitte nicht mehr benötigte Overlays entfernen.");
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
