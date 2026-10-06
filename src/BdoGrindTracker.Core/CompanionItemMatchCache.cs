namespace BdoGrindTracker.Core;

/// <summary>Memoizes pure matches against one immutable catalog, including rejected names.</summary>
internal sealed class CompanionItemMatchCache
{
    internal const int MaximumEntries = 512;
    internal const long MaximumBytes = 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private long _retainedBytes;

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal long RetainedBytes { get { lock (_gate) return _retainedBytes; } }

    internal bool TryGet(string observedText, int quantity, bool rareDropMode, out CompanionItemMatch? match)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(new(observedText, quantity, rareDropMode), out var node))
            {
                match = null;
                return false;
            }

            _recent.Remove(node);
            _recent.AddFirst(node);
            match = node.Value.Match;
            return true;
        }
    }

    internal void Remember(string observedText, int quantity, bool rareDropMode, CompanionItemMatch? match)
    {
        // Count bounds container overhead; the byte budget also bounds unusually long OCR text.
        var retainedBytes = 192L + 2L * observedText.Length +
            (match is null ? 0 : 64L + 2L * (match.ObservedText.Length + match.CanonicalName.Length));
        if (retainedBytes > MaximumBytes) return;
        var key = new Key(observedText, quantity, rareDropMode);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _recent.Remove(existing);
                _recent.AddFirst(existing);
                return;
            }

            while (_entries.Count >= MaximumEntries || _retainedBytes + retainedBytes > MaximumBytes)
            {
                var oldest = _recent.Last!;
                _entries.Remove(oldest.Value.Key);
                _retainedBytes -= oldest.Value.RetainedBytes;
                _recent.RemoveLast();
            }

            var node = _recent.AddFirst(new Entry(key, match, retainedBytes));
            _entries.Add(key, node);
            _retainedBytes += retainedBytes;
        }
    }

    private readonly record struct Key(string ObservedText, int Quantity, bool RareDropMode);
    private sealed record Entry(Key Key, CompanionItemMatch? Match, long RetainedBytes);
}
