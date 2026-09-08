using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Updates;

internal sealed record UpdatePreferences(bool IsBeta, string? PendingVersion = null, bool? PendingIsBeta = null);

/// <summary>Update choices live beside, and never overwrite, tracker settings and history.</summary>
internal sealed class UpdatePreferencesStore(string path)
{
    public static UpdatePreferencesStore CreateDefault() => new(Path.Combine(
        AppDataPaths.Current.BaseDirectory, "update-settings.json"));

    public UpdatePreferences Load(bool defaultBeta = false)
    {
        try
        {
            return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path)) ?? new(defaultBeta);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(defaultBeta);
        }
    }

    public void Save(UpdatePreferences preferences)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".update-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
