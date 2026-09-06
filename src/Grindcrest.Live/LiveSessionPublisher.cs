using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;

namespace Grindcrest.Live;

/// <summary>
/// A bounded, serial HTTP worker. The UI only offers immutable snapshots; capture
/// never waits for the network. End requests always follow the final attempted PUT.
/// </summary>
public sealed class LiveSessionPublisher : IDisposable
{
    private readonly HttpClient _http;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _interval;
    private LiveSessionUpdate? _desired;
    private bool _stopping;
    private int _disposed;
    private string _status = "Wartet auf eine Session";

    public string Status => Volatile.Read(ref _status);

    public LiveSessionPublisher(Uri endpoint, string token, HttpMessageHandler? handler = null,
        TimeProvider? clock = null, TimeSpan? interval = null)
    {
        if (!LiveEndpoint.TryParse(endpoint.AbsoluteUri, out var normalized) || !LiveEndpoint.IsValidToken(token))
            throw new ArgumentException("Live-Verbindung ungültig.");
        _clock = clock ?? TimeProvider.System;
        _interval = interval ?? TimeSpan.FromSeconds(15);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = normalized, Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _worker = Task.Run(RunAsync);
    }

    public void Offer(LiveSessionUpdate? snapshot)
    {
        lock (_sync)
        {
            if (_stopping) return;
            // Prevent later UI/producer mutations from changing an in-flight body.
            _desired = snapshot is null ? null : snapshot with { Loot = new(snapshot.Loot) };
            _signal.Writer.TryWrite(true);
        }
    }

    public async Task StopAsync()
    {
        lock (_sync)
        {
            _stopping = true;
            _desired = null;
            _signal.Writer.TryComplete();
        }
        await _worker.ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        Guid? localId = null;
        Guid? publicId = null;
        DateTimeOffset sharedAt = default;
        long sequence = 0;
        long? lastAttempt = null;
        bool? lastPaused = null;
        try
        {
            await foreach (var _ in _signal.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                LiveSessionUpdate? snapshot;
                lock (_sync) snapshot = _stopping ? null : _desired;
                if (localId != snapshot?.SessionId)
                {
                    if (publicId is { } oldId) await EndAsync(oldId).ConfigureAwait(false);
                    localId = snapshot?.SessionId;
                    publicId = snapshot is null ? null : Guid.NewGuid();
                    sharedAt = _clock.GetUtcNow();
                    sequence = 0;
                    lastAttempt = null;
                    lastPaused = null;
                }
                if (snapshot is null)
                {
                    Volatile.Write(ref _status, "Wartet auf eine Session");
                    continue;
                }
                if (lastAttempt is { } stamp && lastPaused == snapshot.Paused &&
                    _clock.GetElapsedTime(stamp) < _interval) continue;
                lastAttempt = _clock.GetTimestamp();
                lastPaused = snapshot.Paused;
                var outgoing = snapshot with { SessionId = publicId!.Value, SharedAt = sharedAt, Sequence = ++sequence };
                if (outgoing.Validate(_clock.GetUtcNow()) is not null)
                {
                    Volatile.Write(ref _status, "Sessiondaten können noch nicht geteilt werden");
                    continue;
                }
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1/session") { Content = JsonContent.Create(outgoing) };
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token).ConfigureAwait(false);
                    Volatile.Write(ref _status, response.IsSuccessStatusCode
                        ? snapshot.Paused ? "Öffentlich · pausiert" : "Öffentlich · verbunden"
                        : response.StatusCode == HttpStatusCode.Unauthorized ? "Schreibschlüssel prüfen"
                        : "Übertragung unterbrochen · erneuter Versuch folgt");
                }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
                {
                    Volatile.Write(ref _status, "Offline · erneuter Versuch folgt");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            if (!_lifetime.IsCancellationRequested && publicId is { } id)
                await EndAsync(id).ConfigureAwait(false);
        }
    }

    private async Task EndAsync(Guid id)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/v1/session/{id}");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Server expiry removes this publication even after ambiguous network failure.
        }
        catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_sync)
        {
            _stopping = true;
            _desired = null;
            _signal.Writer.TryComplete();
            _lifetime.Cancel();
        }
        _http.Dispose();
        _ = _worker.ContinueWith(completed =>
        {
            _ = completed.Exception;
            _lifetime.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
