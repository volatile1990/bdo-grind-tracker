using System.Text.Json;

namespace BdoGrindTracker.App.Persistence;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private bool _loaded;
    private const int MaximumFileBytes = 4 * 1024 * 1024;

    public string? LoadError { get; private set; }

    internal string BaseDirectory => Path.GetDirectoryName(_settingsPath)
        ?? throw new InvalidOperationException("Der Konfigurationsordner ist ungültig.");

    public SettingsStore(string? baseDirectory = null)
    {
        _settingsPath = Path.Combine(baseDirectory ?? AppDataPaths.Current.BaseDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        _loaded = true;
        try
        {
            // File.Exists hides access failures. Only actual absence permits defaults to be saved.
            using var file = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumFileBytes) throw new InvalidDataException("Die Einstellungsdatei ist zu groß.");
            var settings = JsonSerializer.Deserialize<AppSettings>(file, JsonOptions)
                ?? throw new InvalidDataException("Die Einstellungsdatei enthält keine Einstellungen.");
            settings.UpgradeDefaults();
            LoadError = null;
            return settings;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LoadError = null;
            return CreateCurrentDefaults();
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            LoadError = "Die Einstellungen konnten nicht gelesen werden und werden nicht überschrieben. " +
                "Bitte prüfe die Datei " + _settingsPath + " und versuche das erneute Laden und Sichern. " + exception.Message;
            return CreateCurrentDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!_loaded) Load();
        if (LoadError is not null) throw new IOException(LoadError);

        var directory = BaseDirectory;

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicFile.WriteAllText(_settingsPath, json);
    }

    private static AppSettings CreateCurrentDefaults()
    {
        var settings = new AppSettings();
        settings.UpgradeDefaults();
        return settings;
    }
}
