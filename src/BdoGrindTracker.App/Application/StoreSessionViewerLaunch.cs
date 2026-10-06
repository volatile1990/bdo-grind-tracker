using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.Services;

/// <summary>Resolves an unpackaged Store-session source; the caller chooses viewing or exclusive takeover.</summary>
internal static class StoreSessionViewerLaunch
{
    internal const string ConfigurationFileName = "store-session-viewer.json";
    internal const string ViewerArgument = "--view-store-session";
    internal const string TakeoverArgument = "--take-over-store-session";
    private const int MaximumConfigurationBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static string? Resolve(string baseDirectory, AppPackageIdentity identity, string[] args) =>
        Resolve(baseDirectory, identity, args, Environment.GetEnvironmentVariable(AppDataPaths.DataDirectoryVariable));

    internal static string? Resolve(string baseDirectory, AppPackageIdentity identity, string[] args,
        string? environmentDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(args);
        var explicitViewer = args.Contains(ViewerArgument, StringComparer.OrdinalIgnoreCase);
        var explicitTakeover = args.Contains(TakeoverArgument, StringComparer.OrdinalIgnoreCase);
        if (explicitViewer && explicitTakeover)
            throw new IOException("Store-Session ansehen und Store-Session übernehmen können nicht gleichzeitig gewählt werden.");
        var explicitSource = explicitViewer || explicitTakeover;
        if (identity != AppPackageIdentity.Unpackaged)
        {
            // The Store app must not probe or obey a marker beside its installed files.
            if (explicitSource)
                throw new IOException("Die Store-Session kann nur mit einer lokalen Grindcrest-Ausgabe angesehen oder übernommen werden.");
            return null;
        }

        if (explicitSource && !string.IsNullOrWhiteSpace(environmentDataDirectory))
            return ValidateSourceDirectory(environmentDataDirectory);

        var configurationPath = Path.Combine(baseDirectory, ConfigurationFileName);
        Configuration? configuration;
        try
        {
            using var file = new FileStream(configurationPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumConfigurationBytes)
                throw new InvalidDataException("Die Viewer-Konfiguration ist zu groß.");
            configuration = JsonSerializer.Deserialize<Configuration>(file, JsonOptions)
                ?? throw new InvalidDataException("Die Viewer-Konfiguration enthält keinen Datenordner.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            if (!explicitSource) return null;
            throw new IOException("Für die Store-Session fehlt der Datenordner. " +
                $"Bitte {AppDataPaths.DataDirectoryVariable} oder {ConfigurationFileName} angeben.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            throw new IOException($"Die Viewer-Konfiguration {ConfigurationFileName} konnte nicht gelesen werden. " +
                "Bitte die Datei prüfen.", error);
        }

        return ValidateSourceDirectory(configuration.DataDirectory);
    }

    private static string ValidateSourceDirectory(string? sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Path.IsPathFullyQualified(sourceDirectory) ||
            !Directory.Exists(sourceDirectory))
            throw new IOException("Der Datenordner für die Store-Session muss ein vorhandener absoluter Ordner sein.");
        // A missing checkpoint is a normal state before the first saved session.
        // The viewer reports it without turning this launch into a normal tracker.
        return Path.GetFullPath(sourceDirectory);
    }

    private sealed record Configuration
    {
        public string? DataDirectory { get; init; }
    }
}
