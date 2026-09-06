// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BdoGrindTracker.Core;

public sealed class CompanionRareFrameReconciler
{
    private enum GearCategory : byte
    {
        Belt = 0,
        Earring = 1,
        Necklace = 2,
        Ring = 3,
        None = byte.MaxValue
    }

    private sealed class Frame(List<Entry> entries)
    {
        public List<Entry> Entries { get; } = entries;
    }

    private sealed class Entry(string name, int count, int y, bool suppressCounting, long frame, bool duplicate)
    {
        public string Name { get; } = name;

        public int Count { get; set; } = count;

        public int Y { get; } = y;

        public bool SuppressCounting { get; } = suppressCounting;

        public long Frame { get; set; } = frame;

        public bool Duplicate { get; set; } = duplicate;

        public Entry Copy()
        {
            return new Entry(Name, Count, Y, SuppressCounting, Frame, Duplicate);
        }
    }

    public const int BatchSize = 10;

    public const ulong RecentFrameWindow = 12uL;

    private const int MissingCount = -1;

    private const int InitialLastY = 300;

    private const int MissingRowStep = 50;

    private static readonly HashSet<string> UnitCountItems = new HashSet<string>(new string[2] { "Dawn Crystal", "Fortunate Golden Pig King" }, StringComparer.Ordinal);

    private readonly Dictionary<string, string> iconPaths;

    private readonly Dictionary<string, ulong> lastSeen = new Dictionary<string, ulong>(StringComparer.Ordinal);

    private readonly Dictionary<string, uint> support = new Dictionary<string, uint>(StringComparer.Ordinal);

    private readonly CompanionLootLedger ledger;

    private readonly bool ownsLedger;

    private readonly List<Frame> frames = new List<Frame>();

    private int processedFrameCount;

    private ulong frameIndex;

    public ulong FrameIndex => frameIndex;

    public IReadOnlyDictionary<string, ulong> LastSeen => lastSeen;

    public IReadOnlyDictionary<string, uint> Support => support;

    public CompanionRareFrameReconciler(IEnumerable<CompanionRareCatalogEntry>? catalog = null, CompanionLootLedger? sharedLedger = null)
    {
        ledger = sharedLedger ?? new CompanionLootLedger();
        ownsLedger = sharedLedger == null;
        iconPaths = (catalog ?? Array.Empty<CompanionRareCatalogEntry>()).Where((CompanionRareCatalogEntry item) => !string.IsNullOrWhiteSpace(item.Name) && item.IconPath != null).GroupBy<CompanionRareCatalogEntry, string>((CompanionRareCatalogEntry item) => item.Name, StringComparer.Ordinal).ToDictionary<IGrouping<string, CompanionRareCatalogEntry>, string, string>((IGrouping<string, CompanionRareCatalogEntry> group) => group.Key, (IGrouping<string, CompanionRareCatalogEntry> group) => group.First().IconPath!, StringComparer.Ordinal);
    }

