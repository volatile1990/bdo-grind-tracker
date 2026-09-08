using System.Runtime.InteropServices;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal sealed record ChatQuantityCorrection(int Slot, int NativeY, string ItemName, int Quantity);

internal sealed record ChatQuantityRecoveryDiagnostics(
    int? WindowIndex, string State, int LinesRead, int QuantitiesRecovered, int Errors)
{
    public IReadOnlyList<ChatQuantityCorrection> Corrections { get; init; } = [];
}

internal sealed record ChatQuantityRecoveryResult(
    IReadOnlyList<LootObservation> Observations,
    Rectangle? Region,
    ChatQuantityRecoveryDiagnostics Diagnostics);

internal interface IPrivateItemChatFallback
{
    ChatQuantityRecoveryResult Apply(Mat frame, IReadOnlyList<LootObservation> normal,
        DateTimeOffset capturedAt, CancellationToken cancellationToken);
    void Reset();
}

/// <summary>
/// An optional second view of the same captured frame. Chat never creates a loot
/// event: only missing quantities in already identified normal rows can be filled.
/// </summary>
internal sealed class PrivateItemChatFallback(
    CompanionItemMatcher matcher,
    Func<PrivateItemChatCalibration?> locate,
    Func<Mat, CancellationToken, IReadOnlyList<PrivateItemChatLine>> read) : IPrivateItemChatFallback
{
    private static readonly TimeSpan ConfigurationInterval = TimeSpan.FromSeconds(2);
    private readonly PrivateItemChatQuantityRecovery _recovery = new();
    private PrivateItemChatCalibration? _window;
    private DateTimeOffset _nextConfigurationRead;

    public ChatQuantityRecoveryResult Apply(Mat frame, IReadOnlyList<LootObservation> normal,
        DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (capturedAt >= _nextConfigurationRead || _nextConfigurationRead - capturedAt > ConfigurationInterval)
            {
                var window = locate();
                if (window != _window) _recovery.Reset();
                _window = window;
                _nextConfigurationRead = capturedAt + ConfigurationInterval;
            }

            if (_window is null)
                return Unchanged("no-private-item-window");
            var bounds = _window.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Left < 0 || bounds.Top < 0 ||
                (long)bounds.Left + bounds.Width > frame.Width || (long)bounds.Top + bounds.Height > frame.Height ||
                bounds.Width * (long)bounds.Height > 8L * 1024 * 1024)
            {
                _recovery.Reset();
                return Unchanged("invalid-chat-region");
            }

            using var crop = new Mat(frame, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            // Continue observing history even when the panel currently has no missing
            // quantity. Otherwise the first needed retry could mistake history for loot.
            var lines = read(crop, cancellationToken).Select(line =>
                matcher.TryMatch(line.ItemName, line.Quantity, false, out var match) && match is not null
                    ? line with { ItemName = match.CanonicalName }
                    : line).ToArray();
            var recovered = _recovery.Recover(normal, lines, capturedAt);
            var corrections = normal.Zip(recovered).Where(pair =>
                    pair.First.Quantity is null && pair.Second.Quantity is > 0)
                .Select(pair => new ChatQuantityCorrection(pair.Second.Slot,
                    pair.Second.NativeY ?? 0, pair.Second.ItemName!, pair.Second.Quantity!.Value)).ToArray();
            return new(recovered, bounds, new(_window.WindowIndex,
                lines.Length == 0 ? "no-readable-item-lines" : "reading-private-items",
                lines.Length, corrections.Length, 0) { Corrections = corrections });
        }
        catch (Exception exception) when (exception is OpenCVException or COMException or
            IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or
            NotSupportedException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A missed read invalidates continuity, not the ordinary loot analysis.
            _recovery.Reset();
            return new(normal, null, new(_window?.WindowIndex, "chat-read-failed", 0, 0, 1));
        }

        ChatQuantityRecoveryResult Unchanged(string state) =>
            new(normal, null, new(_window?.WindowIndex, state, 0, 0, 0));
    }

    public void Reset()
    {
        _recovery.Reset();
        _window = null;
        _nextConfigurationRead = default;
    }
}
