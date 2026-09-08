using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Fills missing quantities only when this capture shows the same new messages
/// inserted into both the bottom-anchored private-item chat and the normal panel.
/// Chat never creates events or changes an existing panel quantity.
/// </summary>
internal sealed class PrivateItemChatQuantityRecovery
{
    internal const int MaximumChatLines = 128;
    internal const int MaximumPanelRows = 16;
    internal static readonly TimeSpan MaximumCaptureGap = TimeSpan.FromSeconds(5);
    private const int MinimumUpwardMovement = 8;
    private const int PositionTolerance = 3;
    private PrivateItemChatLine[]? _previousChat;
    private LootObservation[] _previousPanel = [];
    private DateTimeOffset? _lastCapturedAt;

    public int LastRecoveredCount { get; private set; }

    public IReadOnlyList<LootObservation> Recover(
        IReadOnlyList<LootObservation> normal,
        IReadOnlyList<PrivateItemChatLine> chat,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(chat);
        LastRecoveredCount = 0;
        if (chat.Count == 0 || chat.Count > MaximumChatLines ||
            chat.Any(line => line.Quantity <= 0 || line.Y < 0 ||
                string.IsNullOrWhiteSpace(line.ItemName) ||
                string.IsNullOrWhiteSpace(line.RawText) || line.RawText.Length > 512))
        {
            Reset();
            return normal;
        }
        if (_lastCapturedAt is { } previousTime &&
            (capturedAt <= previousTime || capturedAt - previousTime > MaximumCaptureGap))
            Reset();
        _lastCapturedAt = capturedAt;

        var currentChat = chat.OrderBy(line => line.Y).ToArray();
        var currentPanel = ContiguousPanelPrefix(normal);
        var previousChat = _previousChat;
        var previousPanel = _previousPanel;
        _previousChat = currentChat;
        _previousPanel = currentPanel;
        if (previousChat is null || previousPanel.Length == 0 ||
            currentPanel.All(row => row.Quantity.HasValue))
            return normal;

        var appendedChat = AppendedChatCount(previousChat, currentChat);
        if (appendedChat == 0) return normal;
        var appendedPanel = AppendedPanelCount(previousPanel, currentPanel);
        if (appendedPanel == 0 || appendedPanel != appendedChat ||
            !PreviousAnchorsAgree(previousPanel, previousChat,
                Math.Min(previousPanel.Length, currentPanel.Length - appendedPanel)))
            return normal;

        // Match the two proven new tails directly. Older rows are movement
        // anchors only; a missing older row must not borrow a newer drop's number.
        for (var index = 0; index < appendedPanel; index++)
        {
            var row = currentPanel[index];
            var message = currentChat[^(index + 1)];
            if (!string.Equals(row.ItemName, message.ItemName, StringComparison.Ordinal) ||
                row.Quantity is { } known && known != message.Quantity)
                return normal;
        }

        LootObservation[]? recovered = null;
        for (var index = 0; index < appendedPanel; index++)
        {
            var row = currentPanel[index];
            if (row.Quantity.HasValue) continue;
            recovered ??= normal.ToArray();
            // Keep original list order, source, geometry and all diagnostic data.
            var originalIndex = Array.FindIndex(recovered, candidate => ReferenceEquals(candidate, row));
            recovered[originalIndex] = row with { Quantity = currentChat[^(index + 1)].Quantity };
            LastRecoveredCount++;
        }
        return recovered is null ? normal : recovered;
    }

    public void Reset()
    {
        _previousChat = null;
        _previousPanel = [];
        _lastCapturedAt = null;
        LastRecoveredCount = 0;
    }

    private static LootObservation[] ContiguousPanelPrefix(IReadOnlyList<LootObservation> observations)
    {
        var normal = observations.Where(row => row.Source == LootSource.Normal)
            .OrderBy(row => row.Slot).ToArray();
        if (normal.Length > MaximumPanelRows) return [];
        var prefix = new List<LootObservation>(normal.Length);
        foreach (var row in normal)
        {
            // Retain rejected/missing slots as barriers instead of making an
            // older accepted item appear to be the newest visible drop.
            if (row.Slot != prefix.Count || row.RejectionReason is not null ||
                string.IsNullOrWhiteSpace(row.ItemName) || !row.NativeY.HasValue ||
                prefix.Count > 0 && row.NativeY.Value >= prefix[^1].NativeY!.Value)
                break;
            prefix.Add(row);
        }
        return prefix.ToArray();
    }

