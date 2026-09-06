using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// Text-only portion of the BDO Companion 0.7.4 loot OCR pipeline.
/// This type deliberately contains no catalog matching or additional OCR repairs.
/// </summary>
internal static partial class CompanionTextPipeline
{
    private const int CharacterAdvance = 24;

    /// <summary>
    /// Applies the verified character filtering, literal repairs, quantity extraction,
    /// suffix repairs, and the narrow Black Stone repair in their original order.
    /// </summary>
    internal static CompanionTextResult Process(
        string? ocrText,
        int templateQuantity,
        bool rareDropMode,
        float recognizedTextWidth)
    {
        var text = NormalizeRawText(ocrText);
        var quantityMatch = QuantityRegex().Match(text);
        var hasParsedOcrQuantity = TryParseQuantity(quantityMatch, out var parsedQuantity);
        var quantity = hasParsedOcrQuantity
            ? parsedQuantity
            : rareDropMode
                ? 1
                : templateQuantity;

        // Companion removes the first quantity-shaped match from the name even when the
        // captured digits cannot subsequently be parsed as an ASCII decimal value.
        var name = QuantityRegex().Replace(text, string.Empty, 1);
        name = TrailingMultiplierRegex().Replace(name, string.Empty, 1);
        name = GolemPossessiveRegex().Replace(name, "Golem's", 1);
        name = TaintedGolemHeartRegex().Replace(name, "Tainted Golem's Heart Fragment", 1);
        name = TaintedHeartFragmentRegex().Replace(name, "Tainted Golem's Heart Fragment", 1);
        name = TaintedGolemFragmentRegex().Replace(name, "Tainted Golem's Heart Fragment", 1);
        name = DestroyedAncientWeaponStoneRegex().Replace(
            name,
            "Destroyed Ancient Weapon Power Stone",
            1);
        name = DestroyedAncientWeaponRegex().Replace(
            name,
            "Destroyed Ancient Weapon Power Stone",
            1);
        name = DestroyedAncientWeaponPowerRegex().Replace(
            name,
            "Destroyed Ancient Weapon Power Stone",
            1);
        name = UnderwaterAncientWeaponStoneRegex().Replace(
            name,
            "Underwater Ancient Weapon Power Stone",
            1);
        name = UnderwaterAncientWeaponRegex().Replace(
            name,
            "Underwater Ancient Weapon Power Stone",
            1);
        name = UnderwaterAncientWeaponPowerRegex().Replace(
            name,
            "Underwater Ancient Weapon Power Stone",
            1);
        name = TaintedMoonlightSpiritRegex().Replace(
            name,
            "Tainted Moonlight Spirit Powder",
            1);
        // This is the sole regex repair that uses replace_all (replacen limit zero).
        name = WonConfusionRegex().Replace(name, "WON");
        name = TrollHideRegex().Replace(name, "Fiery Troll Hide", 1);
        name = FieryTrollRegex().Replace(name, "Fiery Troll Hide", 1);
        name = SealedBlackRegex().Replace(name, "Sealed Black Magic Crystal", 1);
        name = RefinedEssenceRegex().Replace(name, "Refined Essence of Devouring", 1);

        // Rust str::trim is called only after every regex repair. It uses Unicode
        // White_Space and therefore removes the space left before a quantity suffix.
        name = name.Trim();

        if (recognizedTextWidth > 193 &&
            recognizedTextWidth <= 198 &&
            string.Equals(name, "Black", StringComparison.Ordinal))
        {
            name = "Black Stone";
        }

        return new CompanionTextResult(name, quantity, hasParsedOcrQuantity);
    }

    /// <summary>
    /// Removes Companion's fixed punctuation set, performs its three literal repairs,
    /// decomposes to Unicode NFD, and removes all combining marks. Whitespace and case
    /// are intentionally preserved.
    /// </summary>
    internal static string NormalizeRawText(string? ocrText)
    {
        if (string.IsNullOrEmpty(ocrText))
        {
            return string.Empty;
        }

        var filtered = new StringBuilder(ocrText.Length);
        foreach (var rune in ocrText.EnumerateRunes())
        {
            if (!IsRemovedCharacter(rune.Value))
            {
                filtered.Append(rune);
            }
        }

        var repaired = filtered
            .ToString()
            .Replace("Fiery Hide", "Fiery Troll Hide", StringComparison.Ordinal)
            .Replace("MossCovered", "Moss-Covered", StringComparison.Ordinal)
            .Replace("Faded Mask", "Faded Bandit Mask", StringComparison.Ordinal);

        var decomposed = repaired.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (!IsCombiningMark(Rune.GetUnicodeCategory(rune)))
            {
                normalized.Append(rune);
            }
        }

