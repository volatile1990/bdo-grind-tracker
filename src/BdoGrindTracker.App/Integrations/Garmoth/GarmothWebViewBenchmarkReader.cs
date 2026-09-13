using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BdoGrindTracker.App.Integrations.Garmoth;

internal sealed record GarmothBenchmarkPayload(byte[] Collective, byte[] Metadata);

internal interface IGarmothBenchmarkReader
{
    Task<GarmothBenchmarkPayload> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Observes the public overview's own JSON requests in an isolated, anonymous browser.</summary>
internal sealed class GarmothWebViewBenchmarkReader : IGarmothBenchmarkReader
{
    internal const string OverviewUrl = "https://garmoth.com/grind-tracker/best-grind-spots";
    internal const int MaximumResponseBytes = 8 * 1024 * 1024;
    private readonly Control _owner;
    private readonly string _profileDirectory;

    internal GarmothWebViewBenchmarkReader(Control owner, string profileDirectory)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        _profileDirectory = Path.GetFullPath(profileDirectory);
    }

    public async Task<GarmothBenchmarkPayload> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var readToken = deadline.Token;
        var completion = new TaskCompletionSource<GarmothBenchmarkPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = 0;
        void OwnerDisposed(object? sender, EventArgs args)
        {
            deadline.Cancel();
            completion.TrySetException(new ObjectDisposedException(nameof(_owner)));
        }
        _owner.Disposed += OwnerDisposed;
        using var cancellation = readToken.Register(() =>
        {
            // A canceled callback still queued on the UI thread must not create a browser.
            if (Interlocked.CompareExchange(ref dispatched, -1, 0) == 0)
                completion.TrySetCanceled(readToken);
        });
        async void ReadOnOwner()
        {
            if (Interlocked.CompareExchange(ref dispatched, 1, 0) != 0) return;
            try { completion.TrySetResult(await ReadOnUiThreadAsync(readToken)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(readToken); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        try
        {
            if (_owner.IsDisposed || !_owner.IsHandleCreated)
                throw new ObjectDisposedException(nameof(_owner));
            if (_owner.InvokeRequired) _owner.BeginInvoke((Action)ReadOnOwner);
            else ReadOnOwner();
            return await completion.Task.ConfigureAwait(false);
        }
        finally { _owner.Disposed -= OwnerDisposed; }
    }

    private async Task<GarmothBenchmarkPayload> ReadOnUiThreadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_owner.IsDisposed) throw new ObjectDisposedException(nameof(_owner));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var readToken = lifetime.Token;
        using var view = new WebView2
        {
            Visible = false, TabStop = false, Size = new Size(1024, 768),
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = _profileDirectory, ProfileName = "GrindcrestBenchmarks", IsInPrivateModeEnabled = true,
            },
        };
        _owner.Controls.Add(view);
        try
        {
            // Force only the child HWND into existence; it is never shown or focused.
            _ = view.Handle;
            await view.EnsureCoreWebView2Async().WaitAsync(readToken);
            readToken.ThrowIfCancellationRequested();
            var core = view.CoreWebView2;
            var settings = core.Settings;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            core.IsMuted = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; };
            core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
            core.ClientCertificateRequested += (_, args) => args.Cancel = true;
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!IsAllowedResource(args.Request.Uri))
                    args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
            };
            var received = new TaskCompletionSource<GarmothBenchmarkPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            byte[]? collective = null, metadata = null;
            var collectiveClaimed = false;
            var metadataClaimed = false;
            core.NavigationStarting += (_, args) =>
            {
                if (IsOverview(args.Uri)) return;
                args.Cancel = true;
                received.TrySetException(new InvalidDataException("Garmoth hat die öffentliche Übersicht verlassen."));
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (args.HttpStatusCode >= 400)
                    received.TrySetException(new HttpRequestException("Garmoth konnte nicht geladen werden.", null,
                        (HttpStatusCode)args.HttpStatusCode));
                else if (!args.IsSuccess)
                    received.TrySetException(new IOException("Die öffentliche Garmoth-Übersicht konnte nicht geladen werden."));
            };
            core.ProcessFailed += (_, _) => received.TrySetException(new IOException("Der Garmoth-Datenabruf wurde unterbrochen."));
            core.WebResourceResponseReceived += async (_, args) =>
            {
                try
                {
                    if (received.Task.IsCompleted || readToken.IsCancellationRequested ||
                        !string.Equals(args.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) ||
                        !TryMatchResponse(args.Request.Uri, out var isCollective, out var batchIndex, out var batchCount)) return;
                    if (isCollective ? collectiveClaimed : metadataClaimed) return;
                    if (isCollective) collectiveClaimed = true;
                    else metadataClaimed = true;
                    ValidateResponse(args.Response);
                    // GetContentAsync is a UI-thread COM call. Only its thread-safe stream
                    // is read on a worker because WebView streams may perform blocking reads.
                    var stream = await GetContentAsync(args.Response, readToken);
                    var pendingRead = Task.Run(async () =>
                    {
                        using (stream) return await ReadBoundedAsync(stream, readToken).ConfigureAwait(false);
                    }, CancellationToken.None);
                    byte[] bytes;
                    try { bytes = await pendingRead.WaitAsync(readToken); }
                    catch (OperationCanceledException)
                    {
                        // Cancellation ends the UI wait; the worker still owns its stream.
                        _ = ObserveLateReadAsync(pendingRead);
                        throw;
                    }
                    using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                    if (isCollective)
                    {
                        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ungültige Garmoth-Statistik.");
                        collective = bytes;
                    }
                    else if (batchIndex >= 0)
                    {
                        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != batchCount ||
                            json.RootElement[batchIndex].ValueKind != JsonValueKind.Object)
                            throw new InvalidDataException("Ungültige Garmoth-Metadaten.");
                        // Preserve the procedure envelope expected by the existing parser.
                        metadata = JsonSerializer.SerializeToUtf8Bytes(json.RootElement[batchIndex]);
                    }
                    else
                    {
                        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ungültige Garmoth-Metadaten.");
                        metadata = bytes;
                    }
                    if (collective is not null && metadata is not null)
                        received.TrySetResult(new(collective, metadata));
                }
                catch (OperationCanceledException) { received.TrySetCanceled(token); }
                catch (Exception error) { received.TrySetException(error); }
            };
            core.Navigate(OverviewUrl);
            return await received.Task.WaitAsync(readToken);
        }
        finally
        {
            lifetime.Cancel();
            // Removing and disposing the child also releases all per-read event handlers.
            if (!_owner.IsDisposed) _owner.Controls.Remove(view);
            view.Dispose();
        }
    }

    private static bool IsOverview(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host == "garmoth.com" &&
        uri.AbsolutePath.TrimEnd('/') == "/grind-tracker/best-grind-spots";

    private static bool IsAllowedResource(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        (uri.Scheme is "data" or "blob" || uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
            uri.Host is "garmoth.com" or "www.garmoth.com" or "api.garmoth.com" or "assets.garmoth.com");

    internal static bool TryMatchResponse(string address, out bool collective, out int batchIndex, out int batchCount)
    {
        collective = false;
        batchIndex = -1;
        batchCount = 0;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        if (uri.Host == "api.garmoth.com" && uri.AbsolutePath == "/api/grind-tracker/collective/all")
            return collective = true;
        const string prefix = "/api/trpc/";
        if (uri.Host != "garmoth.com" || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var procedures = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..]).Split(',');
        if (procedures.Length > 64 || procedures.Count(value => value == "grindMeta.list") != 1) return false;
        var batched = uri.Query.TrimStart('?').Split('&').Any(value => value == "batch=1");
        if (!batched) return procedures.Length == 1 && procedures[0] == "grindMeta.list";
        batchIndex = Array.IndexOf(procedures, "grindMeta.list");
        batchCount = procedures.Length;
        return true;
    }

    private static void ValidateResponse(CoreWebView2WebResourceResponseView response)
    {
        if (response.StatusCode is < 200 or >= 300)
            throw new HttpRequestException("Garmoth konnte nicht geladen werden.", null, (HttpStatusCode)response.StatusCode);
        var headers = response.Headers;
        var type = headers.Contains("Content-Type") ? headers.GetHeader("Content-Type").Split(';')[0].Trim() : "";
        if (!string.Equals(type, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Garmoth hat keine JSON-Daten geliefert.");
        if (headers.Contains("Content-Length") && long.TryParse(headers.GetHeader("Content-Length"), NumberStyles.None, CultureInfo.InvariantCulture,
                out var length) && length > MaximumResponseBytes)
            throw new InvalidDataException("Die Garmoth-Antwort ist zu groß.");
    }

    private static async Task<Stream> GetContentAsync(CoreWebView2WebResourceResponseView response, CancellationToken token)
    {
        var pending = response.GetContentAsync();
        try { return await pending.WaitAsync(token) ?? throw new InvalidDataException("Die Garmoth-Antwort ist leer."); }
        catch (OperationCanceledException)
        {
            _ = DisposeLateContentAsync(pending);
            throw;
        }
    }

    private static async Task DisposeLateContentAsync(Task<Stream> pending)
    {
        try { (await pending.ConfigureAwait(false))?.Dispose(); }
        catch (Exception) { /* Canceled/closed WebViews may never expose a response stream. */ }
    }

    private static async Task ObserveLateReadAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { /* The canceled request has already reported its outcome. */ }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) return output.ToArray();
            if (output.Length + count > MaximumResponseBytes)
                throw new InvalidDataException("Die Garmoth-Antwort ist zu groß.");
            output.Write(buffer, 0, count);
        }
    }
}