    private static int AppendedChatCount(PrivateItemChatLine[] previous, PrivateItemChatLine[] current)
    {
        if (previous.Length == current.Length &&
            previous.Zip(current).All(pair => SameLine(pair.First, pair.Second)))
            return 0;
        var overlap = 0;
        for (var length = 1; length <= Math.Min(previous.Length, current.Length); length++)
        {
            var matches = true;
            for (var index = 0; index < length; index++)
            {
                if (SameLine(previous[previous.Length - length + index], current[index])) continue;
                matches = false;
                break;
            }
            if (!matches) continue;
            // Identical consecutive messages retain distinct positions. Multiple
            // possible scroll distances provide no proof of how many are new.
            if (overlap != 0) return 0;
            overlap = length;
        }
        if (overlap == 0 || overlap == current.Length ||
            !HasAppendedChatMovement(previous, current, overlap))
            return 0;
        return current.Length - overlap;
    }

    private static bool HasAppendedChatMovement(
        PrivateItemChatLine[] previous, PrivateItemChatLine[] current, int overlap)
    {
        // A real tail pushes the old chat upward by the space the tail occupies.
        // OCR rediscovery of stationary text and downward user scrolling do not.
        var expectedMovement = current[^1].Y - current[overlap - 1].Y;
        if (expectedMovement < MinimumUpwardMovement ||
            Math.Abs((long)previous[^1].Y - current[^1].Y) > PositionTolerance)
            return false;
        for (var index = 0; index < overlap; index++)
        {
            var movement = (long)previous[previous.Length - overlap + index].Y - current[index].Y;
            if (movement < MinimumUpwardMovement || Math.Abs(movement - expectedMovement) > PositionTolerance)
                return false;
        }
        return true;
    }

    private static int AppendedPanelCount(LootObservation[] previous, LootObservation[] current)
    {
        if (current.Length < 2 || previous[0].NativeY != current[0].NativeY) return 0;
        var appended = 0;
        for (var count = 1; count < current.Length; count++)
        {
            var overlap = Math.Min(previous.Length, current.Length - count);
            var expectedMovement = current[0].NativeY!.Value - current[count].NativeY!.Value;
            var matches = expectedMovement > 0;
            for (var index = 0; matches && index < overlap; index++)
            {
                var oldRow = previous[index];
                var continuingRow = current[count + index];
                var movement = (long)oldRow.NativeY!.Value - continuingRow.NativeY!.Value;
                matches = oldRow.Quantity is > 0 && oldRow.Quantity == continuingRow.Quantity &&
                    string.Equals(oldRow.ItemName, continuingRow.ItemName, StringComparison.Ordinal) &&
                    continuingRow.Slot - oldRow.Slot == count &&
                    movement > 0 && Math.Abs(movement - expectedMovement) <= PositionTolerance;
            }
            if (!matches) continue;
            if (appended != 0) return 0;
            appended = count;
        }
        return appended;
    }

    private static bool PreviousAnchorsAgree(
        LootObservation[] panel, PrivateItemChatLine[] chat, int anchorCount)
    {
        // Independent upward movement is insufficient if one game surface is
        // still displaying an earlier drop. The known rows used as movement
        // anchors must already agree across the two previous images.
        if (chat.Length < anchorCount) return false;
        for (var index = 0; index < anchorCount; index++)
        {
            var message = chat[^(index + 1)];
            if (panel[index].Quantity != message.Quantity ||
                !string.Equals(panel[index].ItemName, message.ItemName, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool SameLine(PrivateItemChatLine left, PrivateItemChatLine right) =>
        left.Quantity == right.Quantity &&
        string.Equals(left.ItemName, right.ItemName, StringComparison.Ordinal) &&
        string.Equals(left.RawText, right.RawText, StringComparison.Ordinal);
}
