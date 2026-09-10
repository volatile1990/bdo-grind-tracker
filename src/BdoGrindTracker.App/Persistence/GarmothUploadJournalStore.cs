using System.Text.Json;
using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Persistence;

internal sealed record GarmothUploadJournalEntry(
    Guid AttemptId, GarmothSessionDraft Draft, DateTimeOffset BeganAt,
    GarmothUploadStatus? Outcome = null, DateTimeOffset? CompletedAt = null)
{
    public bool BlocksAfterRestart => Outcome is not GarmothUploadStatus.Rejected;
}

/// <summary>
/// Write-ahead record for possibly committed remote writes. Contains session data,
/// never credentials or remote response bodies. An unfinished intent is a block,
/// not permission to retry. Old attempts are deliberately never pruned here.
/// </summary>
internal sealed class GarmothUploadJournalStore
{
    internal const string FileName = "garmoth-upload-journal-v1.json";
    private const long MaximumFileBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();

    public GarmothUploadJournalStore(string path) => _path = Path.GetFullPath(path);

    public IReadOnlyList<GarmothUploadJournalEntry> Load()
    {
        lock (_gate) return Read();
    }

    public Guid Begin(GarmothSessionDraft draft)
    {
        _ = GarmothSessionPayload.Create(draft);
        if (draft.SourceSessionId is not { } source || source == Guid.Empty)
            throw new ArgumentException("Die Quellsession des Uploads fehlt.");
        lock (_gate)
        {
            var entries = Read().ToList();
            if (entries.Any(entry => entry.Draft.LocalSessionId == draft.LocalSessionId && entry.BlocksAfterRestart))
                throw new InvalidOperationException("Dieser Upload ist bereits vermerkt. Bitte in Garmoth prüfen.");
            var attemptId = Guid.NewGuid();
            var snapshot = draft with { Totals = new Dictionary<string, long>(draft.Totals, StringComparer.OrdinalIgnoreCase) };
            entries.Add(new(attemptId, snapshot, DateTimeOffset.UtcNow));
            Save(entries);
            return attemptId;
        }
    }

    public void Complete(Guid attemptId, GarmothUploadStatus outcome)
    {
        lock (_gate)
        {
            var entries = Read().ToList();
            var index = entries.FindIndex(entry => entry.AttemptId == attemptId);
            if (index < 0) throw new InvalidDataException("Die gespeicherte Uploadabsicht fehlt.");
            if (entries[index].Outcome is { } previous)
            {
                if (previous == outcome) return;
                throw new InvalidOperationException("Ein abgeschlossenes Upload-Ergebnis darf nicht geändert werden.");
            }
            entries[index] = entries[index] with { Outcome = outcome, CompletedAt = DateTimeOffset.UtcNow };
            Save(entries);
        }
    }

    private IReadOnlyList<GarmothUploadJournalEntry> Read()
    {
        // File.Exists masks access errors; only genuine absence means an empty journal.
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Das Uploadjournal ist zu groß.");
            var document = JsonSerializer.Deserialize<JournalDocument>(stream, JsonOptions);
            if (document is null || document.Version != 1 || document.Entries is null)
                throw new InvalidDataException("Das Uploadjournal hat ein unbekanntes Format.");
            var ids = new HashSet<Guid>();
            foreach (var entry in document.Entries)
            {
                if (entry is null || entry.AttemptId == Guid.Empty || !ids.Add(entry.AttemptId) ||
                    entry.Draft is null || entry.Draft.SourceSessionId is not { } source || source == Guid.Empty ||
                    entry.Outcome is { } status && !Enum.IsDefined(status))
                    throw new InvalidDataException("Das Uploadjournal enthält einen ungültigen Versuch.");
                _ = GarmothSessionPayload.Create(entry.Draft);
            }
            return document.Entries;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    private void Save(IReadOnlyList<GarmothUploadJournalEntry> entries)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new JournalDocument(1, entries), JsonOptions);
                if (stream.Length > MaximumFileBytes) throw new IOException("Das Uploadjournal ist zu groß.");
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record JournalDocument(int Version, IReadOnlyList<GarmothUploadJournalEntry> Entries);
}