    public IReadOnlyList<CompanionRareCountDelta> ProcessFrame(IReadOnlyList<CompanionRareRecognizedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries, "entries");
        if (entries.Any((CompanionRareRecognizedEntry entry) => (object)entry == null))
        {
            throw new ArgumentException("A frame cannot contain a null entry.", "entries");
        }
        frames.Add(new Frame(entries.Select((CompanionRareRecognizedEntry entry) => new Entry(entry.Name, entry.Count, entry.Y, entry.SuppressCounting, 1L, duplicate: false)).ToList()));
        if (frames.Count - processedFrameCount >= 10)
        {
            return Flush();
        }
        return Array.Empty<CompanionRareCountDelta>();
    }

    public IReadOnlyList<CompanionRareCountDelta> Complete()
    {
        return Flush();
    }

    public void Reset()
    {
        ResetState();
        if (ownsLedger)
        {
            ledger.Reset();
        }
    }

    internal void ResetState()
    {
        frameIndex = 0uL;
        processedFrameCount = 0;
        frames.Clear();
        lastSeen.Clear();
        support.Clear();
    }

    private IReadOnlyList<CompanionRareCountDelta> Flush()
    {
        Repair(processedFrameCount);
        List<CompanionRareCountDelta> list = new List<CompanionRareCountDelta>();
        for (int i = processedFrameCount; i < frames.Count; i++)
        {
            ProcessStateFrame(frames[i], list);
        }
        processedFrameCount = frames.Count;
        return list;
    }

    private void ProcessStateFrame(Frame frame, ICollection<CompanionRareCountDelta> changes)
    {
        frameIndex++;
        foreach (Entry entry in frame.Entries.OrderBy((Entry entry2) => entry2.Y))
        {
            if (entry.Count == -1)
            {
                continue;
            }
            ulong value;
            bool num = lastSeen.TryGetValue(entry.Name, out value);
            ulong num2 = (num ? (frameIndex - value) : ulong.MaxValue);
            if (!num || num2 > 12)
            {
                support[entry.Name] = 0u;
            }
            support.TryGetValue(entry.Name, out var value2);
            value2++;
            support[entry.Name] = value2;
            bool flag = !num || num2 > 12;
            lastSeen[entry.Name] = frameIndex;
            if (entry.SuppressCounting || entry.Duplicate)
            {
                continue;
            }
            List<string> list = FindRecentSuffixAliases(entry.Name);
            if (list.Any((string alias) => alias.Length > entry.Name.Length))
            {
                continue;
            }
            List<string> list2 = FindRecentConflicts(entry.Name);
            if (HasEqualOrStrongerSupport(list2, value2))
            {
                continue;
            }
            bool flag2 = false;
            foreach (string item in list2)
            {
                flag2 |= RemoveOne(item, changes);
            }
            if (!flag && !flag2)
            {
                continue;
            }
            foreach (string item2 in list)
            {
                RemoveOne(item2, changes);
            }
            Add(entry.Name, entry.Count, changes);
        }
    }

    private List<string> FindRecentSuffixAliases(string currentName)
    {
        List<string> list = new List<string>();
        foreach (KeyValuePair<string, ulong> item in lastSeen)
        {
            string key = item.Key;
            if (string.Equals(key, currentName, StringComparison.Ordinal) || frameIndex - item.Value > 12)
            {
                continue;
            }
            string text = ((currentName.Length < key.Length) ? currentName : key);
            string text2 = ((currentName.Length < key.Length) ? key : currentName);
            if (text.Length != text2.Length && text2.EndsWith(text, StringComparison.Ordinal))
            {
                int num = text2.Length - text.Length - 1;
                if (num >= 0 && text2[num] == ' ')
                {
                    list.Add(key);
                }
            }
        }
        return list;
    }

    private List<string> FindRecentConflicts(string currentName)
    {
        List<string> list = new List<string>();
        string b = FirstWord(currentName);
        GearCategory gearCategory = ClassifyGear(currentName);
        foreach (KeyValuePair<string, ulong> item in lastSeen)
        {
            string key = item.Key;
            if (!string.Equals(key, currentName, StringComparison.Ordinal) && frameIndex - item.Value <= 12 && (string.Equals(FirstWord(key), b, StringComparison.Ordinal) || (gearCategory != GearCategory.None && ClassifyGear(key) == gearCategory)))
            {
                list.Add(key);
            }
        }
        return list;
    }

    private bool HasEqualOrStrongerSupport(IReadOnlyList<string> names, uint currentSupport)
    {
        foreach (string name in names)
        {
            if (support.TryGetValue(name, out var value) && value >= currentSupport)
            {
                return true;
            }
        }
        return false;
    }

    private bool RemoveOne(string name, ICollection<CompanionRareCountDelta> changes)
    {
        if (!ledger.TryRemoveOne(name))
        {
            return false;
        }
        changes.Add(new CompanionRareCountDelta(name, -1));
        return true;
    }

    private void Add(string name, int count, ICollection<CompanionRareCountDelta> changes)
    {
        ledger.Add(name, count);
        changes.Add(new CompanionRareCountDelta(name, count));
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
                if (entry.Count == -1)
                {
                    if (UnitCountItems.Contains(entry.Name))
                    {
                        entry.Count = 1;
                        continue;
                    }
                    if (TryCopyNeighborCount(i - 1, entries.Count, j, entry.Name, out var count) || TryCopyNeighborCount(i + 1, entries.Count, j, entry.Name, out count))
                    {
                        entry.Count = ((count == -1) ? 1 : count);
                        continue;
                    }
                    entries.RemoveAt(j);
                    j--;
                }
            }
        }
    }

    private bool TryCopyNeighborCount(int frameIndex, int expectedEntryCount, int entryIndex, string name, out int count)
    {
        count = -1;
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

    private GearCategory ClassifyGear(string name)
    {
        if (iconPaths.TryGetValue(name, out string? value))
        {
            string[] array = value.Split((ReadOnlySpan<char>)new char[2] { '/', '\\' });
            for (int i = 0; i < array.Length; i++)
            {
                GearCategory gearCategory = array[i] switch
                {
                    "18_belt" => GearCategory.Belt,
                    "17_earring" => GearCategory.Earring,
                    "15_necklace" => GearCategory.Necklace,
                    "16_ring" => GearCategory.Ring,
                    _ => GearCategory.None,
                };
                if (gearCategory != GearCategory.None)
                {
                    return gearCategory;
                }
            }
        }
        switch (LastWord(name))
        {
            case "Belt":
                return GearCategory.Belt;
            case "Earring":
            case "Earrings":
                return GearCategory.Earring;
            case "Necklace":
                return GearCategory.Necklace;
            case "Ring":
                return GearCategory.Ring;
            default:
                return GearCategory.None;
        }
    }

    private static string FirstWord(string value)
    {
        int num = value.IndexOf(' ');
        if (num >= 0)
        {
            return value.Substring(0, num);
        }
        return value;
    }

    private static string LastWord(string value)
    {
        int num = value.LastIndexOf(' ');
        if (num >= 0)
        {
            int num2 = num + 1;
            return value.Substring(num2, value.Length - num2);
        }
        return value;
    }
}

