using System.Text.Json;
using BdoGrindTracker.App.Diagnostics;

namespace BdoGrindTracker.App.Tests;

public sealed class RollingDebugLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grindcrest-rolling-debug-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void DisabledLoggingCreatesNoFilesOrDirectories()
    {
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(false, 3);
        log.Write(null, "frame", new { Text = "disabled" });
        log.Cleanup();
        Assert.False(Directory.Exists(_directory));
        Assert.Null(log.LastError);
    }

    [Fact]
    public void CleanupAndDisposePreserveExistingLogsUntilRetentionWasConfigured()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "debug-20260901-0900.jsonl");
        const string content = "Previously recorded diagnostics";
        File.WriteAllText(path, content);

        var log = new RollingDebugLog(_directory, _time);
        log.Cleanup();
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Configure(false, 0));
        log.Cleanup();
        log.Dispose();

        Assert.Equal(content, File.ReadAllText(path));
        Assert.Null(log.LastError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupKeepsOnlyTheExactWindowIncludingItsBoundary(bool withinSession)
    {
        Guid? sessionId = withinSession ? Guid.NewGuid() : null;
        var directory = sessionId is { } id ? SessionDirectory(id) : _directory;
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(true, 3);
        log.Write(sessionId, "frame", new { Number = 1 });
        _time.Advance(TimeSpan.FromSeconds(20));
        log.Write(sessionId, "frame", new { Number = 2 });
        _time.Advance(TimeSpan.FromSeconds(10));
        log.Write(sessionId, "frame", new { Number = 3 });
        _time.Advance(TimeSpan.FromMinutes(1));
        log.Write(sessionId, "frame", new { Number = 4 });

        _time.Advance(TimeSpan.FromHours(3) - TimeSpan.FromSeconds(70));
        log.Cleanup();

        Assert.Equal(new[] { 2, 3, 4 }, Entries(directory).Select(entry => entry.GetProperty("data").GetProperty("number").GetInt32()));
        Assert.All(Entries(directory), entry => Assert.True(entry.GetProperty("timestampUtc").GetDateTimeOffset() >= _time.GetUtcNow().AddHours(-3)));
        Assert.Equal(2, Directory.GetFiles(directory, "*.jsonl").Length);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

        _time.Advance(TimeSpan.FromMinutes(2));
        log.Cleanup();
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Empty(Directory.GetDirectories(_directory));
        Assert.Null(log.LastError);
    }

    [Fact]
    public void TenShortSessionsHaveTenSeparateFoldersInsideTheSameRetentionWindow()
    {
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(true, 3);
        var sessionIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var sessionId in sessionIds)
        {
            log.Write(sessionId, "session-started", new { Session = sessionId });
            _time.Advance(TimeSpan.FromMinutes(10));
            log.Write(sessionId, "session-stopped", new { Session = sessionId });
            log.Cleanup();
        }

        Assert.Equal(10, Directory.GetDirectories(_directory).Length);
        Assert.Empty(Directory.GetFiles(_directory));
        foreach (var sessionId in sessionIds)
        {
            var entries = Entries(SessionDirectory(sessionId));
            Assert.Equal(new[] { "session-started", "session-stopped" }, entries.Select(entry => entry.GetProperty("kind").GetString()));
            Assert.All(entries, entry =>
            {
                Assert.Equal(sessionId, entry.GetProperty("sessionId").GetGuid());
                Assert.Equal(sessionId, entry.GetProperty("data").GetProperty("session").GetGuid());
            });
        }
        Assert.Null(log.LastError);
    }

    [Fact]
    public void RestartWithTheSameSessionIdAppendsToTheSameFolder()
    {
        var sessionId = Guid.NewGuid();
        using (var first = new RollingDebugLog(_directory, _time))
        {
            first.Configure(true, 3);
            first.Write(sessionId, "frame", new { Number = 1 });
        }
        using var restarted = new RollingDebugLog(_directory, _time);
        restarted.Configure(true, 3);
        restarted.Write(sessionId, "frame", new { Number = 2 });

        Assert.Equal(SessionDirectory(sessionId), Assert.Single(Directory.GetDirectories(_directory)));
        Assert.Single(Directory.GetFiles(SessionDirectory(sessionId)));
        Assert.Equal(new[] { 1, 2 }, Entries(SessionDirectory(sessionId))
            .Select(entry => entry.GetProperty("data").GetProperty("number").GetInt32()));
        Assert.Null(restarted.LastError);
    }

    [Fact]
    public void EntriesWithoutASessionRemainInTheRootDirectory()
    {
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(true, 3);
        log.Write(null, "settings", new { Enabled = true });

        Assert.Empty(Directory.GetDirectories(_directory));
        Assert.Equal(JsonValueKind.Null, Assert.Single(Entries()).GetProperty("sessionId").ValueKind);
        Assert.Null(log.LastError);
    }

    [Fact]
    public void EmptySessionIdIsIsolatedWithoutCreatingAnAmbiguousFolder()
    {
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(true, 3);
        log.Write(Guid.Empty, "frame", new { Number = 1 });

        Assert.NotNull(log.LastError);
        Assert.False(Directory.Exists(_directory));
        log.Write(Guid.NewGuid(), "frame", new { Number = 2 });
        Assert.Single(Directory.GetDirectories(_directory));
    }

    [Fact]
    public void RestartAndShorterRetentionCleanExistingLogsEvenWhenDisabled()
    {
        using (var first = new RollingDebugLog(_directory, _time))
        {
            first.Configure(true, 3);
            first.Write(null, "frame", new { Number = 1 });
            _time.Advance(TimeSpan.FromHours(2));
            first.Write(null, "frame", new { Number = 2 });
        }
        using var restarted = new RollingDebugLog(_directory, _time);
        restarted.Configure(false, 1);
        restarted.Write(null, "frame", new { Number = 3 });
        Assert.Equal(2, Assert.Single(Entries()).GetProperty("data").GetProperty("number").GetInt32());
        _time.Advance(TimeSpan.FromHours(2));
        restarted.Cleanup();
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void RestartAndDisabledCleanupRemoveExpiredSessionsAndRetainRecentSessions()
    {
        var expiredSessionId = Guid.NewGuid();
        var recentSessionId = Guid.NewGuid();
        using (var first = new RollingDebugLog(_directory, _time))
        {
            first.Configure(true, 3);
            first.Write(expiredSessionId, "frame", new { Number = 1 });
            _time.Advance(TimeSpan.FromHours(2));
            first.Write(recentSessionId, "frame", new { Number = 2 });
        }
        using var restarted = new RollingDebugLog(_directory, _time);
        restarted.Configure(false, 1);

        Assert.False(Directory.Exists(SessionDirectory(expiredSessionId)));
        Assert.Equal(SessionDirectory(recentSessionId), Assert.Single(Directory.GetDirectories(_directory)));
        Assert.Equal(2, Assert.Single(Entries(SessionDirectory(recentSessionId)))
            .GetProperty("data").GetProperty("number").GetInt32());
        _time.Advance(TimeSpan.FromHours(2));
        restarted.Cleanup();
        Assert.Empty(Directory.GetDirectories(_directory));
        Assert.Null(restarted.LastError);
    }

    [Fact]
    public void CleanupLeavesManualRecordingsAndUnrecognizedFilesUntouched()
    {
        Directory.CreateDirectory(_directory);
        var unrelated = new[] { "observations.jsonl", "rotation.jsonl", "debug-notes.jsonl", "debug-20260922-0900.jsonl.backup" };
        foreach (var name in unrelated) File.WriteAllText(Path.Combine(_directory, name), "keep");
        var nested = Path.Combine(_directory, "loot-manual");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "debug-20260922-0900.jsonl"), "keep");
        var malformedSession = Path.Combine(_directory, "session-" + Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(malformedSession);
        File.WriteAllText(Path.Combine(malformedSession, "debug-20260922-0900.jsonl"), "keep");
        var session = SessionDirectory(Guid.NewGuid());
        Directory.CreateDirectory(session);
        foreach (var name in unrelated) File.WriteAllText(Path.Combine(session, name), "keep");
        File.WriteAllText(Path.Combine(session, "debug-20260922-0900.jsonl"), "expired");
        File.WriteAllText(Path.Combine(session, "debug-20260922-0900.jsonl.tmp"), "incomplete compaction");
        var nestedInSession = Path.Combine(session, "manual");
        Directory.CreateDirectory(nestedInSession);
        File.WriteAllText(Path.Combine(nestedInSession, "debug-20260922-0900.jsonl"), "keep");
        File.WriteAllText(Path.Combine(_directory, "debug-20260922-0900.jsonl.tmp"), "incomplete compaction");
        using var log = new RollingDebugLog(_directory, _time);
        _time.Advance(TimeSpan.FromDays(10));
        log.Configure(false, 3);

        Assert.All(unrelated, name => Assert.Equal("keep", File.ReadAllText(Path.Combine(_directory, name))));
        Assert.All(unrelated, name => Assert.Equal("keep", File.ReadAllText(Path.Combine(session, name))));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(nested, "debug-20260922-0900.jsonl")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(malformedSession, "debug-20260922-0900.jsonl")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(nestedInSession, "debug-20260922-0900.jsonl")));
        Assert.False(File.Exists(Path.Combine(session, "debug-20260922-0900.jsonl")));
        Assert.Empty(Directory.GetFiles(session, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Null(log.LastError);
    }

    [Fact]
    public void InvalidAndOversizedBoundaryLinesDoNotBlockCleanupOfValidEntries()
    {
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(true, 1);
        log.Write(null, "frame", new { Number = 1 });
        var path = Assert.Single(Directory.GetFiles(_directory));
        File.AppendAllText(path, "{broken\nnull\n42\n[]\n" + new string('x', RollingDebugLog.MaximumLineBytes + 10) + "\n");
        _time.Advance(TimeSpan.FromSeconds(30));
        log.Write(null, "frame", new { Number = 2 });
        _time.Advance(TimeSpan.FromHours(1));
        log.Cleanup();

        Assert.Equal(2, Assert.Single(Entries()).GetProperty("data").GetProperty("number").GetInt32());
        Assert.Null(log.LastError);
    }

    [Fact]
    public void FailedWritesAreIsolatedAndLaterWritesCanRecover()
    {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "");
        using var log = new RollingDebugLog(blocked, _time);
        log.Configure(true, 3);
        log.Write(null, "frame", new { Number = 1 });
        Assert.NotNull(log.LastError);
        File.Delete(blocked);
        log.Write(null, "frame", new { Number = 2 });
        Assert.Single(Directory.GetFiles(blocked));
    }

    [Fact]
    public void ConcurrentCleanupAndWritesLeaveCompleteJsonLines()
    {
        using var log = new RollingDebugLog(_directory, _time);
        log.Configure(true, 3);
        Parallel.For(0, 100, number =>
        {
            log.Write(null, "frame", new { Number = number });
            log.Cleanup();
        });
        var entries = Entries();
        Assert.Equal(100, entries.Length);
        Assert.Equal(100, entries.Select(entry => entry.GetProperty("data").GetProperty("number").GetInt32()).Distinct().Count());
        Assert.Null(log.LastError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    public void RetentionOutsideTheSupportedRangeIsRejected(int hours)
    {
        using var log = new RollingDebugLog(_directory, _time);
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Configure(true, hours));
        Assert.False(Directory.Exists(_directory));
    }

    private string SessionDirectory(Guid sessionId) => Path.Combine(_directory, "session-" + sessionId.ToString("N"));

    private JsonElement[] Entries(string? directory = null) => Directory.GetFiles(directory ?? _directory, "*.jsonl").Order(StringComparer.Ordinal)
        .SelectMany(File.ReadAllLines).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
