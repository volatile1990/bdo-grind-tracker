namespace BdoGrindTracker.App.Persistence;

/// <summary>Copies legacy user state once; original files and existing Store data always win.</summary>
internal static class LegacyDataImport
{
    internal const string CompletionFile = ".legacy-import-v1.complete";
    private static readonly string[] PersistentFiles =
        ["settings.json", "loot-history-v1.json", "window-placement.json", "garmoth-api-key.dpapi"];

    public static void Import(string sourceDirectory, string destinationDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Die Datenordner für die Übernahme müssen getrennt sein.");
        try
        {
            RejectLinkedDirectories(destination);
            var complete = Path.Combine(destination, CompletionFile);
            if (ReadAttributes(complete) is { } completionAttributes)
            {
                if ((completionAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("Die Übernahmemarkierung ist ungültig.");
                if (File.ReadAllText(complete) != "1\n")
                    throw new IOException("Die Übernahmemarkierung ist unvollständig.");
                return;
            }

            RejectLinkedDirectories(source);
            Directory.CreateDirectory(destination);
            foreach (var name in PersistentFiles)
            {
                var target = Path.Combine(destination, name);
                if (ReadAttributes(target) is { } targetAttributes)
                {
                    if ((targetAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                        throw new IOException("Ein Zieldateipfad ist kein regulärer Datenpfad.");
                    continue;
                }
                var original = Path.Combine(source, name);
                if (ReadAttributes(original) is not { } attributes) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                    throw new IOException("Eine Quelldatei ist ein Ordner.");
                CopyAtomically(original, target, destination);
            }

            // A failed copy never writes this marker. The next startup retries
            // missing files while retaining every successfully imported file.
            WriteCompletion(complete, destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Vorhandene Grindcrest-Daten konnten nicht vollständig in die Store-Version übernommen werden. " +
                "Die ursprünglichen Daten bleiben unverändert. Bitte beide Grindcrest-Versionen schließen und erneut starten. " +
                "Falls der Fehler bestehen bleibt, den Zugriff auf den bisherigen Datenordner und den Store-Datenordner prüfen.", error);
        }
    }

    private static void WriteCompletion(string complete, string directory)
    {
        var temporary = Path.Combine(directory, $".legacy-import-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var marker = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                marker.Write("1\n"u8);
                marker.Flush(flushToDisk: true);
            }
            File.Move(temporary, complete, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CopyAtomically(string source, string target, string directory)
    {
        var temporary = Path.Combine(directory, $".legacy-import-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
