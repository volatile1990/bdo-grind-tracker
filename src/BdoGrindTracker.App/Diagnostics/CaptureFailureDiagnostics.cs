using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// Retains only the last technical capture failure. Messages, argument values,
/// source paths, OCR text and images are deliberately never read or serialized.
/// </summary>
internal static class CaptureFailureDiagnostics
{
    internal const string FileName = "last-capture-error.json";
    internal const int MaximumBytes = 8 * 1024;
    private const int MaximumExceptions = 3;
    private const int MaximumStackFrames = 24;
    private static readonly object WriteLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static void TryWrite(string baseDirectory, Exception? error, DateTimeOffset occurredAt)
    {
        if (error is null) return;
        try
        {
            var exceptions = new List<FailureException>();
            var remainingFrames = MaximumStackFrames;
            for (var current = error; current is not null && exceptions.Count < MaximumExceptions;
                 current = current.InnerException)
            {
                var methods = new List<string>();
                var trace = new StackTrace(current, fNeedFileInfo: false);
                for (var index = 0; index < trace.FrameCount && remainingFrames > 0; index++)
                {
                    var method = trace.GetFrame(index)?.GetMethod();
                    if (method is null) continue;
                    methods.Add(Limit($"{method.DeclaringType?.FullName}.{method.Name}", 192));
                    remainingFrames--;
                }
                exceptions.Add(new(Limit(current.GetType().FullName ?? current.GetType().Name, 256),
                    current.HResult, SafeParameterName((current as ArgumentException)?.ParamName), methods));
            }

            var report = new FailureReport(1, occurredAt.ToUniversalTime(),
                Limit(typeof(CaptureFailureDiagnostics).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown", 96),
                LootDiagnosticFormat.EngineVersion, exceptions);
            var payload = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
            while (payload.Length > MaximumBytes)
            {
                var lastWithFrames = exceptions.LastOrDefault(exception => exception.Stack.Count > 0);
                if (lastWithFrames is null) return;
                lastWithFrames.Stack.RemoveAt(lastWithFrames.Stack.Count - 1);
                payload = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
            }

            lock (WriteLock)
            {
                var directory = Path.GetFullPath(baseDirectory);
                Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, FileName);
                var temporary = destination + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, payload);
                    File.Move(temporary, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
        catch
        {
            // Logging is optional, including during shutdown or a full disk.
            // It must never replace the original error or fault the capture task.
        }
    }

    private static string? SafeParameterName(string? name) =>
        name is { Length: > 0 and <= 64 } &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? name : null;

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record FailureReport(int SchemaVersion, DateTimeOffset OccurredAtUtc,
        string AppVersion, string EngineVersion, IReadOnlyList<FailureException> Exceptions);

    private sealed record FailureException(string Type, int HResult, string? ParamName, List<string> Stack);
}
