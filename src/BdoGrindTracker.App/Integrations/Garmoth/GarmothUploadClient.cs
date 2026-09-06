using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace BdoGrindTracker.App.Integrations.Garmoth;

internal enum GarmothUploadStatus { Succeeded, Rejected, OutcomeUnknown, AlreadySubmitted }

internal sealed record GarmothUploadResult(GarmothUploadStatus Status, string Message)
{
    public bool BlocksAnotherUpload => Status is GarmothUploadStatus.Succeeded or
        GarmothUploadStatus.OutcomeUnknown or GarmothUploadStatus.AlreadySubmitted;
}

/// <summary>
/// Sends only on an explicit UploadAsync call. No login probing, automatic retries,
/// cookies, redirects, telemetry, or credential persistence. A possibly committed
/// session cannot be submitted again through this client.
/// </summary>
internal sealed class GarmothUploadClient : IDisposable
{
    public static Uri UploadEndpoint { get; } =
        new("https://api.garmoth.com/api/external/grind-tracker/sessions/create");
    public static Uri SettingsPage { get; } = new("https://garmoth.com/settings");
    public static Uri TrackerPage { get; } = new("https://garmoth.com/grind-tracker");

    private readonly HttpClient _http;
    private readonly TimeSpan _requestTimeout;
    private readonly ConcurrentDictionary<Guid, byte> _submitted = new();

    public GarmothUploadClient() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    }) { }

    internal GarmothUploadClient(HttpMessageHandler handler, TimeSpan? requestTimeout = null)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        // A linked deadline below also covers the body after ResponseHeadersRead.
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<GarmothUploadResult> UploadAsync(GarmothSessionDraft draft, string apiKey,
        CancellationToken cancellationToken = default)
    {
        // All local checks happen before a request or attempt marker exists.
        var payload = GarmothSessionPayload.Create(draft);
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 ||
            apiKey.Any(static character => character < 0x21 || character > 0x7e))
            return new(GarmothUploadStatus.Rejected, "Bitte einen gültigen Garmoth-API-Key eingeben (ohne Leerzeichen).");
        if (cancellationToken.IsCancellationRequested)
            return new(GarmothUploadStatus.Rejected, "Upload wurde vor dem Senden abgebrochen.");
        if (!_submitted.TryAdd(draft.LocalSessionId, 0))
            return new(GarmothUploadStatus.AlreadySubmitted,
                "Diese Sitzung wurde bereits gesendet oder ihr Upload-Ergebnis ist unklar. Bitte zuerst in Garmoth prüfen.");

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_requestTimeout);
            var requestToken = deadline.Token;
            using var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint);
            request.Headers.Add("apiKey", apiKey);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd(AppBranding.UserAgent);
            request.Content = JsonContent.Create(payload);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                requestToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is "text/html" or "application/xhtml+xml") return Unknown();
                // Native Companion accepts HTTP success. Additionally reject an explicit
                // JSON error, without ever echoing an untrusted response or its secrets.
                if (mediaType == "application/json")
                {
                    if (response.Content.Headers.ContentLength is > 65_536) return Unknown();
                    await response.Content.LoadIntoBufferAsync(65_536, requestToken).ConfigureAwait(false);
                    var body = await response.Content.ReadAsByteArrayAsync(requestToken).ConfigureAwait(false);
                    if (body.Length != 0)
                    {
                        using var document = JsonDocument.Parse(body);
                        if (IsExplicitFailure(document.RootElement))
                            return Rejected(draft.LocalSessionId,
                                "Garmoth hat den Upload abgelehnt. API-Key und Sitzungsdaten prüfen.");
                    }
                }
                return new(GarmothUploadStatus.Succeeded, "Sitzung erfolgreich an Garmoth übertragen.");
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                    Rejected(draft.LocalSessionId, "Garmoth hat die Sitzungsdaten abgelehnt. Klasse, Loot und Dauer prüfen."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    Rejected(draft.LocalSessionId, "Garmoth hat den Zugriff abgelehnt. API-Key und dessen Berechtigung prüfen."),
                HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed =>
                    Rejected(draft.LocalSessionId, "Die Garmoth-Uploadschnittstelle ist nicht verfügbar oder hat sich geändert."),
                HttpStatusCode.TooManyRequests =>
                    Rejected(draft.LocalSessionId, "Garmoth begrenzt derzeit Anfragen. Später manuell erneut versuchen."),
                // Conflicts, redirects and server errors may follow a committed write.
                _ => Unknown(),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
            or JsonException or IOException or InvalidOperationException)
        {
            // Even timeout/cancellation can arrive AFTER Garmoth has saved the session.
            // Keep the guard. Never include exception.Message (may contain a token).
            return Unknown();
        }
    }

    private GarmothUploadResult Rejected(Guid sessionId, string message)
    {
        _submitted.TryRemove(sessionId, out _);
        return new(GarmothUploadStatus.Rejected, message);
    }

    private static GarmothUploadResult Unknown() => new(GarmothUploadStatus.OutcomeUnknown,
        "Upload-Ergebnis unklar. Die Sitzung könnte bereits gespeichert sein. Bitte in Garmoth prüfen; kein automatischer Wiederholungsversuch.");

    private static bool IsExplicitFailure(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            return true;
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
            return true;
        return root.TryGetProperty("error", out var error) && error.ValueKind is not
            (JsonValueKind.Null or JsonValueKind.False) &&
            (error.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(error.GetString()));
    }

    public void Dispose() => _http.Dispose();
}