        return normalized.ToString();
    }

    /// <summary>
    /// Applies the verified pre-catalog expected-width gate. A parsed OCR quantity
    /// bypasses the width comparison, but an empty item name is always rejected.
    /// </summary>
    internal static bool PassesExpectedWidth(
        CompanionTextResult result,
        float recognizedTextWidth,
        int leftmostQuantityX,
        float nameScale)
    {
        if (result.Name.Length == 0)
        {
            return false;
        }

        if (result.HasParsedOcrQuantity)
        {
            return true;
        }

        var expectedRight = ExpectedWidthBase(recognizedTextWidth) +
            (Encoding.UTF8.GetByteCount(result.Name) * nameScale * CharacterAdvance);
        var observedRight = leftmostQuantityX * nameScale;
        return observedRight <= expectedRight;
    }

    private static bool TryParseQuantity(Match match, out int quantity)
    {
        quantity = 0;
        if (!match.Success)
        {
            return false;
        }

        var capture = match.Groups[1].Value;
        if (capture is "I" or "i")
        {
            quantity = 1;
            return true;
        }

        // Rust's decimal parser accepts ASCII digits. The regex itself uses Unicode
        // \d, so a non-ASCII numeric capture is a match but not a parsed quantity.
        if (capture.Length == 0 || capture.Any(static character => character is < '0' or > '9'))
        {
            return false;
        }

        return int.TryParse(
            capture,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out quantity);
    }

    private static float ExpectedWidthBase(float recognizedTextWidth) =>
        recognizedTextWidth switch
        {
            >= 500f => -85f,
            >= 450f => -30f,
            >= 400f => -15f,
            >= 300f => 0f,
            >= 250f => 25f,
            >= 95f => 45f,
            _ => 115f,
        };

    private static bool IsCombiningMark(UnicodeCategory category) =>
        category is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark;

    private static bool IsRemovedCharacter(int value) =>
        value is 0x0024 or // $
            0x003F or // ?
            0x003C or // <
            0x003E or // >
            0x002D or // -
            0x002F or // /
            0x005C or // backslash
            0x0026 or // &
            0x2022 or // bullet
            0x005F or // _
            0x0021 or // !
            0x003A or // :
            0x003B or // ;
            0x002C or // comma
            0x002E or // full stop
            0x00AF or // macron
            0x0025 or // %
            0x00B1; // plus-minus

    [GeneratedRegex(@"[xkv] ?(\d{1,4}|I|i)", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityRegex();

    // Rust regex `$` is absolute end-of-haystack without multi-line mode. Use .NET
    // `\z` because .NET `$` additionally matches immediately before a final LF.
    [GeneratedRegex(@" [xkv]\z", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingMultiplierRegex();

    [GeneratedRegex(@"Golem ?s", RegexOptions.CultureInvariant)]
    private static partial Regex GolemPossessiveRegex();

    [GeneratedRegex(@"Tainted Golem's Heart\z", RegexOptions.CultureInvariant)]
    private static partial Regex TaintedGolemHeartRegex();

    [GeneratedRegex(@"Tainted Heart Fragment\z", RegexOptions.CultureInvariant)]
    private static partial Regex TaintedHeartFragmentRegex();

    [GeneratedRegex(@"Tainted Golem's Fragment\z", RegexOptions.CultureInvariant)]
    private static partial Regex TaintedGolemFragmentRegex();

    [GeneratedRegex(@"Destroyed Ancient Weapon Stone\z", RegexOptions.CultureInvariant)]
    private static partial Regex DestroyedAncientWeaponStoneRegex();

    [GeneratedRegex(@"Destroyed Ancient Weapon\z", RegexOptions.CultureInvariant)]
    private static partial Regex DestroyedAncientWeaponRegex();

    [GeneratedRegex(@"Destroyed Ancient Weapon Power\z", RegexOptions.CultureInvariant)]
    private static partial Regex DestroyedAncientWeaponPowerRegex();

    [GeneratedRegex(@"Underwater Ancient Weapon Stone\z", RegexOptions.CultureInvariant)]
    private static partial Regex UnderwaterAncientWeaponStoneRegex();

    [GeneratedRegex(@"Underwater Ancient Weapon\z", RegexOptions.CultureInvariant)]
    private static partial Regex UnderwaterAncientWeaponRegex();

    [GeneratedRegex(@"Underwater Ancient Weapon Power\z", RegexOptions.CultureInvariant)]
    private static partial Regex UnderwaterAncientWeaponPowerRegex();

    [GeneratedRegex(@"Tainted Moonlight Spirit\z", RegexOptions.CultureInvariant)]
    private static partial Regex TaintedMoonlightSpiritRegex();

    [GeneratedRegex(@"(?:V.ON|.VON)", RegexOptions.CultureInvariant)]
    private static partial Regex WonConfusionRegex();

    [GeneratedRegex(@"^Troll Hide\z", RegexOptions.CultureInvariant)]
    private static partial Regex TrollHideRegex();

    [GeneratedRegex(@"Fiery Troll\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieryTrollRegex();

    [GeneratedRegex(@"Sealed Black\z", RegexOptions.CultureInvariant)]
    private static partial Regex SealedBlackRegex();

    [GeneratedRegex(@"^Ref\S*\s+Essence of Devouring\z", RegexOptions.CultureInvariant)]
    private static partial Regex RefinedEssenceRegex();
}

internal readonly record struct CompanionTextResult(
    string Name,
    int Quantity,
    bool HasParsedOcrQuantity);
