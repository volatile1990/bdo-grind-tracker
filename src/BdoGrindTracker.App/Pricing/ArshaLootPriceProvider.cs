using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace BdoGrindTracker.App.Pricing;

/// <summary>
/// Anonymous batched market GET, with bounded individual requests after a batch HTTP 500. Region-isolated persisted prices
/// remain usable offline but retain their old timestamps and stale markers.
/// No captured images, quantities, class, session, account or API key are sent.
/// </summary>
internal sealed class ArshaLootPriceProvider : ILootPriceProvider
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

    public ArshaLootPriceProvider() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false,
    }, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BdoGrindTracker", "market-prices-v1.json")) { }

    internal ArshaLootPriceProvider(HttpMessageHandler handler, string? cachePath = null,
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
            return BuildSnapshot(region, GetRegion(region), live: false);
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
                if (previous.NextAttempt > now ||
                    previous.LastRefresh is { } refreshed && refreshed <= now && now - refreshed < RefreshInterval)
                    return BuildSnapshot(region, previous, live: false);
            }

            Dictionary<int, MarketPrice> received;
            DateTimeOffset receivedAt;
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
                    receivedAt = _time.GetUtcNow();
                    received = await FetchIndividualPricesAsync(region, receivedAt, deadline.Token).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (received.Count == 0) return Failed(region, "Marktpreisquelle derzeit nicht verfügbar.");
                }
                else
                {
                if (!response.IsSuccessStatusCode)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ??
                        (response.Headers.RetryAfter?.Date is { } date ? date - now : (TimeSpan?)null);
                    return Failed(region, response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Marktpreisquelle begrenzt Anfragen."
                        : "Marktpreisquelle derzeit nicht verfügbar.", retryAfter);
                }
                if (response.Content.Headers.ContentLength is > MaximumResponseBytes ||
                    response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                    return Failed(region, "Marktpreisquelle liefert keine gültigen Preisdaten.");
                await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
                receivedAt = _time.GetUtcNow();
                received = ParsePrices(bytes, receivedAt);
                }
                if (received.Count == 0) return Failed(region, "Keine passenden Marktpreise erhalten.");
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or
                JsonException or OperationCanceledException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                return Failed(region, "Marktpreise konnten nicht aktualisiert werden.");
            }

            LootPriceSnapshot snapshot;
            lock (_sync)
            {
                var current = GetRegion(region);
                // Only successful returned rows receive a new timestamp. Missing rows
                // keep their old quote and are never silently made fresh or zeroed.
                foreach (var (id, price) in received) current.Prices[id] = price;
                current.LastRefresh = receivedAt;
                current.NextAttempt = current.LastRefresh.Value + RefreshInterval;
                current.Failures = 0;
                current.Message = received.Count == LootPriceCatalog.MarketItemIds.Count
                    ? "Marktpreise von Arsha aktualisiert."
                    : "Marktpreise teilweise aktualisiert; fehlende Werte bleiben gekennzeichnet.";
                snapshot = BuildSnapshot(region, current, live: true);
                SaveCache();
            }
            return snapshot;
        }
        finally { _refreshGate.Release(); }
    }

    private async Task<Dictionary<int, MarketPrice>> FetchIndividualPricesAsync(string region, DateTimeOffset receivedAt, CancellationToken token)
    {
        var received = new System.Collections.Concurrent.ConcurrentDictionary<int, MarketPrice>();
        using var slots = new SemaphoreSlim(4);
        var stop = 0;
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
        return received.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private LootPriceSnapshot Failed(string region, string message, TimeSpan? retryAfter = null)
    {
        lock (_sync)
        {
            var current = GetRegion(region);
            current.Failures = Math.Min(current.Failures + 1, 6);
            var backoff = TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, current.Failures - 1)));
            if (retryAfter is { } requested && requested > backoff)
                backoff = requested > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : requested;
            current.NextAttempt = _time.GetUtcNow() + backoff;
            current.Message = message + " Vorhandene Cache-/Festwerte werden weiter angezeigt.";
            return BuildSnapshot(region, current, live: false);
        }
    }

    private LootPriceSnapshot BuildSnapshot(string region, RegionCache cache, bool live)
    {
        var now = _time.GetUtcNow();
        var quotes = LootPriceCatalog.FixedSnapshot(region).Quotes.Values.ToList();
        foreach (var definition in LootPriceCatalog.Definitions.Where(static item => item.Kind == LootPriceKind.Market))
        {
            if (!cache.Prices.TryGetValue(definition.MarketItemId!.Value, out var price)) continue;
            if (price.RetrievedAt > now) continue;
            quotes.Add(new(definition.ItemName, price.UnitPrice, 0,
                live && cache.LastRefresh == price.RetrievedAt ? LootPriceOrigin.LiveMarket : LootPriceOrigin.CachedMarket,
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
        public DateTimeOffset NextAttempt { get; set; }
        public int Failures { get; set; }
        public string Message { get; set; } = "";
    }

    private sealed record MarketPrice(long UnitPrice, DateTimeOffset RetrievedAt);
    private sealed record PersistedCache(int SchemaVersion, Dictionary<string, Dictionary<int, MarketPrice>> Regions);
}
