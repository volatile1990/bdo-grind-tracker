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

    internal string BaseDirectory => Path.GetDirectoryName(_settingsPath)
        ?? throw new InvalidOperationException("Der Konfigurationsordner ist ungültig.");

    public SettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BdoGrindTracker");

        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return CreateCurrentDefaults();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateCurrentDefaults();
            settings.UpgradeDefaults();
            return settings;
        }
        catch (JsonException)
        {
            return CreateCurrentDefaults();
        }
        catch (IOException)
        {
            return CreateCurrentDefaults();
        }
        catch (UnauthorizedAccessException)
        {
            return CreateCurrentDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = BaseDirectory;

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static AppSettings CreateCurrentDefaults()
    {
        var settings = new AppSettings();
        settings.UpgradeDefaults();
        return settings;
    }
}
