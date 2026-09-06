// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BdoGrindTracker.Core;

public sealed class CompanionItemMatcher
{
    private sealed record Entry(string Name, byte[] Bytes, string? IconPath, Gear GearCategory);

    private readonly record struct ScoredEntry(Entry Entry, float Score);

    private enum Gear : byte
    {
        Belt = 0,
        Earring = 1,
        Necklace = 2,
        Ring = 3,
        None = byte.MaxValue
    }

    public const float MaximumNormalizedDistance = 0.34f;

    public const float MinimumRelatedCandidateGap = 0.1f;

    public const float BonCandidatePenalty = 0.01f;

    private static readonly HashSet<string> CountOneTrashNames = new HashSet<string>(new string[23]
    {
        "Decayed Cloth", "Outlaw's Mark", "Lightstone Core", "Fiery Troll Hide", "Faded Bandit Mask", "Discarded Kkebicap", "Chilled Soul Piece", "Tainted Token Piece", "Howling Bone Fragment", "Tainted Ruins Fragment",
        "Tungrad Ruins Fragment", "Tainted Sulfur Fragment", "Tainted Specter's Cloth", "Contaminated Coral Piece", "Tainted Token of Crescent", "Sea Monster's Spirit Pouch", "Bronze Fragment of Delusion", "Tainted Golem's Heart Fragment", "Thorn-Entwined Weapon Fragment", "Tainted Moonlight Spirit Powder",
        "Destroyed Ancient Weapon Power Stone", "Ferocious Sea Monster's Spirit Pouch", "Underwater Ancient Weapon Power Stone"
    }, StringComparer.Ordinal);

    private readonly Entry[] entries;

    private readonly Dictionary<string, Entry> exactEntries;

    private readonly CompanionRareCatalogEntry[] catalogEntries;

    private bool MetadataTablePresent { get; }

    public IReadOnlyList<CompanionRareCatalogEntry> CatalogEntries => catalogEntries;

    public CompanionItemMatcher(IEnumerable<string> canonicalItemNames)
        : this(CreateEntries(canonicalItemNames), metadataTablePresent: false)
    {
    }

    public CompanionItemMatcher(IEnumerable<CompanionRareCatalogEntry> catalogEntries)
        : this(CreateEntries(catalogEntries), metadataTablePresent: true)
    {
    }

