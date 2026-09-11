using System.Globalization;
using System.Xml.Linq;

namespace BdoGrindTracker.Ocr;

public enum RareLootAnchorStatus
{
    Active,
    PresetFallback,
    Hidden,
    Missing,
    Invalid,
    Ambiguous,
}

public sealed record RareLootAnchorResolution(RareLootAnchorStatus Status, string Source, string Reason);

public sealed partial class CompanionCalibrationReader
{
    private static CompanionCalibration ResolveRareCalibration(
        IReadOnlyList<XElement> elements, CompanionCalibration calibration)
    {
        var activeSections = elements.Where(element => element.Name == "UIData").ToArray();
        if (activeSections.Length != 1)
            return WithoutRare(activeSections.Length == 0 ? RareLootAnchorStatus.Missing : RareLootAnchorStatus.Ambiguous,
                "active-ui", "The active UI section is missing or ambiguous.");

        var activeEntries = activeSections[0].Elements("UIData").Where(IsRareEntry).ToArray();
        if (activeEntries.Length == 0)
            return WithoutRare(RareLootAnchorStatus.Missing, "active-ui", "The active UI has no rare-loot entry.");
        if (activeEntries.Length != 1)
            return WithoutRare(RareLootAnchorStatus.Ambiguous, "active-ui", "The active UI has conflicting rare-loot entries.");

        var active = activeEntries[0];
        if (active.Attribute("IsShow")?.Value == "false")
            return WithoutRare(RareLootAnchorStatus.Hidden, "active-ui", "The active rare-loot panel is hidden.");
        if (active.Attribute("IsShow")?.Value != "true")
            return WithoutRare(RareLootAnchorStatus.Invalid, "active-ui", "The active rare-loot visibility is unknown.");

        if (TryReadRareRelativePosition(active, out var activeX, out var activeY) &&
            !IsSuspiciousRareOrigin(active, activeX, activeY) &&
            TryBuildRarePosition(calibration, activeX, activeY, out var activePosition))
            return WithRare(activePosition, RareLootAnchorStatus.Active, "active-ui",
                "The visible active rare-loot position is valid.");

        // A preset can repair coordinates, but cannot enable a missing or hidden panel.
        var presetSections = elements.Where(element => element.Name == "UISettingPreset").ToArray();
        if (presetSections.Length > 1)
            return WithoutRare(RareLootAnchorStatus.Ambiguous, "preset", "The saved UI preset section is ambiguous.");
        if (presetSections.Length == 0)
            return WithoutRare(RareLootAnchorStatus.Invalid, "active-ui", "No compatible saved rare-loot position was found.");

        var candidates = new Dictionary<(int X, int Y), List<string>>();
        for (var presetIndex = 0; presetIndex < 3; presetIndex++)
        {
            var name = $"UISettingPreset{presetIndex}";
            // BDO stores flat, repeated entries named after their preset. Revert,
            // battle layouts and nested character data are not active-layout evidence.
            var entries = presetSections[0].Elements(name).ToArray();
            var mainEntries = entries.Where(element => HasUiIndex(element, LootUiDataIndex)).ToArray();
            var rareEntries = entries.Where(IsRareEntry).ToArray();
            if (mainEntries.Length != 1 || rareEntries.Length != 1 ||
                mainEntries[0].Attribute("IsShow")?.Value != "true" ||
                rareEntries[0].Attribute("IsShow")?.Value != "true" ||
                !TryReadRareRelativePosition(mainEntries[0], out var mainX, out var mainY) ||
                !TryScaleRarePosition(calibration, mainX, mainY, out var mainPosition) ||
                Math.Abs((long)mainPosition.X - calibration.LootAnchorX) > 1 ||
                Math.Abs((long)mainPosition.Y - calibration.LootAnchorY) > 1 ||
                !RarePresetMetadataMatches(entries, calibration) ||
                !TryReadRareRelativePosition(rareEntries[0], out var rareX, out var rareY) ||
                // A zero preset supplies no independent evidence for repairing a zero active entry.
                (rareX == 0f && rareY == 0f) ||
                !TryBuildRarePosition(calibration, rareX, rareY, out var position))
                continue;

            if (!candidates.TryGetValue(position, out var sources))
                candidates.Add(position, sources = []);
            sources.Add(name);
        }

        if (candidates.Count == 0)
            return WithoutRare(RareLootAnchorStatus.Invalid, "active-ui", "No compatible saved rare-loot position was found.");
        if (candidates.Count > 1)
            return WithoutRare(RareLootAnchorStatus.Ambiguous, "preset", "Compatible presets disagree on the rare-loot position.");

        var selected = candidates.Single();
        return WithRare(selected.Key, RareLootAnchorStatus.PresetFallback, string.Join(",", selected.Value),
            "Compatible presets provide one unique rare-loot position.");

        CompanionCalibration WithoutRare(RareLootAnchorStatus status, string source, string reason) => calibration with
        {
            HasRareLootAnchor = false,
            RareLootAnchorX = 0,
            RareLootAnchorY = 0,
            RareLootResolution = new(status, source, reason),
        };

        CompanionCalibration WithRare((int X, int Y) position, RareLootAnchorStatus status, string source, string reason) => calibration with
        {
            HasRareLootAnchor = true,
            RareLootAnchorX = position.X,
            RareLootAnchorY = position.Y,
            RareLootResolution = new(status, source, reason),
        };
    }

