using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class CaptureFailureDiagnosticsTests : IDisposable
{
    private const string PrivateText = "PRIVATE_OCR_VALUE_AND_SECRET";
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        $"BdoGrindTracker-CaptureFailureTests-{Guid.NewGuid():N}");
    private string FailurePath => Path.Combine(_directory, CaptureFailureDiagnostics.FileName);

    [Fact]
    public void SavesTechnicalFieldsWithoutMessageValueDataOrSourcePaths()
    {
        var error = CaptureCountError();
        error.Data["private"] = PrivateText;
        var timestamp = new DateTimeOffset(2026, 9, 7, 18, 0, 0, TimeSpan.FromHours(2));

        CaptureFailureDiagnostics.TryWrite(_directory, error, timestamp);

        var json = File.ReadAllText(FailurePath);
        Assert.DoesNotContain(PrivateText, json);
        Assert.DoesNotContain("C:\\", json);
        Assert.DoesNotContain(".cs", json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(new[] { "schemaVersion", "occurredAtUtc", "appVersion", "engineVersion", "exceptions" },
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(timestamp.ToUniversalTime(), root.GetProperty("occurredAtUtc").GetDateTimeOffset());
        Assert.Equal(LootDiagnosticFormat.EngineVersion, root.GetProperty("engineVersion").GetString());
        var exception = Assert.Single(root.GetProperty("exceptions").EnumerateArray());
        Assert.Equal(new[] { "type", "hResult", "paramName", "stack" },
            exception.EnumerateObject().Select(property => property.Name));
        Assert.Equal(typeof(ArgumentOutOfRangeException).FullName, exception.GetProperty("type").GetString());
        Assert.Equal("count", exception.GetProperty("paramName").GetString());
        Assert.Equal(error.HResult, exception.GetProperty("hResult").GetInt32());
        Assert.Contains(exception.GetProperty("stack").EnumerateArray(),
            frame => frame.GetString()!.EndsWith(".ThrowCountError", StringComparison.Ordinal));
    }

    [Fact]
    public void NormalStopsCreateNothingAndDoNotOverwriteTheLastFailure()
    {
        CaptureFailureDiagnostics.TryWrite(_directory, null, DateTimeOffset.UnixEpoch);
        Assert.False(Directory.Exists(_directory));
        CaptureFailureDiagnostics.TryWrite(_directory, CaptureCountError(), DateTimeOffset.UnixEpoch);
        var previous = File.ReadAllBytes(FailurePath);

        CaptureFailureDiagnostics.TryWrite(_directory, null, DateTimeOffset.UtcNow);

        Assert.Equal(previous, File.ReadAllBytes(FailurePath));
        CaptureFailureDiagnostics.TryWrite(_directory, new IOException(PrivateText), DateTimeOffset.UnixEpoch.AddSeconds(1));
        using var document = JsonDocument.Parse(File.ReadAllText(FailurePath));
        Assert.Equal(typeof(IOException).FullName,
            Assert.Single(document.RootElement.GetProperty("exceptions").EnumerateArray()).GetProperty("type").GetString());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void BoundsExceptionDepthTotalStackFramesAndFileBytes()
    {
        Exception? inner = null;
        for (var index = 0; index < 6; index++) inner = CaptureDeepError(inner);

        CaptureFailureDiagnostics.TryWrite(_directory, inner, DateTimeOffset.UnixEpoch);

        Assert.InRange(new FileInfo(FailurePath).Length, 1, CaptureFailureDiagnostics.MaximumBytes);
        using var document = JsonDocument.Parse(File.ReadAllText(FailurePath));
        var exceptions = document.RootElement.GetProperty("exceptions").EnumerateArray().ToArray();
        Assert.Equal(3, exceptions.Length);
        Assert.Equal(24, exceptions.Sum(exception => exception.GetProperty("stack").GetArrayLength()));
        Assert.DoesNotContain(PrivateText, File.ReadAllText(FailurePath));
    }

    [Fact]
    public void ParameterNamesContainingPathsAreOmittedAndUnwritableLocationIsHarmless()
    {
        CaptureFailureDiagnostics.TryWrite(_directory,
            new ArgumentException(PrivateText, @"C:\Users\private\ocr.txt"), DateTimeOffset.UnixEpoch);
        using var document = JsonDocument.Parse(File.ReadAllText(FailurePath));
        var exception = Assert.Single(document.RootElement.GetProperty("exceptions").EnumerateArray());
        Assert.False(exception.TryGetProperty("paramName", out _));
        var fileInsteadOfDirectory = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(fileInsteadOfDirectory, "owned test file");

        CaptureFailureDiagnostics.TryWrite(fileInsteadOfDirectory, CaptureCountError(), DateTimeOffset.UnixEpoch);

        Assert.Equal("owned test file", File.ReadAllText(fileInsteadOfDirectory));
    }

    [Fact]
    public async Task ServiceStopHandlerWritesOnlyFailuresAndPreservesOriginalErrorWhenLoggingFails()
    {
        await using var service = CreateService();
        NotifyStopped(service, null);
        Assert.False(File.Exists(FailurePath));
        var error = CaptureCountError();

        NotifyStopped(service, error);

        Assert.True(File.Exists(FailurePath));
        Assert.Same(error, LastCaptureError(service));
        var previous = File.ReadAllBytes(FailurePath);
        NotifyStopped(service, null);
        Assert.Equal(previous, File.ReadAllBytes(FailurePath));
        File.Delete(FailurePath);
        Directory.CreateDirectory(FailurePath);
        var nextError = new IOException(PrivateText);

        NotifyStopped(service, nextError);

        Assert.Same(nextError, LastCaptureError(service));
        Assert.True(Directory.Exists(FailurePath));
        Assert.False(File.Exists(FailurePath + ".tmp"));
    }

    private TrackerSessionService CreateService()
    {
        Directory.CreateDirectory(_directory);
        var settings = new SettingsStore();
        typeof(SettingsStore).GetField("_settingsPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(settings, Path.Combine(_directory, "settings.json"));
        return new TrackerSessionService(new PassiveCaptureSession(_ => new Bitmap(2, 2)),
            new EmptyAnalyzer(), settings, [], classDetector: () => CharacterClassDetection.Unknown,
            priceProvider: new FixedPrices(), garmothClient: new GarmothUploadClient(new NoHttp()),
            keyStore: new GarmothApiKeyStore(Path.Combine(_directory, "test-key.dpapi")),
            historyStore: new LootHistoryStore(Path.Combine(_directory, "history.json")));
    }

    private static void NotifyStopped(TrackerSessionService service, Exception? error) =>
        typeof(TrackerSessionService).GetMethod("CaptureSessionStopped", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [null, new CaptureSessionStoppedEventArgs(error)]);

    private static Exception? LastCaptureError(TrackerSessionService service) =>
        (Exception?)typeof(TrackerSessionService).GetField("_lastCaptureStopError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service);

    private static Exception CaptureCountError()
    {
        try { ThrowCountError(); }
        catch (ArgumentOutOfRangeException error) { return error; }
        throw new InvalidOperationException("The synthetic throw must run.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCountError() =>
        throw new ArgumentOutOfRangeException("count", PrivateText, @"C:\Users\private\ocr.txt " + PrivateText);

    private static Exception CaptureDeepError(Exception? inner)
    {
        try { ThrowDeep(40, inner); }
        catch (InvalidOperationException error) { return error; }
        throw new InvalidOperationException("The synthetic throw must run.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void ThrowDeep(int depth, Exception? inner)
    {
        if (depth == 0) throw new InvalidOperationException(new string('x', 20_000) + PrivateText, inner);
        ThrowDeep(depth - 1, inner);
        GC.KeepAlive(inner); // Prevent tail-call elimination in Release tests.
    }

    private sealed class EmptyAnalyzer : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;
        public string Status => "synthetic";
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt, CancellationToken token) =>
            Task.FromResult(CompleteSession(capturedAt));
        public FrameAnalysisResult CompleteSession(DateTimeOffset completedAt) => new([], [], 0, "synthetic", 0, 0, 0, 0, null);
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class FixedPrices : ILootPriceProvider
    {
        public LootPriceSnapshot GetCachedSnapshot(string region) => LootPriceCatalog.FixedSnapshot(region);
        public Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetCachedSnapshot(region));
        public void Dispose() { }
    }

    private sealed class NoHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Technical error recording must not send HTTP.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