    private CompanionItemMatcher(IEnumerable<Entry> sourceEntries, bool metadataTablePresent)
    {
        entries = (from entry in sourceEntries.DistinctBy<Entry, string>((Entry entry) => entry.Name, StringComparer.Ordinal)
                   select entry with
                   {
                       GearCategory = ClassifyGear(entry.Name, entry.IconPath)
                   }).ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("The item catalog must contain at least one name.");
        }
        MetadataTablePresent = metadataTablePresent;
        catalogEntries = entries.Select((Entry entry) => new CompanionRareCatalogEntry(entry.Name, entry.IconPath)).ToArray();
        exactEntries = entries.ToDictionary<Entry, string>((Entry entry) => entry.Name, StringComparer.Ordinal);
    }

    public bool TryMatch(string observedText, int quantity, bool rareDropMode, out CompanionItemMatch? match)
    {
        ArgumentNullException.ThrowIfNull(observedText, "observedText");
        if (IsFilteredCountOneTrash(observedText, quantity))
        {
            match = null;
            return false;
        }
        if (exactEntries.TryGetValue(observedText, out Entry? value))
        {
            match = new CompanionItemMatch(observedText, value.Name, 0.0, IsExact: true);
            return true;
        }
        byte[] observedBytes = Encoding.UTF8.GetBytes(observedText);
        ScoredEntry[] array = (from entry in entries
                               select new ScoredEntry(entry, CalculateScore(observedBytes, entry.Bytes, rareDropMode)) into candidate
                               orderby candidate.Score
                               select candidate).ToArray();
        ScoredEntry scoredEntry = array[0];
        if (rareDropMode)
        {
            if (!PassesRareAccessoryPrefixRule(observedBytes, scoredEntry.Entry))
            {
                match = null;
                return false;
            }
            for (int num = 1; num < array.Length; num++)
            {
                ScoredEntry scoredEntry2 = array[num];
                if (AreRelatedRareCandidates(scoredEntry.Entry, scoredEntry2.Entry))
                {
                    if (!(scoredEntry2.Score - scoredEntry.Score < 0.1f))
                    {
                        break;
                    }
                    match = null;
                    return false;
                }
            }
        }
        if (scoredEntry.Score > 0.34f || IsFilteredCountOneTrash(scoredEntry.Entry.Name, quantity))
        {
            match = null;
            return false;
        }
        match = new CompanionItemMatch(observedText, scoredEntry.Entry.Name, scoredEntry.Score, IsExact: false);
        return true;
    }

    internal static int LevenshteinDistance(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }
        if (right.Length == 0)
        {
            return left.Length;
        }
        if (right.Length > left.Length)
        {
            return LevenshteinDistance(right, left);
        }
        int[] array = new int[right.Length + 1];
        int[] array2 = new int[right.Length + 1];
        for (int i = 0; i <= right.Length; i++)
        {
            array[i] = i;
        }
        for (int j = 1; j <= left.Length; j++)
        {
            array2[0] = j;
            for (int k = 1; k <= right.Length; k++)
            {
                int val = array[k - 1] + ((left[j - 1] != right[k - 1]) ? 1 : 0);
                array2[k] = Math.Min(Math.Min(array[k] + 1, array2[k - 1] + 1), val);
            }
            int[] array3 = array2;
            array2 = array;
            array = array3;
        }
        return array[right.Length];
    }

    private static IEnumerable<Entry> CreateEntries(IEnumerable<string> canonicalItemNames)
    {
        ArgumentNullException.ThrowIfNull(canonicalItemNames, "canonicalItemNames");
        foreach (string canonicalItemName in canonicalItemNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(canonicalItemName, "name");
            yield return new Entry(canonicalItemName, Encoding.UTF8.GetBytes(canonicalItemName), null, Gear.None);
        }
    }

    private static IEnumerable<Entry> CreateEntries(IEnumerable<CompanionRareCatalogEntry> catalogEntries)
    {
        ArgumentNullException.ThrowIfNull(catalogEntries, "catalogEntries");
        foreach (CompanionRareCatalogEntry catalogEntry in catalogEntries)
        {
            ArgumentNullException.ThrowIfNull(catalogEntry, "item");
            ArgumentException.ThrowIfNullOrWhiteSpace(catalogEntry.Name, "item.Name");
            yield return new Entry(catalogEntry.Name, Encoding.UTF8.GetBytes(catalogEntry.Name), catalogEntry.IconPath, Gear.None);
        }
    }

    private static float CalculateScore(ReadOnlySpan<byte> observed, ReadOnlySpan<byte> candidate, bool rareDropMode)
    {
        int num = Math.Max(observed.Length, candidate.Length);
        float num2 = ((num == 0) ? 0f : ((float)LevenshteinDistance(observed, candidate) / (float)num));
        if (rareDropMode && candidate.Length > 0 && candidate.Length < observed.Length)
        {
            List<int> list = new List<int> { 0 };
            List<int> list2 = new List<int>();
            for (int i = 0; i < observed.Length; i++)
            {
                if (observed[i] == 32)
                {
                    list2.Add(i);
                    list.Add(i + 1);
                }
            }
            list2.Add(observed.Length);
            foreach (int item in list)
            {
                foreach (int item2 in list2)
                {
                    int num3 = item2 - item;
                    if (num3 <= 0 || Math.Abs(num3 - candidate.Length) > 2)
                    {
                        continue;
                    }
                    float num4 = (float)LevenshteinDistance(observed.Slice(item, num3), candidate) / (float)candidate.Length;
                    if (num4 < num2)
                    {
                        num2 = num4;
                        if (num2 == 0f)
                        {
                            break;
                        }
                    }
                }
                if (num2 == 0f)
                {
                    break;
                }
            }
        }
        if (candidate.Length >= 3 && candidate[0] == 66 && candidate[1] == 79 && candidate[2] == 78)
        {
            num2 += 0.01f;
        }
        return num2;
    }

    private static bool PassesRareAccessoryPrefixRule(ReadOnlySpan<byte> observed, Entry candidate)
    {
        if (CountNonEmptySpaceSeparatedWords(candidate.Bytes) != 2 || ClassifyEnglishSuffix(candidate.Name) == Gear.None || CountNonEmptySpaceSeparatedWords(observed) < 3)
        {
            return true;
        }
        if (!TryGetPrefixBeforeFirstSpace(observed, out var prefix) || !TryGetPrefixBeforeFirstSpace(candidate.Bytes, out var prefix2))
        {
            return false;
        }
        int num = Math.Max(prefix.Length, prefix2.Length);
        return (float)LevenshteinDistance(prefix, prefix2) / (float)num <= 0.34f;
    }

    private bool AreRelatedRareCandidates(Entry left, Entry right)
    {
        if (TryGetPrefixBeforeFirstSpace(left.Bytes, out var prefix) && TryGetPrefixBeforeFirstSpace(right.Bytes, out var prefix2) && prefix.SequenceEqual(prefix2))
        {
            return true;
        }
        Gear gear = (MetadataTablePresent ? left.GearCategory : ClassifyEnglishSuffix(left.Name));
        Gear gear2 = (MetadataTablePresent ? right.GearCategory : ClassifyEnglishSuffix(right.Name));
        if (gear != Gear.None)
        {
            return gear == gear2;
        }
        return false;
    }

    private static bool TryGetPrefixBeforeFirstSpace(ReadOnlySpan<byte> value, out ReadOnlySpan<byte> prefix)
    {
        int num = value.IndexOf<byte>(32);
        if (num <= 0)
        {
            prefix = default(ReadOnlySpan<byte>);
            return false;
        }
        prefix = value.Slice(0, num);
        return true;
    }

    private static int CountNonEmptySpaceSeparatedWords(ReadOnlySpan<byte> value)
    {
        int num = 0;
        bool flag = false;
        ReadOnlySpan<byte> readOnlySpan = value;
        for (int i = 0; i < readOnlySpan.Length; i++)
        {
            if (readOnlySpan[i] == 32)
            {
                flag = false;
            }
            else if (!flag)
            {
                flag = true;
                num++;
            }
        }
        return num;
    }

    private static Gear ClassifyGear(string name, string? iconPath)
    {
        if (iconPath != null)
        {
            string[] array = iconPath.Split('/');
            for (int i = 0; i < array.Length; i++)
            {
                Gear gear = array[i] switch
                {
                    "18_belt" => Gear.Belt,
                    "17_earring" => Gear.Earring,
                    "15_necklace" => Gear.Necklace,
                    "16_ring" => Gear.Ring,
                    _ => Gear.None,
                };
                if (gear != Gear.None)
                {
                    return gear;
                }
            }
        }
        return ClassifyEnglishSuffix(name);
    }

    private static Gear ClassifyEnglishSuffix(string value)
    {
        int num = value.LastIndexOf(' ');
        string text;
        if (num >= 0)
        {
            int num2 = num + 1;
            text = value.Substring(num2, value.Length - num2);
        }
        else
        {
            text = value;
        }
        switch (text)
        {
            case "Belt":
                return Gear.Belt;
            case "Earring":
            case "Earrings":
                return Gear.Earring;
            case "Necklace":
                return Gear.Necklace;
            case "Ring":
                return Gear.Ring;
            default:
                return Gear.None;
        }
    }

    private static bool IsFilteredCountOneTrash(string name, int quantity)
    {
        if (quantity == 1)
        {
            return CountOneTrashNames.Contains(name);
        }
        return false;
    }
}

