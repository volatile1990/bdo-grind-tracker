using System.Net;
using System.Net.Http;
using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Pricing;

/// <summary>
/// Arsha market GET with a direct Pearl Abyss fallback. Region-isolated persisted prices
/// remain usable offline but retain their old timestamps and stale markers.
/// No captured images, quantities, class, session, account or API key are sent.
/// </summary>
internal sealed partial class MarketLootPriceProvider : ILootPriceProvider
{
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromMinutes(10);
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(8);
    private const int MaximumResponseBytes = 1_048_576;
    private readonly HttpClient _http;
    private readonly string? _cachePath;
    private readonly TimeProvider _time;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, RegionCache> _regions = new(StringComparer.Ordinal);
    private bool _cacheLoaded;

    public MarketLootPriceProvider() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false,
    }, Path.Combine(AppDataPaths.Current.BaseDirectory, "market-prices-v1.json")) { }

    internal MarketLootPriceProvider(HttpMessageHandler handler, string? cachePath = null,
        TimeProvider? timeProvider = null, TimeSpan? requestTimeout = null)
    {
        _http = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
        _cachePath = cachePath;
        _time = timeProvider ?? TimeProvider.System;
        _requestTimeout = requestTimeout ?? RequestTimeout;
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public LootPriceSnapshot GetCachedSnapshot(string region)
    {
        region = LootPriceCatalog.NormalizeRegion(region);
        lock (_sync)
        {
            LoadCacheOnce();
            return BuildSnapshot(region, GetRegion(region));
        }
    }

    public async Task<LootPriceSnapshot> GetSnapshotAsync(string region,
        CancellationToken cancellationToken = default)
    {
        region = LootPriceCatalog.NormalizeRegion(region);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RegionCache previous;
            var now = _time.GetUtcNow();
            lock (_sync)
            {
                LoadCacheOnce();
                previous = GetRegion(region);
                if (previous.LastRefresh is { } refreshed && refreshed <= now && now - refreshed < RefreshInterval ||
                    previous.Arsha.NextAttempt > now && previous.PearlAbyss.NextAttempt > now)
                    return BuildSnapshot(region, previous);
            }

            var received = new Dictionary<int, MarketPrice>();
            var sources = new List<string>();
            SourcePrices? arsha = null;
            SourcePrices? pearlAbyss = null;
            if (previous.Arsha.NextAttempt <= _time.GetUtcNow())
            {
                arsha = await FetchArshaPricesAsync(region, cancellationToken).ConfigureAwait(false);
                lock (_sync) UpdateSource(previous.Arsha, arsha);
                foreach (var (id, price) in arsha.Prices) received[id] = price;
                if (arsha.Prices.Count > 0) sources.Add("Arsha");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var missing = LootPriceCatalog.MarketItemIds.Where(id => !received.ContainsKey(id)).ToArray();
            if (missing.Length > 0 && previous.PearlAbyss.NextAttempt <= _time.GetUtcNow())
            {
                // The fallback has its own deadline, so an Arsha timeout cannot cancel it.
                pearlAbyss = await FetchPearlAbyssPricesAsync(region, missing, cancellationToken).ConfigureAwait(false);
                lock (_sync) UpdateSource(previous.PearlAbyss, pearlAbyss);
                foreach (var (id, price) in pearlAbyss.Prices) received.TryAdd(id, price);
                if (pearlAbyss.Prices.Count > 0) sources.Add("Pearl Abyss (Fallback)");
            }
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                var current = GetRegion(region);
                if (received.Count == 0)
                {
                    current.Message = string.Join(" ", new[] { current.Arsha.Error, current.PearlAbyss.Error }
                        .Where(static error => !string.IsNullOrEmpty(error)).Distinct()) +
                        " Vorhandene Cache-/Festwerte werden weiter angezeigt.";
                    return BuildSnapshot(region, current);
                }
                // Only successful returned rows receive a new timestamp. Missing rows
                // keep their old quote and are never silently made fresh or zeroed.
                foreach (var (id, price) in received) current.Prices[id] = price;
                if (arsha is { Prices.Count: > 0 }) UpdateSource(current.Arsha, arsha, pricesCommitted: true);
                if (pearlAbyss is { Prices.Count: > 0 }) UpdateSource(current.PearlAbyss, pearlAbyss, pricesCommitted: true);
                current.LastRefresh = _time.GetUtcNow();
                current.Message = received.Count == LootPriceCatalog.MarketItemIds.Count
                    ? $"Marktpreise von {string.Join(" und ", sources)} aktualisiert."
                    : $"Marktpreise teilweise aktualisiert ({string.Join(" und ", sources)}); fehlende Werte bleiben gekennzeichnet.";
                if (current.Arsha.RateLimited && current.Arsha.NextAttempt > current.LastRefresh ||
                    current.PearlAbyss.RateLimited && current.PearlAbyss.NextAttempt > current.LastRefresh)
                    current.Message += " Eine Marktpreisquelle begrenzt weitere Anfragen; ihre Wartefrist wird eingehalten.";
                var snapshot = BuildSnapshot(region, current, received.Keys.ToHashSet());
                SaveCache();
                return snapshot;
            }
        }
        finally { _refreshGate.Release(); }
    }

    private async Task<SourcePrices> FetchArshaPricesAsync(string region, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        try
        {
            var ids = string.Join(',', LootPriceCatalog.MarketItemIds);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.arsha.io/v2/{region}/GetWorldMarketSubList?id={ids}&lang=en");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd(AppBranding.UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                var individual = await FetchIndividualPricesAsync(region, _time.GetUtcNow(), deadline.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new(individual.Prices, individual.RateLimited ? "Arsha: Marktpreisquelle begrenzt Anfragen." :
                    individual.Prices.Count == 0 ? "Arsha: Marktpreisquelle derzeit nicht verfügbar." : null,
                    individual.RetryAfter, individual.RateLimited);
            }
            if (!response.IsSuccessStatusCode)
                return new([], response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Arsha: Marktpreisquelle begrenzt Anfragen." : "Arsha: Marktpreisquelle derzeit nicht verfügbar.",
                    RetryAfter(response), response.StatusCode == HttpStatusCode.TooManyRequests);
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes ||
                response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                return new([], "Arsha: Marktpreisquelle liefert keine gültigen Preisdaten.");
            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
            var received = ParsePrices(bytes, _time.GetUtcNow());
            return new(received, received.Count == 0 ? "Arsha: Keine passenden Marktpreise erhalten." : null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            JsonException or OperationCanceledException or InvalidDataException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            return new([], "Arsha: Marktpreise konnten nicht aktualisiert werden.");
        }
    }

    private async Task<IndividualPrices> FetchIndividualPricesAsync(string region, DateTimeOffset receivedAt, CancellationToken token)
    {
        var received = new System.Collections.Concurrent.ConcurrentDictionary<int, MarketPrice>();
        using var slots = new SemaphoreSlim(4);
        var stop = 0;
        var rateGate = new object();
        var rateLimited = false;
        TimeSpan? retryAfter = null;
        await Task.WhenAll(LootPriceCatalog.MarketItemIds.Select(async id =>
        {
            var entered = false;
            try
            {
                await slots.WaitAsync(token).ConfigureAwait(false);
                entered = true;
                if (Volatile.Read(ref stop) != 0) return;
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.arsha.io/v2/{region}/GetWorldMarketSubList?id={id}&lang=en");
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.UserAgent.ParseAdd(AppBranding.UserAgent);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                {
                    Interlocked.Exchange(ref stop, 1);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var requested = RetryAfter(response);
                        lock (rateGate)
                        {
                            rateLimited = true;
                            if (requested is { } wait && (retryAfter is null || wait > retryAfter)) retryAfter = wait;
                        }
                    }
                    return;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumResponseBytes ||
                    response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml") return;
                await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, token).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                var prices = ParsePrices(bytes, receivedAt);
                if (prices.TryGetValue(id, out var price)) received[id] = price;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
                OperationCanceledException or InvalidDataException) { }
            finally { if (entered) slots.Release(); }
        })).ConfigureAwait(false);
        return new(received.ToDictionary(pair => pair.Key, pair => pair.Value), rateLimited, retryAfter);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter?.Delta ??
        (response.Headers.RetryAfter?.Date is { } date ? date - _time.GetUtcNow() : null);

    private static TimeSpan ClampRetryAfter(TimeSpan requested) => requested > TimeSpan.FromHours(1)
        ? TimeSpan.FromHours(1) : requested < TimeSpan.Zero ? TimeSpan.Zero : requested;

    private void UpdateSource(SourceState source, SourcePrices result, bool pricesCommitted = false)
    {
        TimeSpan wait;
        if (result.Prices.Count > 0)
        {
            // Cancellation during the fallback must not suppress an uncommitted
            // primary result for ten minutes. Server limits still apply immediately.
            if (!pricesCommitted)
            {
                if (result.RetryAfter is null && !result.RateLimited) return;
                wait = result.RateLimited ? TimeSpan.FromSeconds(30) : TimeSpan.Zero;
            }
            else
            {
                source.Failures = 0;
                wait = RefreshInterval;
            }
        }
        else
        {
            source.Failures = Math.Min(source.Failures + 1, 6);
            wait = TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, source.Failures - 1)));
        }
        if (result.RetryAfter is { } requested && requested > wait) wait = ClampRetryAfter(requested);
        source.NextAttempt = _time.GetUtcNow() + wait;
        source.Error = result.Error;
        source.RateLimited = result.RateLimited;
    }

    private LootPriceSnapshot BuildSnapshot(string region, RegionCache cache, IReadOnlySet<int>? refreshedIds = null)
    {
        var now = _time.GetUtcNow();
        var quotes = LootPriceCatalog.FixedSnapshot(region).Quotes.Values.ToList();
        foreach (var definition in LootPriceCatalog.Definitions.Where(static item => item.Kind == LootPriceKind.Market))
        {
            if (!cache.Prices.TryGetValue(definition.MarketItemId!.Value, out var price)) continue;
            if (price.RetrievedAt > now) continue;
            quotes.Add(new(definition.ItemName, price.UnitPrice, 0,
                refreshedIds?.Contains(definition.MarketItemId.Value) == true ? LootPriceOrigin.LiveMarket : LootPriceOrigin.CachedMarket,
                price.RetrievedAt, now - price.RetrievedAt >= RefreshInterval));
        }
        if (cache.Prices.TryGetValue(721003, out var caphras) && caphras.RetrievedAt <= now &&
            cache.Prices.TryGetValue(16001, out var blackStone) && blackStone.RetrievedAt <= now &&
            caphras.UnitPrice >= blackStone.UnitPrice)
        {
            // Companion stores an integer unit value for dust; every nonzero tax
            // flag (including dust's 2) taxes that complete value. Never tax only
            // the Caphras revenue while leaving the ingredient deduction untaxed.
            quotes.Add(new("Ancient Spirit Dust", (caphras.UnitPrice - blackStone.UnitPrice) / 5, 0,
                LootPriceOrigin.DerivedMarket,
                caphras.RetrievedAt < blackStone.RetrievedAt ? caphras.RetrievedAt : blackStone.RetrievedAt,
                now - caphras.RetrievedAt >= RefreshInterval || now - blackStone.RetrievedAt >= RefreshInterval));
        }
        var last = cache.Prices.Count == 0 ? (DateTimeOffset?)null : cache.Prices.Values.Max(static price => price.RetrievedAt);
        var status = cache.Message.Length != 0 ? cache.Message :
            cache.Prices.Count == 0 ? "NPC-/Festwerte verfügbar; Marktpreise fehlen." : "Gespeicherte Marktpreise geladen.";
        if (quotes.Any(static quote => quote.IsStale)) status += " Marktpreise veraltet (Cache).";
        return new(region, quotes, last, status);
    }

    private static Dictionary<int, MarketPrice> ParsePrices(ReadOnlyMemory<byte> bytes, DateTimeOffset timestamp)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        // A single item returns a flat array of enhancement rows. A multi-ID
        // request returns an outer array containing one such array per item.
        var rows = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray()
                .SelectMany(static value => value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().ToArray() : new[] { value }).ToArray() :
            root.ValueKind == JsonValueKind.Object ? new[] { root } : [];
        if (rows.Length > 5000) throw new InvalidDataException("Too many price rows.");
        var result = new Dictionary<int, MarketPrice>();
        foreach (var item in rows)
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.Number ||
                !idValue.TryGetInt32(out var id) ||
                !LootPriceCatalog.MarketItemIds.Contains(id) ||
                !item.TryGetProperty("sid", out var sidValue) || sidValue.ValueKind != JsonValueKind.Number ||
                !sidValue.TryGetInt32(out var sid) || sid != 0 ||
                !item.TryGetProperty("basePrice", out var priceValue) || priceValue.ValueKind != JsonValueKind.Number ||
                !priceValue.TryGetInt64(out var price) || price <= 0)
                continue;
            if (result.TryGetValue(id, out var duplicate) && duplicate.UnitPrice != price)
                throw new InvalidDataException("Contradictory duplicate price.");
            result[id] = new(price, timestamp);
        }
        return result;
    }

    private RegionCache GetRegion(string region)
    {
        if (!_regions.TryGetValue(region, out var cache)) _regions.Add(region, cache = new());
        return cache;
    }

    private void LoadCacheOnce()
    {
        if (_cacheLoaded) return;
        _cacheLoaded = true;
        if (_cachePath is null) return;
        try
        {
            if (!File.Exists(_cachePath) || new FileInfo(_cachePath).Length > MaximumResponseBytes) return;
            using var stream = File.OpenRead(_cachePath);
            var stored = JsonSerializer.Deserialize<PersistedCache>(stream);
            if (stored?.SchemaVersion != 1 || stored.Regions is null) return;
            var now = _time.GetUtcNow();
            foreach (var (region, prices) in stored.Regions)
            {
                if (!LootPriceCatalog.SupportedRegions.Contains(region, StringComparer.Ordinal) || prices is null) continue;
                var cache = GetRegion(region);
                foreach (var (id, price) in prices)
                    if (LootPriceCatalog.MarketItemIds.Contains(id) && price is not null && price.UnitPrice > 0 &&
                        price.RetrievedAt > DateTimeOffset.UnixEpoch && price.RetrievedAt <= now)
                        cache.Prices[id] = price;
                // Persisted quotes may avoid an immediate redundant fetch only when
                // all requested IDs were saved recently; a partial cache must refresh.
                if (cache.Prices.Count == LootPriceCatalog.MarketItemIds.Count)
                    cache.LastRefresh = cache.Prices.Values.Min(static price => price.RetrievedAt);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void SaveCache()
    {
        if (_cachePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            var stored = new PersistedCache(1, _regions.ToDictionary(static region => region.Key,
                static region => region.Value.Prices));
            var temp = _cachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(stored));
            File.Move(temp, _cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => _http.Dispose();

    private sealed class RegionCache
    {
        public Dictionary<int, MarketPrice> Prices { get; } = new();
        public DateTimeOffset? LastRefresh { get; set; }
        public SourceState Arsha { get; } = new();
        public SourceState PearlAbyss { get; } = new();
        public string Message { get; set; } = "";
    }

    private sealed class SourceState
    {
        public DateTimeOffset NextAttempt { get; set; }
        public int Failures { get; set; }
        public string? Error { get; set; }
        public bool RateLimited { get; set; }
    }

    private sealed record MarketPrice(long UnitPrice, DateTimeOffset RetrievedAt);
    private sealed record SourcePrices(Dictionary<int, MarketPrice> Prices, string? Error = null,
        TimeSpan? RetryAfter = null, bool RateLimited = false);
    private sealed record IndividualPrices(Dictionary<int, MarketPrice> Prices, bool RateLimited, TimeSpan? RetryAfter);
    private sealed record PersistedCache(int SchemaVersion, Dictionary<string, Dictionary<int, MarketPrice>> Regions);
}
