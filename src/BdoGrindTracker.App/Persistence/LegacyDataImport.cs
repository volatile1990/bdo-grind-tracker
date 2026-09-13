using System.Security.Cryptography;
using System.Text.Json;
using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Persistence;

/// <summary>Imports persistent state without replacing existing Store data or losing upload guards.</summary>
internal static class LegacyDataImport
{
    internal const string PreviousCompletionFile = ".legacy-import-v1.complete";
    internal const string CompletionFile = ".legacy-import-v2.complete";
    internal const string PreservedCurrentSessionFile = "legacy-current-session-v1.json";
    private const string HistoryFile = "loot-history-v1.json";
    private static readonly string[] OriginalFiles =
        ["settings.json", HistoryFile, "window-placement.json", "garmoth-api-key.dpapi"];
    private static readonly string[] AdditionalFiles =
        [GarmothUploadJournalStore.FileName, CurrentSessionStore.FileName, "overlay.json", OverlayTemplateStore.FileName];

    public static void Import(string sourceDirectory, string destinationDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Die Datenordner für die Übernahme müssen getrennt sein.");
        var opened = new Dictionary<string, FileStream>(StringComparer.Ordinal);
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            RejectLinkedDirectories(destination);
            if (IsComplete(Path.Combine(destination, CompletionFile), "2\n")) return;
            var previousImport = IsComplete(Path.Combine(destination, PreviousCompletionFile), "1\n");
            RejectLinkedDirectories(source);
            Directory.CreateDirectory(destination);
            var names = previousImport ? AdditionalFiles : OriginalFiles.Concat(AdditionalFiles).ToArray();
            // Hold the source journal and session/history open together. This
            // prevents an old installation from committing another upload while
            // the related local snapshot is copied.
            foreach (var name in new[] { GarmothUploadJournalStore.FileName, HistoryFile }.Concat(names).Distinct())
            {
                var targetAttributes = ReadAttributes(Path.Combine(destination, name));
                if (targetAttributes is { } target && (target & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("Ein Zieldateipfad ist kein regulärer Datenpfad.");
                if (targetAttributes is not null && name is not (GarmothUploadJournalStore.FileName or HistoryFile)) continue;
                var original = Path.Combine(source, name);
                if (ReadAttributes(original) is not { } attributes) continue;
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("Eine Quelldatei ist kein regulärer Datenpfad.");
                opened.Add(name, new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            foreach (var (name, input) in opened)
            {
                var temporary = Path.Combine(destination, $".legacy-import-{Guid.NewGuid():N}.tmp");
                staged.Add(name, temporary);
                using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            // Upload intent wins the commit order: a later failed file move can
            // leave an extra conservative block, never history without its guard.
            if (staged.TryGetValue(GarmothUploadJournalStore.FileName, out var journal))
                new GarmothUploadJournalStore(Path.Combine(destination, GarmothUploadJournalStore.FileName)).ImportFrom(journal);
            foreach (var name in names.Where(name => name != GarmothUploadJournalStore.FileName))
            {
                if (!staged.TryGetValue(name, out var temporary)) continue;
                var target = Path.Combine(destination, name);
                if (ReadAttributes(target) is not null) continue;
                if (name == CurrentSessionStore.FileName &&
                    (previousImport || ReadAttributes(Path.Combine(destination, HistoryFile)) is not null) &&
                    (ReadAttributes(Path.Combine(destination, HistoryFile)) is null ||
                     !staged.TryGetValue(HistoryFile, out var sourceHistory) ||
                     !FilesMatch(sourceHistory, Path.Combine(destination, HistoryFile))))
                {
                    // A previously used Store installation is authoritative.
                    // Keep the omitted checkpoint available as a separate backup,
                    // without reactivating a possibly superseded old session.
                    target = Path.Combine(destination, PreservedCurrentSessionFile);
                    if (ReadAttributes(target) is not null) continue;
                }
                File.Move(temporary, target, overwrite: false);
            }
            WriteCompletion(Path.Combine(destination, CompletionFile), destination);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new IOException("Vorhandene Grindcrest-Daten konnten nicht vollständig in die Store-Version übernommen werden. " +
                "Die ursprünglichen Daten bleiben unverändert. Bitte beide Grindcrest-Versionen schließen und erneut starten. " +
                "Falls der Fehler bestehen bleibt, den Zugriff auf den bisherigen Datenordner und den Store-Datenordner prüfen.", error);
        }
        finally
        {
            foreach (var file in opened.Values) file.Dispose();
            foreach (var temporary in staged.Values)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsComplete(string path, string expected)
    {
        if (ReadAttributes(path) is not { } attributes) return false;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || File.ReadAllText(path) != expected)
            throw new IOException("Die Übernahmemarkierung ist ungültig oder unvollständig.");
        return true;
    }

    private static bool FilesMatch(string first, string second)
    {
        using var left = File.OpenRead(first);
        using var right = File.OpenRead(second);
        return left.Length == right.Length && SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
    }

    private static void WriteCompletion(string complete, string directory)
    {
        var temporary = Path.Combine(directory, $".legacy-import-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var marker = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                marker.Write("2\n"u8);
                marker.Flush(flushToDisk: true);
            }
            File.Move(temporary, complete, overwrite: false);
        }
        finally { File.Delete(temporary); }
    }

    private static FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void RejectLinkedDirectories(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (ReadAttributes(current.FullName) is not { } attributes) continue;
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Datenordner enthält eine Verknüpfung oder einen ungültigen Ordner.");
        }
    }
}