    private static bool HasUiIndex(XElement element, uint expected) =>
        TryParseUnsigned(element.Attribute("Index")?.Value, out var index) && index == expected;

    private static bool IsRareEntry(XElement element) => HasUiIndex(element, 161);

    private static bool TryReadRareRelativePosition(XElement element, out float x, out float y)
    {
        x = y = 0;
        return TryReadRareNumber(element.Attribute("RelativePosX"), out x) &&
            TryReadRareNumber(element.Attribute("RelativePosY"), out y) &&
            x is >= 0f and <= 1f && y is >= 0f and <= 1f;
    }

    private static bool TryReadRareNumber(XAttribute? attribute, out float value)
    {
        value = 0;
        return attribute is not null && float.TryParse(attribute.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }

    private static bool IsSuspiciousRareOrigin(XElement element, float x, float y) =>
        x == 0f && y == 0f && element.Attribute("PendingType")?.Value == "RightBottom" &&
        (TryReadRareNumber(element.Attribute("PosX"), out var positionX) && positionX != 0f ||
         TryReadRareNumber(element.Attribute("PosY"), out var positionY) && positionY != 0f);

    private static bool TryBuildRarePosition(CompanionCalibration calibration, float x, float y,
        out (int X, int Y) position)
    {
        if (!TryScaleRarePosition(calibration, x, y, out position)) return false;
        try
        {
            var candidate = calibration with
            {
                HasRareLootAnchor = true,
                RareLootAnchorX = position.X,
                RareLootAnchorY = position.Y,
            };
            var band = CompanionNormalLootGeometry.CalculateRareBandCrop(candidate);
            return band.Width > 0 && band.Height > 0 && band.X >= 0 && band.Y >= 0 &&
                (long)band.X + band.Width <= calibration.ScreenWidth &&
                (long)band.Y + band.Height <= calibration.ScreenHeight;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            // Optional rare metadata must never prevent normal-loot tracking.
            return false;
        }
    }

    private static bool TryScaleRarePosition(CompanionCalibration calibration, float x, float y,
        out (int X, int Y) position)
    {
        position = default;
        try
        {
            position = (ScaleRelativeCoordinate(x, calibration.ScreenWidth), ScaleRelativeCoordinate(y, calibration.ScreenHeight));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool RarePresetMetadataMatches(IReadOnlyList<XElement> entries, CompanionCalibration calibration)
    {
        foreach (var entry in entries)
        {
            if (!Matches(entry.Attribute("ScreenWidth"), calibration.ScreenWidth) ||
                !Matches(entry.Attribute("ResolutionWidth"), calibration.ScreenWidth) ||
                !Matches(entry.Attribute("ScreenHeight"), calibration.ScreenHeight) ||
                !Matches(entry.Attribute("ResolutionHeight"), calibration.ScreenHeight) ||
                !Matches(entry.Attribute("UiScale"), calibration.UiScale))
                return false;

            // Validate explicit metadata under an index-less preset marker too;
            // a widget's Pos/Size values are not screen dimensions.
            if (entry.Attribute("Index") is not null) continue;
            foreach (var resolution in entry.Elements("Resolution"))
                if (!Matches(resolution.Attribute("Width"), calibration.ScreenWidth) ||
                    !Matches(resolution.Attribute("Height"), calibration.ScreenHeight))
                    return false;
            foreach (var scale in entry.Elements("UiScale"))
                if (!Matches(scale.Attribute("Value"), calibration.UiScale)) return false;
        }
        return true;

        static bool Matches(XAttribute? attribute, float expected) => attribute is null ||
            TryReadRareNumber(attribute, out var value) && value == expected;
    }
}
