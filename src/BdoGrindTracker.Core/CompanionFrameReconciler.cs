// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;
using System.Collections.Generic;

namespace BdoGrindTracker.Core;

public sealed class CompanionFrameReconciler
{
    private sealed class Frame(List<Entry> entries)
    {
        public List<Entry> Entries { get; } = entries;
    }

    private sealed class Entry(string name, uint count, int y, long frame, bool duplicate)
    {
        public string Name { get; } = name;

        public uint Count { get; set; } = count;

        public int Y { get; } = y;

        public long Frame { get; set; } = frame;

        public bool Duplicate { get; set; } = duplicate;

        public Entry Copy()
        {
            return new Entry(Name, Count, Y, Frame, Duplicate);
        }
    }

    public const int BatchSize = 10;

    private const uint InvalidCount = uint.MaxValue;

    private const int InitialLastY = 300;

    private const int MissingRowStep = 50;

    private static readonly HashSet<string> UnitCountItems = new HashSet<string>(new string[2] { "Dawn Crystal", "Fortunate Golden Pig King" }, StringComparer.Ordinal);

    private readonly List<Frame> frames = new List<Frame>();

    private int processedFrameCount;

    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries, "entries");
        List<Entry> list = new List<Entry>(entries.Count);
        foreach (CompanionRecognizedEntry entry in entries)
        {
            if ((object)entry == null)
            {
                throw new ArgumentException("A frame cannot contain a null entry.", "entries");
            }
            list.Add(new Entry(entry.Name, entry.Count, entry.Y, 1L, duplicate: false));
        }
        frames.Add(new Frame(list));
        if (frames.Count - processedFrameCount >= 10)
        {
            return Flush();
        }
        return Array.Empty<CompanionRecognizedEntry>();
    }

    public IReadOnlyList<CompanionRecognizedEntry> Complete()
    {
        return Flush();
    }

    public void Reset()
    {
        frames.Clear();
        processedFrameCount = 0;
    }

    private IReadOnlyList<CompanionRecognizedEntry> Flush()
    {
        Repair(processedFrameCount);
        List<CompanionRecognizedEntry> list = new List<CompanionRecognizedEntry>();
        for (int i = processedFrameCount; i < frames.Count; i++)
        {
            foreach (Entry entry in frames[i].Entries)
            {
                if (!entry.Duplicate && entry.Count != uint.MaxValue)
                {
                    list.Add(new CompanionRecognizedEntry(entry.Name, entry.Count, entry.Y));
                }
            }
        }
        processedFrameCount = frames.Count;
        return list;
    }

    private void Repair(int processedStart)
    {
        if (frames.Count == 0)
        {
            return;
        }
        int num = ((processedStart > 0) ? (processedStart - 1) : 0);
        RepairInvalidCounts(num);
        for (int i = num; i < frames.Count - 1; i++)
        {
            Frame left = frames[i];
            Frame right = frames[i + 1];
            int overlap = FindOverlap(left, right, requireFrameProgression: false);
            AssignNextFrames(left, right, overlap);
        }
        int num2 = ((processedStart <= 0) ? 1 : processedStart);
        for (int num3 = frames.Count - 1; num3 >= num2; num3--)
        {
            Frame left2 = frames[num3 - 1];
            Frame frame = frames[num3];
            int num4 = FindOverlap(left2, frame, requireFrameProgression: true);
            for (int j = frame.Entries.Count - num4; j < frame.Entries.Count; j++)
            {
                frame.Entries[j].Duplicate = true;
            }
        }
    }

    private void RepairInvalidCounts(int repairStart)
    {
        for (int i = repairStart; i < frames.Count - 1; i++)
        {
            List<Entry> entries = frames[i].Entries;
            for (int j = 0; j < entries.Count; j++)
            {
                Entry entry = entries[j];
                if (entry.Count == uint.MaxValue)
                {
                    if (UnitCountItems.Contains(entry.Name))
                    {
                        entry.Count = 1u;
                        continue;
                    }
                    if (TryCopyNeighborCount(i - 1, entries.Count, j, entry.Name, out var count) || TryCopyNeighborCount(i + 1, entries.Count, j, entry.Name, out count))
                    {
                        entry.Count = ((count == uint.MaxValue) ? 1u : count);
                        continue;
                    }
                    entries.RemoveAt(j);
                    j--;
                }
            }
        }
    }

    private bool TryCopyNeighborCount(int frameIndex, int expectedEntryCount, int entryIndex, string name, out uint count)
    {
        count = uint.MaxValue;
        if (frameIndex < 0 || frameIndex >= frames.Count)
        {
            return false;
        }
        List<Entry> entries = frames[frameIndex].Entries;
        if (entries.Count != expectedEntryCount || !string.Equals(entries[entryIndex].Name, name, StringComparison.Ordinal))
        {
            return false;
        }
        count = entries[entryIndex].Count;
        return true;
    }

    private static void AssignNextFrames(Frame left, Frame right, int overlap)
    {
        int num = right.Entries.Count - overlap;
        for (int i = 0; i < overlap; i++)
        {
            Entry entry = left.Entries[i];
            Entry entry2 = right.Entries[num + i];
            entry2.Frame = ((entry.Frame > 2) ? 1 : (entry.Frame + 1));
            for (int j = 0; j < num + i; j++)
            {
                Entry entry3 = right.Entries[j];
                if (SameIdentity(entry3, entry2) && entry2.Frame < entry3.Frame)
                {
                    Entry entry4 = entry2;
                    long frame = entry2.Frame;
                    long frame2 = entry3.Frame;
                    entry3.Frame = frame;
                    entry4.Frame = frame2;
                }
            }
        }
    }

    private static int FindOverlap(Frame left, Frame right, bool requireFrameProgression)
    {
        if (TryInsertMissingEntry(left, right))
        {
            return right.Entries.Count;
        }
        for (int num = Math.Min(left.Entries.Count, right.Entries.Count); num > 0; num--)
        {
            int num2 = right.Entries.Count - num;
            long num3 = 0L;
            bool flag = false;
            bool flag2 = true;
            for (int i = 0; i < num; i++)
            {
                Entry entry = left.Entries[i];
                Entry entry2 = right.Entries[num2 + i];
                if (!SameIdentity(entry, entry2))
                {
                    flag2 = false;
                    break;
                }
                if (requireFrameProgression)
                {
                    bool flag3 = entry2.Frame < num3;
                    if (entry.Frame != entry2.Frame - 1 && !flag && !flag3)
                    {
                        flag2 = false;
                        break;
                    }
                    flag = flag || flag3;
                    num3 = entry2.Frame;
                }
            }
            if (flag2)
            {
                return num;
            }
        }
        return 0;
    }

    private static bool TryInsertMissingEntry(Frame left, Frame right)
    {
        if (right.Entries.Count != left.Entries.Count + 1 || MinimumY(left.Entries) != MinimumY(right.Entries))
        {
            return false;
        }
        int num = 300;
        int num2 = 0;
        Entry? entry = null;
        int index = -1;
        for (int i = 0; i < left.Entries.Count; i++)
        {
            if (num2 >= right.Entries.Count)
            {
                return false;
            }
            Entry entry2 = left.Entries[i];
            Entry entry3 = right.Entries[num2];
            if (entry2.Y + 50 == num)
            {
                if (!SameIdentity(entry2, entry3))
                {
                    return false;
                }
                num = entry2.Y;
                num2++;
                continue;
            }
            if (num2 + 1 >= right.Entries.Count || !SameIdentity(entry2, right.Entries[num2 + 1]))
            {
                return false;
            }
            entry = entry3.Copy();
            index = i;
            num = entry3.Y - 50;
            num2 += 2;
        }
        if (entry == null)
        {
            return false;
        }
        left.Entries.Insert(index, entry);
        return true;
    }

    private static int MinimumY(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return 0;
        }
        int num = entries[0].Y;
        for (int i = 1; i < entries.Count; i++)
        {
            num = Math.Min(num, entries[i].Y);
        }
        return num;
    }

    private static bool SameIdentity(Entry left, Entry right)
    {
        if (left.Count == right.Count)
        {
            return string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }
        return false;
    }
}

