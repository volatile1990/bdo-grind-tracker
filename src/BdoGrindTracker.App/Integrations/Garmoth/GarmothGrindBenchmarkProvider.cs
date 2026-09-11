using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>Public, anonymous reference reads. Successful spot rows are cached independently;
/// a failed request never changes the values or timestamps of the last usable reference.</summary>
internal sealed class GarmothGrindBenchmarkProvider : IGarmothGrindBenchmarkProvider
{
    internal const string CacheFileName = "garmoth-benchmarks-v1.json";
    internal const string CollectiveUrl = "https://api.garmoth.com/api/grind-tracker/collective/all";
    internal const string MetadataUrl = "https://garmoth.com/api/trpc/grindMeta.list";
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string? _cachePath;
    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, GrindBenchmark> _benchmarks = new(StringComparer.Ordinal);
    private bool _loaded;

    public GarmothGrindBenchmarkProvider(string cachePath) : this(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false,
    }, cachePath) { }

    internal GarmothGrindBenchmarkProvider(HttpMessageHandler handler, string? cachePath = null,
        TimeProvider? timeProvider = null, TimeSpan? requestTimeout = null)
    {
        _http = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
        _cachePath = cachePath;
        _time = timeProvider ?? TimeProvider.System;
        _timeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public GarmothBenchmarkSnapshot GetCachedSnapshot()
    {
        lock (_sync)
        {
            LoadCache();
            return Snapshot(_benchmarks.Count == 0 ? "Mitgelieferter Garmoth-Referenzstand."
                : "Gespeicherte Garmoth-Referenzwerte geladen.");
        }
    }

    public async Task<GarmothBenchmarkSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);
            IReadOnlyList<GrindBenchmark> received;
            try
            {
                // Both sources must succeed: combining today's average with unverified old
                // moderator thresholds would incorrectly mark the entire row as current.
                var requests = new[] { FetchAsync(CollectiveUrl, deadline.Token), FetchAsync(MetadataUrl, deadline.Token) };
                var payloads = await Task.WhenAll(requests).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                received = Parse(payloads[0], payloads[1], _time.GetUtcNow());
                if (received.Count == 0) return Failed("Garmoth liefert keine gültigen Vergleichswerte.");
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or
                OperationCanceledException or InvalidDataException or OverflowException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Failed(exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden }
                    ? "Garmoth blockiert den Datenabruf (HTTP 403)."
                    : exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
                        ? "Garmoth begrenzt derzeit die Datenabrufe (HTTP 429)."
                        : "Garmoth-Referenzwerte konnten nicht aktualisiert werden.");
            }

            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCache();
                foreach (var benchmark in received) _benchmarks[benchmark.SpotId] = benchmark;
                var saved = SaveCache();
                var message = received.Count == GarmothGrindBenchmarks.All.Count
                    ? "Garmoth-Referenzwerte aktualisiert."
                    : $"Garmoth-Referenzwerte für {received.Count} von {GarmothGrindBenchmarks.All.Count} Spots aktualisiert; übrige Werte behalten ihren bisherigen Stand.";
                if (!saved) message += " Die aktualisierten Werte konnten nicht lokal gespeichert werden.";
                return Snapshot(message);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<byte[]> FetchAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd(AppBranding.UserAgent);
        if (url == MetadataUrl)
        {
            request.Content = new ByteArrayContent([]);
            request.Content.Headers.ContentType = new("application/json");
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes ||
            response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            throw new InvalidDataException("Invalid Garmoth response.");
        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, token).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
    }

    private GarmothBenchmarkSnapshot Failed(string message)
    {
        lock (_sync)
        {
            LoadCache();
            return Snapshot(message + " Der letzte verfügbare Referenzstand wird weiterverwendet.");
        }
    }

    private GarmothBenchmarkSnapshot Snapshot(string message) => new(Array.AsReadOnly(
        GarmothGrindBenchmarks.All.Select(fallback => _benchmarks.GetValueOrDefault(fallback.SpotId, fallback)).ToArray()), message);

    internal static IReadOnlyList<GrindBenchmark> Parse(ReadOnlyMemory<byte> collective, ReadOnlyMemory<byte> metadata,
        DateTimeOffset retrievedAt)
    {
        using var statsDocument = JsonDocument.Parse(collective, new JsonDocumentOptions { MaxDepth = 32 });
        using var metaDocument = JsonDocument.Parse(metadata, new JsonDocumentOptions { MaxDepth = 32 });
        var stats = statsDocument.RootElement;
        var meta = metaDocument.RootElement;
        // tRPC's unbatched response wraps the procedure's ID-keyed object.
        if (meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty("result", out meta) ||
            meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty("data", out meta))
            throw new InvalidDataException("Invalid Garmoth metadata.");
        if (meta.ValueKind != JsonValueKind.Object || stats.ValueKind != JsonValueKind.Object ||
            !stats.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 1000)
            throw new InvalidDataException("Invalid Garmoth statistics.");

        var result = new Dictionary<string, GrindBenchmark>(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("grindspot_id", out var idElement) ||
                !Numeric(idElement, out var numericId) || numericId < 1 || numericId > int.MaxValue ||
                numericId != decimal.Truncate(numericId)) continue;
            var id = (int)numericId;
            var known = GarmothGrindBenchmarks.All.FirstOrDefault(reference =>
                GarmothCatalog.TryGetSpot(reference.SpotId, out var knownId) && knownId == id);
            if (known is null) continue;
            if (!seen.Add(id)) throw new InvalidDataException("Duplicate Garmoth spot.");
            if (!meta.TryGetProperty(id.ToString(CultureInfo.InvariantCulture), out var spotMeta) ||
                spotMeta.ValueKind != JsonValueKind.Object || !Number(row, "total_minutes", out var minutes) || minutes <= 0 ||
                !row.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Array) continue;

            var trash = drops.EnumerateArray().Where(drop => drop.ValueKind == JsonValueKind.Object &&
                drop.TryGetProperty("is_trash", out var flag) && (flag.ValueKind == JsonValueKind.True ||
                    Numeric(flag, out var numericFlag) && numericFlag == 1)).ToArray();
            if (trash.Length != 1 || !Number(trash[0], "hourly_rate", out var rawAverage) || rawAverage <= 0) continue;
            var multiplier = 2m;
            if (spotMeta.TryGetProperty("dropRatios", out var ratios) && ratios.ValueKind == JsonValueKind.Object &&
                ratios.TryGetProperty("l2", out var l2) && l2.ValueKind == JsonValueKind.Number &&
                l2.TryGetDecimal(out var ratio) && ratio >= 1) multiplier = ratio;
            var average = RoundAverage(rawAverage * multiplier);
            if (!OptionalTier(spotMeta, "highTierTrash", average, out var high) ||
                !OptionalTier(spotMeta, "topTierTrash", average, out var top)) continue;
            var start = Date(row, "start_date") ?? Date(stats, "start_date");
            var end = Date(row, "end_date") ?? Date(stats, "end_date");
            if (start is null || end is null || start > end || end > DateOnly.FromDateTime(retrievedAt.UtcDateTime)) continue;
            var reference = new GrindBenchmark(known.SpotId, average, high, top, retrievedAt,
                $"https://garmoth.com/grind-tracker/best-grind-spots/{id}?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}",
                GarmothGrindBenchmarks.Conditions);
            if (Valid(reference, retrievedAt)) result.Add(reference.SpotId, reference);
        }
        return result.Values.ToArray();
    }

    private static bool Number(JsonElement element, string name, out decimal number)
    {
        number = 0;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && Numeric(value, out number);
    }

    private static bool Numeric(JsonElement value, out decimal number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetDecimal(out number) :
            value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 64 } text &&
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static decimal RoundAverage(decimal value) => decimal.Round(value,
        value < 0.1m ? 4 : value < 1m ? 3 : value < 10m ? 2 : value < 100m ? 1 : 0,
        MidpointRounding.AwayFromZero);

    private static bool OptionalTier(JsonElement meta, string name, decimal average, out decimal? tier)
    {
        tier = null;
        if (!meta.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return true;
        if (!Numeric(value, out var number) || number < 0) return false;
        if (number > 0) tier = decimal.Round(Math.Max(average, number), 0, MidpointRounding.AwayFromZero);
        return true;
    }

    private static DateOnly? Date(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: >= 10 and <= 40 } text) return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        // Laravel may serialize dates as ISO timestamps instead of plain dates.
        return text.Length > 10 && text[10] == 'T' && DateTimeOffset.TryParseExact(text,
            ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var timestamp) ? DateOnly.FromDateTime(timestamp.Date) : null;
    }

    private static bool Valid(GrindBenchmark reference, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reference.SpotId) || !GarmothCatalog.TryGetSpot(reference.SpotId, out var id) || reference.UpdatedAt <= DateTimeOffset.UnixEpoch ||
            reference.UpdatedAt > now || reference.Conditions != GarmothGrindBenchmarks.Conditions ||
            !Uri.TryCreate(reference.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https" ||
            source.Host != "garmoth.com" || source.AbsolutePath != $"/grind-tracker/best-grind-spots/{id}") return false;
        return GrindRatingEvaluator.Evaluate(reference.SpotId, 0, TimeSpan.FromHours(1), reference).Tier != GrindRatingTier.Unavailable;
    }

    private void LoadCache()
    {
        if (_loaded) return;
        _loaded = true;
        if (_cachePath is null) return;
        try
        {
            if (!File.Exists(_cachePath) || new FileInfo(_cachePath).Length > 65_536) return;
            var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cachePath));
            if (cache?.SchemaVersion != 1 || cache.Benchmarks is null) return;
            var now = _time.GetUtcNow();
            foreach (var reference in cache.Benchmarks)
                if (reference is not null && Valid(reference, now) &&
                    reference.UpdatedAt >= GarmothGrindBenchmarks.Find(reference.SpotId)!.UpdatedAt)
                    _benchmarks[reference.SpotId] = reference;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private bool SaveCache()
    {
        if (_cachePath is null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            AtomicFile.WriteAllText(_cachePath, JsonSerializer.Serialize(new Cache(1, _benchmarks.Values.ToArray())));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose() => _http.Dispose();
    private sealed record Cache(int SchemaVersion, GrindBenchmark[] Benchmarks);
}
