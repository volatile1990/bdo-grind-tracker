using System.Text.Json;

namespace BdoGrindTracker.App.Persistence;

internal sealed class GrindGoalStore(string? path)
{
    private Dictionary<DateOnly, decimal> _goals = [];
    public string? Error { get; private set; }
    public IReadOnlyDictionary<DateOnly, decimal> Goals => _goals;
    public event Action? Changed;
    public void Load()
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<DateOnly,decimal>>(File.ReadAllText(path)) ?? [];
            if (loaded.Values.Any(v => v <= 0)) throw new InvalidDataException("Ungültiger Zielwert.");
            _goals = loaded; Error = null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { Error = "Grind Goals konnten nicht geladen werden: " + e.Message; }
    }
    public void Set(IEnumerable<DateOnly> dates, decimal? amount)
    {
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
