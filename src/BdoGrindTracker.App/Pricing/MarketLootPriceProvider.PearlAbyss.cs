using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BdoGrindTracker.App.Pricing;

internal sealed partial class MarketLootPriceProvider
{
    private async Task<SourcePrices> FetchPearlAbyssPricesAsync(string region,
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestedIds = itemIds.Where(LootPriceCatalog.MarketItemIds.Contains).ToHashSet();
        if (requestedIds.Count == 0) return new(new());
        var endpoint = region switch
        {
            "eu" => "https://eu-trade.naeu.playblackdesert.com/Trademarket/GetWorldMarketSearchList",
            "na" => "https://na-trade.naeu.playblackdesert.com/Trademarket/GetWorldMarketSearchList",
            _ => throw new ArgumentException("Unsupported market region.", nameof(region)),
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        TimeSpan? retryAfter = null;
        try
        {
            // Velia documents this anonymous public search endpoint. It returns
            // the base price for each ID in one response, without enhancement rows.
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    searchResult = string.Join(',', requestedIds),
                }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("BlackDesert");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            retryAfter = RetryAfter(response);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
                return new(new(), response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Pearl-Abyss-Marktpreisquelle begrenzt Anfragen."
                    : "Pearl-Abyss-Marktpreisquelle derzeit nicht verfügbar.",
                    retryAfter, response.StatusCode == HttpStatusCode.TooManyRequests);
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes ||
                response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                return new(new(), "Pearl-Abyss-Marktpreisquelle liefert keine gültigen Preisdaten.", retryAfter);
            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
            var prices = ParsePearlAbyssPrices(bytes, requestedIds, _time.GetUtcNow());
            cancellationToken.ThrowIfCancellationRequested();
            return new(prices, prices.Count == 0 ? "Keine passenden Pearl-Abyss-Marktpreise erhalten." : null,
                retryAfter);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
            OperationCanceledException or InvalidDataException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            return new(new(), "Pearl-Abyss-Marktpreise konnten nicht aktualisiert werden.", retryAfter);
        }
    }

    private static Dictionary<int, MarketPrice> ParsePearlAbyssPrices(ReadOnlyMemory<byte> bytes,
        HashSet<int> requestedIds, DateTimeOffset timestamp)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid market response envelope.");
        // Duplicate envelope fields are ambiguous even when JsonDocument would
        // silently choose the last occurrence.
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!fields.Add(property.Name)) throw new InvalidDataException("Duplicate market response field.");
        if (!root.TryGetProperty("resultCode", out var resultCode) ||
            resultCode.ValueKind != JsonValueKind.Number || !resultCode.TryGetInt32(out var code) || code != 0 ||
            !root.TryGetProperty("resultMsg", out var resultMsg) || resultMsg.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid market response envelope.");

        var remaining = resultMsg.GetString().AsSpan();
        var prices = new Dictionary<int, MarketPrice>();
        var rowCount = 0;
        while (!remaining.IsEmpty)
        {
            if (++rowCount > 5000) throw new InvalidDataException("Too many market price rows.");
            var separator = remaining.IndexOf('|');
            var row = separator < 0 ? remaining : remaining[..separator];
            remaining = separator < 0 ? [] : remaining[(separator + 1)..];
            // Four unsigned decimal fields fit within 70 characters. Bound the
            // individual row before splitting, including malicious long fields.
            if (row.IsEmpty || row.Length > 70) throw new InvalidDataException("Invalid market price row.");
            var values = row.ToString().Split('-');
            if (values.Length != 4 ||
                !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
                !long.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                !long.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out var price) || price <= 0 ||
                !long.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("Invalid market price row.");
            if (!requestedIds.Contains(id)) continue;
            if (prices.TryGetValue(id, out var duplicate) && duplicate.UnitPrice != price)
                throw new InvalidDataException("Contradictory duplicate market price.");
            prices[id] = new(price, timestamp);
        }
        return prices;
    }
}
