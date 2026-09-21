using System.Globalization;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal sealed record BuffFrameReading(IReadOnlyList<BuffObservation> Observations)
{
    /// <summary>Visible but unreadable or ambiguous buffs; only their accounting continuity is lost.</summary>
    internal IReadOnlyList<string> UnknownBuffIds { get; init; } = [];
}
internal interface IBuffFrameReader : IDisposable
{
    string? LastDiagnostic => null;
    BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken);
    BuffFrameReading? Read(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken) =>
        Read(frame, cancellationToken);
}

/// <summary>Matches calibrated HUD icons across the bar, then OCRs only their associated timers.</summary>
internal sealed partial class BuffFrameReader(
    Func<BuffRecognitionProfile?> readProfile,
    Func<Mat, CancellationToken, CompanionOcrResult>? recognize = null) : IBuffFrameReader
{
    private CompanionWindowsOcrRecognizer? _engine;
    private readonly Dictionary<string, CachedTemplate> _templates = new(StringComparer.Ordinal);
    private bool _disposed;
    public string? LastDiagnostic { get; private set; }
    internal int TemplateLoadCount { get; private set; }
    internal sealed record Match(BuffIconTemplate Template, Rectangle Bounds, double Score);

    private sealed class CachedTemplate(BuffIconTemplate template, long length, DateTime lastWriteTime,
        DateTime creationTime, Mat original) : IDisposable
    {
        internal BuffIconTemplate Template { get; } = template;
        internal long Length { get; } = length;
        internal DateTime LastWriteTime { get; } = lastWriteTime;
        internal DateTime CreationTime { get; } = creationTime;
        private Mat? _scaled;
        private double _scale;

        internal Mat AtScale(double scale)
        {
            if (scale == 1) return original;
            if (_scaled is not null && _scale == scale) return _scaled;
            _scaled?.Dispose();
            _scaled = new Mat();
            Cv2.Resize(original, _scaled, new OpenCvSharp.Size(
                Math.Max(1, (int)Math.Round(original.Width * scale)),
                Math.Max(1, (int)Math.Round(original.Height * scale))), interpolation: InterpolationFlags.Area);
            _scale = scale;
            return _scaled;
        }

        public void Dispose() { _scaled?.Dispose(); original.Dispose(); }
    }

    public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        LastDiagnostic = null;
        try { return ReadFrame(frame, cancellationToken); }
        catch (OperationCanceledException)
        {
            LastDiagnostic = "Buff-Prüfung abgebrochen oder Zeitlimit erreicht; der Verbrauch bleibt unbekannt.";
            throw;
        }
        catch (Exception)
        {
            return Unknown("Buff-Prüfung fehlgeschlagen. Vorlagen und Kalibrierung prüfen oder den Screenshot-Test wiederholen.");
        }
    }

    private BuffFrameReading? ReadFrame(Bitmap frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = readProfile();
        if (profile is null || profile.ValidationError is { })
        {
            ClearTemplates();
            return Unknown(profile?.ValidationError ?? "Kein nutzbares Buff-Profil geladen. Buffleiste und Vorlagen kalibrieren.");
        }
        // Remove obsolete entries even when the new screen layout cannot currently be resolved.
        foreach (var id in _templates.Keys.Where(id => !profile.Templates.Any(template =>
                     template.BuffId == id && template == _templates[id].Template)).ToArray())
        {
            _templates[id].Dispose();
            _templates.Remove(id);
        }
        if (BuffHudConfigurationReader.Resolve(profile, frame.Size) is not { } layout)
            return Unknown(profile.UiDataIndex is null
                ? "Buffleistenbereich passt nicht zum Screenshot. Auflösung und Bereich neu kalibrieren."
                : "Buffleistenposition nicht auflösbar. gameVariable.xml, sichtbaren UIData-Eintrag, Auflösung und UI-Skalierung prüfen.");
        using var crop = frame.Clone(layout.Region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var bar = CompanionFrameDecoder.Decode(crop);
        var candidates = new List<Match>();
        foreach (var template in profile.Templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var icon = GetTemplate(template, layout.Scale);
            if (icon is null) return null;
            if (icon.Width > bar.Width || icon.Height > bar.Height)
                return Unknown($"Buff-Vorlage {template.BuffId} ist größer als der kalibrierte Buffleistenbereich.");
            Cv2.MeanStdDev(icon, out _, out var deviation);
            // Constant or nearly blank templates match any flat part of the HUD.
            if (deviation.Val0 + deviation.Val1 + deviation.Val2 < 30)
                return Unknown($"Buff-Vorlage {template.BuffId} hat zu wenig Bildinhalt. Das vollständige Buffsymbol neu ausschneiden.");
            using var scores = new Mat();
            Cv2.MatchTemplate(bar, icon, scores, TemplateMatchModes.CCoeffNormed);
            for (var occurrence = 0; occurrence < 8; occurrence++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Cv2.MinMaxLoc(scores, out _, out var best, out _, out var at);
                if (!double.IsFinite(best) || best < template.MinimumSimilarity) break;
                candidates.Add(new(template, new Rectangle(at.X, at.Y, icon.Width, icon.Height), best));
                var excluded = new Rect(Math.Max(0, at.X - icon.Width / 2), Math.Max(0, at.Y - icon.Height / 2), 0, 0);
                excluded.Width = Math.Min(scores.Width, at.X + icon.Width / 2 + 1) - excluded.X;
                excluded.Height = Math.Min(scores.Height, at.Y + icon.Height / 2 + 1) - excluded.Y;
                using var suppression = new Mat(scores, excluded);
                suppression.SetTo(Scalar.All(-1));
            }
        }
        var matches = SelectUnambiguous(candidates);
        if (candidates.Count == 0)
            return Unknown("Keine ausgewählte Buff-Vorlage erkannt. Sichtbare Buffleiste, UI-Skalierung und Iconausschnitte prüfen.");
        var unknownBuffIds = candidates.Where(candidate => !matches.Any(match =>
                match.Template.BuffId == candidate.Template.BuffId ||
                Overlap(match.Bounds, candidate.Bounds) && match.Score > candidate.Score + .04))
            .Select(candidate => candidate.Template.BuffId).ToHashSet(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        if (unknownBuffIds.Count > 0)
            diagnostics.Add("Einige Buffsymbole sind mehrdeutig oder mehrfach vorhanden. Vorlagen und gewählte Varianten prüfen.");
        if (recognize is null && (_engine ??= CompanionWindowsOcrRecognizer.TryCreate()) is null)
            return Unknown("Restzeiten-OCR nicht verfügbar. Eine unterstützte Windows-OCR-Sprache installieren.");
        var observations = new List<BuffObservation>();
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = match.Template.TimerRegion;
            var region = new Rectangle(match.Bounds.X + (int)Math.Round(timer.X * layout.Scale),
                match.Bounds.Y + (int)Math.Round(timer.Y * layout.Scale),
                (int)Math.Round(timer.Width * layout.Scale), (int)Math.Round(timer.Height * layout.Scale));
            if (!BuffHudConfigurationReader.Contains(new(bar.Width, bar.Height), region))
                return Unknown($"Zeitbereich für {match.Template.BuffId} liegt außerhalb der Buffleiste. Den Bereich einschließlich Restzeit neu kalibrieren.");
            (TimeSpan Remaining, TimeSpan Precision)? remaining;
            try { remaining = ReadTimer(bar, region, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { remaining = null; }
            if (remaining is not { } value)
            {
                unknownBuffIds.Add(match.Template.BuffId);
                diagnostics.Add($"Restzeit für {match.Template.BuffId} ist unlesbar oder widersprüchlich. Den Zeitbereich im Screenshot-Test prüfen.");
                continue;
            }
            var definition = BuffPriceCatalog.Definitions.Single(definition => definition.Id == match.Template.BuffId);
            if (value.Remaining <= TimeSpan.Zero || value.Remaining > definition.Duration)
            {
                unknownBuffIds.Add(match.Template.BuffId);
                diagnostics.Add($"Restzeit für {match.Template.BuffId} passt nicht zur gewählten Laufzeitvariante. Variante und Zeitbereich prüfen.");
                continue;
            }
            observations.Add(new(definition.Id, value.Remaining, value.Precision)
            { ConsumptionAttributionConfirmed = match.Template.ConsumptionAttributionConfirmed });
        }
        LastDiagnostic = $"{observations.Count} Buff(s) mit lesbarer Restzeit erkannt. Kosten beruhen auf den gewählten Varianten." +
            (diagnostics.Count == 0 ? string.Empty : " " + string.Join(" ", diagnostics));
        return new(observations) { UnknownBuffIds = unknownBuffIds.Order(StringComparer.Ordinal).ToArray() };
    }

    private Mat? GetTemplate(BuffIconTemplate template, double scale)
    {
        var file = new FileInfo(template.IconPath);
        if (!file.Exists || file.Length is <= 0 or > 1024 * 1024)
        {
            if (_templates.Remove(template.BuffId, out var missing)) missing.Dispose();
            Unknown($"Buff-Vorlage fehlt, ist leer oder größer als 1 MB: {Path.GetFileName(template.IconPath)}");
            return null;
        }
        if (_templates.TryGetValue(template.BuffId, out var cached) &&
            (cached.Template != template || cached.Length != file.Length || cached.LastWriteTime != file.LastWriteTimeUtc ||
             cached.CreationTime != file.CreationTimeUtc))
        {
            cached.Dispose();
            _templates.Remove(template.BuffId);
            cached = null;
        }
        if (cached is null)
        {
            var original = Cv2.ImRead(template.IconPath, ImreadModes.Color);
            if (original.Empty() || original.Width is < 8 or > 256 || original.Height is < 8 or > 256)
            {
                original.Dispose();
                Unknown($"Buff-Vorlage ist kein lesbares Icon mit 8–256 Pixeln je Seite: {Path.GetFileName(template.IconPath)}");
                return null;
            }
            cached = new(template, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc, original);
            _templates.Add(template.BuffId, cached);
            TemplateLoadCount++;
        }
        return cached.AtScale(scale);
    }

    private BuffFrameReading? Unknown(string reason) { LastDiagnostic = reason; return null; }

    private void ClearTemplates()
    {
        foreach (var template in _templates.Values) template.Dispose();
        _templates.Clear();
    }

    internal static IReadOnlyList<Match> SelectUnambiguous(IReadOnlyList<Match> candidates)
    {
        var accepted = new List<Match>();
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score))
        {
            if (candidates.Any(other => !ReferenceEquals(candidate, other) &&
                other.Template.BuffId != candidate.Template.BuffId && Overlap(candidate.Bounds, other.Bounds) &&
                other.Score >= candidate.Score - .04)) continue;
            if (candidates.Any(other => !ReferenceEquals(candidate, other) &&
                other.Template.BuffId == candidate.Template.BuffId && !Overlap(candidate.Bounds, other.Bounds))) continue;
            if (accepted.Any(other => other.Template.BuffId == candidate.Template.BuffId || Overlap(candidate.Bounds, other.Bounds))) continue;
            accepted.Add(candidate);
        }
        return accepted;
    }

    private static bool Overlap(Rectangle left, Rectangle right)
    {
        var intersection = Rectangle.Intersect(left, right);
        return (long)intersection.Width * intersection.Height > Math.Min((long)left.Width * left.Height,
            (long)right.Width * right.Height) * .35;
    }

    internal (TimeSpan Remaining, TimeSpan Precision)? ReadTimer(Mat bar, Rectangle region, CancellationToken token)
    {
        if (recognize is null && (_engine ??= CompanionWindowsOcrRecognizer.TryCreate()) is null) return null;
        using var crop = new Mat(bar, new Rect(region.X, region.Y, region.Width, region.Height));
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        // Keep the narrow strokes of closed digits through the two resize stages.
        // At 48 pixels the Strong Sword font's 8 can alias to 3 in grayscale.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var large = new Mat();
            var scale = Math.Clamp((attempt == 0 ? 56d : 32d) / region.Height, 1, 4);
            Cv2.Resize(gray, large, new OpenCvSharp.Size((int)Math.Round(gray.Width * scale),
                (int)Math.Round(gray.Height * scale)), interpolation: InterpolationFlags.Cubic);
            (TimeSpan Remaining, TimeSpan Precision)? accepted = null;
            var readableVariants = 0;
            for (var variant = 0; variant < 2; variant++)
            {
                token.ThrowIfCancellationRequested();
                using var prepared = new Mat();
                if (variant == 0) Cv2.BitwiseNot(large, prepared);
                else Cv2.Threshold(large, prepared, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
                using var padded = new Mat();
                Cv2.CopyMakeBorder(prepared, padded, 12, 12, 12, 12, BorderTypes.Constant, Scalar.All(255));
                // A second interpolation after padding keeps tiny serifs connected:
                // direct scaling can split 13m into 113m or the digits of 114m.
                using var readable = new Mat();
                Cv2.Resize(padded, readable, new OpenCvSharp.Size(), 1.5, 1.5, InterpolationFlags.Cubic);
                var result = recognize?.Invoke(readable, token) ?? _engine!.Recognize(readable, token);
                var timer = ParseTimer(result.Text);
                if (accepted is not null && timer is not null && accepted != timer) return null;
                if (timer is not null) readableVariants++;
                accepted ??= timer;
            }
            if (accepted is not null && (attempt == 0 || readableVariants == 2)) return accepted;
            // Very short labels such as 2h can disappear from OCR at the larger
            // size. Retry unreadable labels at a smaller scale, requiring two
            // agreeing preparations; never override a contradictory primary read.
        }
        return null;
    }

    internal static (TimeSpan Remaining, TimeSpan Precision)? ParseTimer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 32) return null;
        var value = text.Trim();
        var clock = ClockTimer().Match(value);
        if (clock.Success)
        {
            var parts = value.Split(':').Select(part => int.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
            var seconds = parts.Length == 3 ? parts[0] * 3600 + parts[1] * 60 + parts[2] : parts[0] * 60 + parts[1];
            return seconds is > 0 and <= 86400 ? (TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1)) : null;
        }
        var units = UnitTimer().Match(value);
        if (!units.Success) return null;
        var hours = Part(units, "hours"); var minutes = Part(units, "minutes"); var secondsPart = Part(units, "seconds");
        if (units.Groups["hours"].Success && minutes >= 60 ||
            (units.Groups["hours"].Success || units.Groups["minutes"].Success) && secondsPart >= 60) return null;
        var duration = hours * 3600 + minutes * 60 + secondsPart;
        if (duration is <= 0 or > 86400) return null;
        var precision = units.Groups["seconds"].Success ? 1 : units.Groups["minutes"].Success ? 60 : 3600;
        return (TimeSpan.FromSeconds(duration), TimeSpan.FromSeconds(precision));
    }

    private static int Part(System.Text.RegularExpressions.Match match, string group) =>
        match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;

    [GeneratedRegex(@"^(?:[0-9]{1,3}\s*:\s*[0-5][0-9]|[0-9]{1,2}\s*:\s*[0-5][0-9]\s*:\s*[0-5][0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex ClockTimer();

    [GeneratedRegex(@"^(?:(?<hours>[0-9]{1,2})\s*(?:h|hr|hrs|std)\.?\s*)?(?:(?<minutes>[0-9]{1,3})\s*(?:m|min|mins)\.?\s*)?(?:(?<seconds>[0-9]{1,3})\s*(?:s|sec|secs|sek)\.?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitTimer();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearTemplates();
        _engine = null;
    }
}
