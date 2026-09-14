using System.Text.Json;

namespace BdoGrindTracker.App.Persistence;

internal sealed class GrindGoalStore(string? path)
{
    private Dictionary<DateOnly, decimal> _goals = [];
    private bool _loaded;
    public string? Error { get; private set; }
    public IReadOnlyDictionary<DateOnly, decimal> Goals => _goals;
    public event Action? Changed;
    public void Load()
    {
        _loaded = true;
        if (path is null) return;
        try
        {
            // File.Exists hides access errors; only actual absence permits empty defaults.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var loaded = JsonSerializer.Deserialize<Dictionary<DateOnly,decimal>>(stream)
                ?? throw new InvalidDataException("Die Zieldatei enthält keine Ziele.");
            if (loaded.Values.Any(v => v <= 0)) throw new InvalidDataException("Ungültiger Zielwert.");
            _goals = loaded; Error = null;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { _goals = []; Error = null; }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { Error = "Grind Goals konnten nicht geladen werden: " + e.Message; }
        Changed?.Invoke();
    }
    public void Set(IEnumerable<DateOnly> dates, decimal? amount)
    {
        ArgumentNullException.ThrowIfNull(dates);
        if (!_loaded) Load();
        if (Error is not null) throw new IOException(Error);
        if (amount is <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var next = new Dictionary<DateOnly,decimal>(_goals);
        foreach (var date in dates) { if (amount is { } value) next[date] = value; else next.Remove(date); }
        if (path is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); AtomicFile.WriteAllText(path, JsonSerializer.Serialize(next)); }
        _goals = next;
        Changed?.Invoke();
    }
    public static Dictionary<DateOnly, decimal> DailyNet(IEnumerable<LootHistoryEntry> sessions) => sessions
        .GroupBy(s => s.SessionId).Select(g => g.MaxBy(s => s.UpdatedAt)!)
        .GroupBy(s => DateOnly.FromDateTime(s.StartedAt.LocalDateTime))
        .ToDictionary(g => g.Key, g => g.Sum(s => s.SilverAfterTax));

    public static Dictionary<DateOnly, KeyValuePair<string, long>[]> DailyDrops(
        IEnumerable<LootHistoryEntry> sessions, Func<string, bool> includeItem) => sessions
        .GroupBy(s => s.SessionId).Select(g => g.MaxBy(s => s.UpdatedAt)!)
        .GroupBy(s => DateOnly.FromDateTime(s.StartedAt.LocalDateTime))
        .ToDictionary(g => g.Key, g => g.SelectMany(s => s.Totals)
            .Where(item => item.Value > 0 && includeItem(item.Key))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(items => new KeyValuePair<string, long>(items.Key, items.Sum(item => item.Value)))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
}
