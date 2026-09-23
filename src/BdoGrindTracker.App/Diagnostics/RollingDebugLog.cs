using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// Optional text diagnostics grouped by session with a shared wall-clock retention window. These logs are separate from
/// complete, replayable diagnostic recordings. Call Cleanup regularly, including while disabled.
/// </summary>
internal sealed class RollingDebugLog : IDisposable
{
    internal const int MaximumLineBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private bool _enabled;
    private bool _configured;
    private bool _disposed;
    private int _retentionHours = 3;
    private string? _lastError;

    public RollingDebugLog(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string? LastError { get { lock (_sync) return _lastError; } }

    public void Configure(bool enabled, int retentionHours)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionHours, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retentionHours, 168);
        lock (_sync)
        {
            if (_disposed) return;
            _enabled = enabled;
            _retentionHours = retentionHours;
            _configured = true;
            CleanupCore();
        }
    }

    public void Write<T>(Guid? sessionId, string kind, T data)
    {
        lock (_sync)
        {
            if (!_enabled || _disposed) return;
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(kind);
                if (sessionId == Guid.Empty)
                    throw new ArgumentException("Eine Debug-Session benötigt eine gültige ID.", nameof(sessionId));
                var timestamp = _timeProvider.GetUtcNow().ToUniversalTime();
                var bytes = JsonSerializer.SerializeToUtf8Bytes(
                    new { Version = 1, TimestampUtc = timestamp, SessionId = sessionId, Kind = kind, Data = data }, JsonOptions);
                if (bytes.Length > MaximumLineBytes)
                    throw new InvalidDataException("Debug-Eintrag überschreitet die maximale Größe von 512 KiB.");
                Directory.CreateDirectory(_directory);
                RejectDirectoryLink(_directory);
                var directory = sessionId is { } id ? Path.Combine(_directory, "session-" + id.ToString("N")) : _directory;
                Directory.CreateDirectory(directory);
                RejectDirectoryLink(directory);
                var fileName = "debug-" + timestamp.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".jsonl";
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Debuglogs können nicht in eine Dateiverknüpfung geschrieben werden.");
                using var file = new FileStream(path, FileMode.Append,
                    FileAccess.Write, FileShare.Read);
                file.Write(bytes);
                file.WriteByte((byte)'\n');
            }
            catch (Exception exception) when (IsLogException(exception)) { _lastError = exception.Message; }
        }
    }

    public void Cleanup()
    {
        lock (_sync)
        {
            if (!_disposed) CleanupCore();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            CleanupCore();
            _disposed = true;
        }
    }

    private void CleanupCore()
    {
        // Unread settings must never let fallback retention values remove existing logs.
        if (!_configured) return;
        try
        {
            if (!Directory.Exists(_directory)) return;
            if ((File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0) return;
            var cutoff = _timeProvider.GetUtcNow().ToUniversalTime().AddHours(-_retentionHours);
            CleanupDirectory(_directory, cutoff);
            foreach (var directory in Directory.EnumerateDirectories(_directory, "session-*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (name.Length != 40 || !name.StartsWith("session-", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(name.AsSpan(8), "N", out _)) continue;
                try
                {
                    if (!Directory.Exists(directory)) continue;
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    CleanupDirectory(directory, cutoff);
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, recursive: false);
                }
                catch (Exception exception) when (IsLogException(exception)) { _lastError = exception.Message; }
            }
        }
        catch (Exception exception) when (IsLogException(exception)) { _lastError = exception.Message; }
    }

    private void CleanupDirectory(string directory, DateTimeOffset cutoff)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "debug-*.jsonl*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            var temporary = fileName.EndsWith(".tmp", StringComparison.Ordinal);
            if (!TrySegmentTime(temporary ? fileName[..^4] : fileName, out var segment)) continue;
            try
            {
                // A leftover compaction file belongs to this logger, too. Never follow links.
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                if (temporary || segment.AddMinutes(1) <= cutoff) File.Delete(path);
                else if (segment < cutoff) TrimBoundary(path, cutoff);
            }
            catch (Exception exception) when (IsLogException(exception)) { _lastError = exception.Message; }
        }
    }

    private static void RejectDirectoryLink(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Debuglogs können nicht in eine Verzeichnisverknüpfung geschrieben werden.");
    }

    private static bool TrySegmentTime(string fileName, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return fileName.Length == 25 && fileName.StartsWith("debug-", StringComparison.Ordinal) &&
            fileName.EndsWith(".jsonl", StringComparison.Ordinal) &&
            DateTimeOffset.TryParseExact(fileName.AsSpan(6, 13), "yyyyMMdd-HHmm", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    private static void TrimBoundary(string path, DateTimeOffset cutoff)
    {
        var temporary = path + ".tmp";
        try
        {
            // Delete only the known temporary filename; CreateNew never follows a stale link.
            if (File.Exists(temporary)) File.Delete(temporary);
            var retained = false;
            using (var input = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), Utf8))
            using (var output = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None), Utf8))
            {
                foreach (var line in ReadBoundedLines(input))
                {
                    try
                    {
                        using var entry = JsonDocument.Parse(line);
                        if (entry.RootElement.ValueKind != JsonValueKind.Object ||
                            !entry.RootElement.TryGetProperty("timestampUtc", out var property) ||
                            property.ValueKind != JsonValueKind.String ||
                            !property.TryGetDateTimeOffset(out var timestamp) || timestamp < cutoff) continue;
                        output.WriteLine(line);
                        retained = true;
                    }
                    catch (JsonException) { } // Discard incomplete writes left by a terminated process.
                }
            }
            if (retained) File.Move(temporary, path, overwrite: true);
            else File.Delete(path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static IEnumerable<string> ReadBoundedLines(StreamReader reader)
    {
        var line = new StringBuilder();
        var oversized = false;
        while (reader.Read() is var character && character >= 0)
        {
            if (character == '\n')
            {
                if (!oversized && line.Length > 0) yield return line.ToString();
                line.Clear();
                oversized = false;
            }
            else if (!oversized)
            {
                if (line.Length >= MaximumLineBytes) oversized = true;
                else line.Append((char)character);
            }
        }
        if (!oversized && line.Length > 0) yield return line.ToString();
    }

    private static bool IsLogException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            JsonException or InvalidOperationException;
}
