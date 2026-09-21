using BdoGrindTracker.Core.Buffs;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Locates bundled HUD symbols in the saved BDO buff panel. No user profile required.</summary>
internal sealed class AutomaticBuffFrameReader : IBuffFrameReader
{
    private readonly AutomaticBuffCatalog _catalog;
    private readonly BuffFrameReader _timerReader;
    private readonly Func<CompanionCalibration?>? _readCalibration;
    private readonly List<Icon> _icons = [];
    private const long MaximumMatchedFrameBytes = 4 * 1024 * 1024;
    private Mat? _matchedFrame;
    private double? _matchedExpectedIconWidth;
    private IReadOnlyList<Detection>? _matchedDetections;
    private bool _loaded;
    private bool _disposed;
    public string? LastDiagnostic { get; private set; }
    internal long TemplateResizeCount => _icons.Sum(icon => icon.ColorSizes.ResizeCount + icon.GraySizes.ResizeCount);
    internal sealed record Detection(AutomaticBuffCatalog.Template Template, Rectangle Bounds, double Score);
    private sealed record Icon(AutomaticBuffCatalog.Template Template, Mat Color, Mat Gray) : IDisposable
    {
        public ResizedTemplateCache ColorSizes { get; } = new(Color);
        public ResizedTemplateCache GraySizes { get; } = new(Gray, maximumBytes: 256 * 1024);
        public void Dispose() { ColorSizes.Dispose(); GraySizes.Dispose(); Color.Dispose(); Gray.Dispose(); }
    }

    internal AutomaticBuffFrameReader(AutomaticBuffCatalog? catalog = null,
        Func<Mat, CancellationToken, CompanionOcrResult>? recognize = null,
        Func<CompanionCalibration?>? readCalibration = null)
    {
        _catalog = catalog ?? AutomaticBuffCatalog.Default;
        _timerReader = new(() => null, recognize);
        _readCalibration = readCalibration;
    }

    public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken) => ReadFrame(frame, null, cancellationToken);

    public BuffFrameReading? Read(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken) =>
        ReadFrame(frame, capturedAt, cancellationToken);

    private BuffFrameReading? ReadFrame(Bitmap frame, DateTimeOffset? capturedAt, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastDiagnostic = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = new Rectangle(System.Drawing.Point.Empty, frame.Size);
            double? expectedIconWidth = null;
            if (_readCalibration is not null)
            {
                if (BuffHudConfigurationReader.ResolveAutomatic(_readCalibration(), frame.Size, capturedAt) is not { } layout)
                {
                    LastDiagnostic = "Buff-Bereich in der BDO-Konfiguration nicht verfügbar. Eine aktuelle Aufnahme und eine sichtbare, gespeicherte Buffleiste sind erforderlich.";
                    return null;
                }
                bounds = layout.Region;
                expectedIconWidth = 32 * layout.Scale;
            }
            LoadIcons();
            using var color = CompanionFrameDecoder.Decode(frame, bounds);
            var matches = FindIcons(color, cancellationToken, expectedIconWidth);
            if (matches.Count == 0)
            {
                LastDiagnostic = "Automatische Buff-Erkennung: Noch kein unterstütztes Symbol mit Restzeit gefunden.";
                return null;
            }
            var (candidates, unknown) = ClassifyMatches(matches);
            var observations = new List<BuffObservation>();
            foreach (var match in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = _catalog.DefinitionFor(match.Template);
                var icon = _icons.Single(icon => ReferenceEquals(icon.Template, match.Template));
                var scale = (double)match.Bounds.Width / icon.Color.Width;
                var timer = match.Template.TimerRegion;
                var region = new Rectangle(match.Bounds.X + (int)Math.Round(timer.X * scale),
                    match.Bounds.Y + (int)Math.Round(timer.Y * scale),
                    (int)Math.Round(timer.Width * scale), (int)Math.Round(timer.Height * scale));
                if (!BuffHudConfigurationReader.Contains(new(color.Width, color.Height), region)) { unknown.Add(definition.Id); continue; }
                (TimeSpan Remaining, TimeSpan Precision)? reading;
                try { reading = _timerReader.ReadTimer(color, region, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { reading = null; }
                if (reading is not { } value || value.Remaining <= TimeSpan.Zero || value.Remaining > definition.Duration)
                {
                    unknown.Add(definition.Id);
                    continue;
                }
                observations.Add(new(definition.Id, value.Remaining, value.Precision)
                {
                    // Count confirmed Harmony refreshes, including visible party effects.
                    ConsumptionAttributionConfirmed = definition.Category == "Harmony Draughts",
                });
            }
            observations.RemoveAll(observation => unknown.Contains(observation.BuffId));
            LastDiagnostic = observations.Count > 0
                ? _readCalibration is null ? "Automatische Buff-Erkennung aktiv · Symbole und Restzeiten erkannt."
                    : "Automatische Buff-Erkennung aktiv · BDO-Buffbereich und Restzeiten erkannt."
                : "Automatische Buff-Erkennung: Symbole gefunden, Restzeit oder Zuordnung noch unklar.";
            return new(observations.DistinctBy(observation => observation.BuffId).ToArray()) { UnknownBuffIds = unknown.ToArray() };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            LastDiagnostic = "Automatische Buff-Erkennung vorübergehend nicht verfügbar.";
            return null;
        }
    }

    internal (IReadOnlyList<Detection> Accepted, HashSet<string> Unknown) ClassifyMatches(IReadOnlyList<Detection> matches)
    {
        // Discard dominated lookalikes before they can invalidate a clear match
        // of the same identity at a different position.
        var candidates = matches.Where(match => !matches.Any(other =>
            other.Template.GroupId != match.Template.GroupId && Overlap(other.Bounds, match.Bounds) &&
            other.Score - match.Score > AmbiguityMargin(other.Score))).ToArray();
        var accepted = new List<Detection>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in candidates.Where(match => match.Score >= match.Template.MinimumSimilarity))
        {
            var definition = _catalog.DefinitionFor(match.Template);
            if (candidates.Any(other => !ReferenceEquals(other, match) &&
                (_catalog.DefinitionFor(other.Template).Id == definition.Id && !Overlap(other.Bounds, match.Bounds)
                    && other.Score >= other.Template.MinimumSimilarity
                || other.Template.GroupId != match.Template.GroupId && Overlap(other.Bounds, match.Bounds)
                    && other.Score >= match.Score - AmbiguityMargin(match.Score))))
                unknown.Add(definition.Id);
            else accepted.Add(match);
        }
        return (accepted, unknown);
    }

    private void LoadIcons()
    {
        if (_loaded) return;
        var pending = new List<Icon>();
        try
        {
            foreach (var template in _catalog.Templates)
            {
                using var stream = _catalog.OpenIcon(template) ?? throw new InvalidDataException(template.IconPath);
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var color = DecodeIcon(bytes.ToArray());
                if (color.Empty() || color.Width is < 8 or > 256 || color.Height is < 8 or > 256)
                { color.Dispose(); throw new InvalidDataException(template.IconPath); }
                var gray = new Mat();
                try { Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY); }
                catch { color.Dispose(); gray.Dispose(); throw; }
                pending.Add(new(template, color, gray));
            }
        }
        catch { foreach (var icon in pending) icon.Dispose(); throw; }
        _icons.AddRange(pending);
        _loaded = true;
    }

    internal static Mat DecodeIcon(byte[] encoded)
    {
        using var original = Cv2.ImDecode(encoded, ImreadModes.Unchanged);
        if (original.Type() != MatType.CV_8UC4)
            return Cv2.ImDecode(encoded, ImreadModes.Color);

        // Item-style HUD textures contain arbitrary RGB outside their visible
        // artwork. Render their alpha against the neutral dark slot background
        // before either geometry or color comparison, just as the game does.
        // In particular, do not match the hidden colors of transparent pixels.
        var color = new Mat(original.Size(), MatType.CV_8UC3);
        var width = original.Width;
        var height = original.Height;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = original.At<Vec4b>(y, x);
            var alpha = pixel.Item3 / 255d;
            color.Set(y, x, new Vec3b(
                (byte)Math.Round(pixel.Item0 * alpha + 32 * (1 - alpha)),
                (byte)Math.Round(pixel.Item1 * alpha + 32 * (1 - alpha)),
                (byte)Math.Round(pixel.Item2 * alpha + 32 * (1 - alpha))));
        }
        return color;
    }

    internal IReadOnlyList<Detection> FindIcons(Mat frame, CancellationToken token, double? expectedIconWidth = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        // Match only exact pixels, including countdowns. New capture timestamps and
        // timer reads still pass through ReadFrame even when icon matching is reusable.
        if (_matchedFrame is not null && _matchedExpectedIconWidth == expectedIconWidth &&
            _matchedFrame.Type() == frame.Type() && _matchedFrame.Size() == frame.Size() &&
            Cv2.Norm(_matchedFrame, frame, NormTypes.INF) == 0)
        {
            token.ThrowIfCancellationRequested();
            return _matchedDetections!;
        }
        LoadIcons();
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        var downscale = Math.Min(1, 1024d / frame.Width);
        using var small = new Mat();
        Cv2.Resize(gray, small, new OpenCvSharp.Size(), downscale, downscale, InterpolationFlags.Area);
        var detections = new List<Detection>();
        Parallel.ForEach(_icons, new ParallelOptions
        {
            CancellationToken = token, MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount),
        }, icon =>
        {
            var seeds = new List<(Rectangle Bounds, double Score)>();
            // BDO renders every slot at 32 layout pixels regardless of texture size.
            // A bound configuration supplies the scale; standalone image diagnostics
            // retain the broader search when no saved layout is available.
            var widths = new List<double>();
            if (expectedIconWidth is { } expected)
                widths.AddRange(new[] { .94, .97, 1, 1.03, 1.06 }.Select(scale => expected * scale));
            else
            {
                // Fine-detail artwork needs closely spaced probes.
                for (var width = 18d; width <= 100; width *= 1.04) widths.Add(width);
                widths.AddRange(new[] { .75, 1, 1.25, 1.5, 2 }.Select(scale => icon.Color.Width * scale));
            }
            var visitedSizes = new HashSet<int>();
            foreach (var width in widths.Where(width => width is >= 16 and <= 100))
            {
                token.ThrowIfCancellationRequested();
                var w = Math.Max(6, (int)Math.Round(width * downscale));
                if (!visitedSizes.Add(w)) continue;
                var h = Math.Max(6, (int)Math.Round(w * (double)icon.Gray.Height / icon.Gray.Width));
                if (w >= small.Width || h >= small.Height) continue;
                using var scaled = icon.GraySizes.Acquire(w, h, InterpolationFlags.Area);
                using var scores = new Mat();
                Cv2.MatchTemplate(small, scaled, scores, TemplateMatchModes.CCoeffNormed);
                for (var occurrence = 0; occurrence < 3; occurrence++)
                {
                    Cv2.MinMaxLoc(scores, out _, out var score, out _, out var point);
                    // Downsampling can shift a tiny symbol by half a pixel.
                    // Coarse evidence only locates candidates; full-resolution
                    // color scoring still decides their identity.
                    if (!double.IsFinite(score) || score < (w <= 12 ? .65 : .72)) break;
                    seeds.Add((new((int)Math.Round(point.X / downscale), (int)Math.Round(point.Y / downscale),
                        (int)Math.Round(w / downscale), (int)Math.Round(h / downscale)), score));
                    var exclusion = Rect.Intersect(new(point.X - w / 2, point.Y - h / 2, w, h), new(0, 0, scores.Width, scores.Height));
                    using var suppress = new Mat(scores, exclusion);
                    suppress.SetTo(Scalar.All(-1));
                }
            }
            var refined = new List<Detection>();
            foreach (var seed in seeds.OrderByDescending(seed => seed.Score).Take(24))
            {
                token.ThrowIfCancellationRequested();
                if (refined.Any(match => Overlap(match.Bounds, seed.Bounds))) continue;
                var padding = Math.Max(4, (int)Math.Ceiling(3 / downscale));
                var area = Rectangle.Intersect(new(seed.Bounds.X - padding, seed.Bounds.Y - padding,
                    seed.Bounds.Width + padding * 2, seed.Bounds.Height + padding * 2), new(0, 0, frame.Width, frame.Height));
                using var crop = new Mat(frame, new Rect(area.X, area.Y, area.Width, area.Height));
                Detection? best = null;
                for (var w = Math.Max(12, seed.Bounds.Width - padding); w <= seed.Bounds.Width + padding; w++)
                {
                    token.ThrowIfCancellationRequested();
                    var h = (int)Math.Round(w * (double)icon.Color.Height / icon.Color.Width);
                    if (w > crop.Width || h > crop.Height) continue;
                    using var scaled = icon.ColorSizes.Acquire(w, h, InterpolationFlags.Area);
                    using var scores = new Mat();
                    Cv2.MatchTemplate(crop, scaled, scores, TemplateMatchModes.CCoeffNormed);
                    Cv2.MinMaxLoc(scores, out _, out var geometryScore, out _, out var point);
                    if (!double.IsFinite(geometryScore) || geometryScore < .70) continue;
                    using var observed = new Mat(crop, new Rect(point.X, point.Y, w, h));
                    var score = BuffIconSimilarity.Score(observed, icon.Color);
                    // Keep close competitors even just below the acceptance threshold:
                    // .905 versus .899 is ambiguous, not a confirmed .905 identity.
                    var candidateFloor = icon.Template.MinimumSimilarity - AmbiguityMargin(icon.Template.MinimumSimilarity);
                    if (double.IsFinite(score) && score >= candidateFloor && (best is null || score > best.Score))
                        best = new(icon.Template, new(area.X + point.X, area.Y + point.Y, w, h), score);
                }
                if (best is not null) refined.Add(best);
            }
            lock (detections) detections.AddRange(refined);
        });
        // Alternate exemplars of the same effect only produce one observation.
        var selected = new List<Detection>();
        foreach (var detection in detections.OrderByDescending(match => match.Score))
            if (!selected.Any(other => other.Template.GroupId == detection.Template.GroupId && Overlap(other.Bounds, detection.Bounds)))
                selected.Add(detection);
        token.ThrowIfCancellationRequested();
        var result = selected.AsReadOnly();
        var rememberedFrame = frame.Total() * frame.ElemSize() <= MaximumMatchedFrameBytes ? frame.Clone() : null;
        _matchedFrame?.Dispose();
        _matchedFrame = rememberedFrame;
        _matchedExpectedIconWidth = expectedIconWidth;
        _matchedDetections = rememberedFrame is null ? null : result;
        return result;
    }

    private static bool Overlap(Rectangle a, Rectangle b)
    {
        var intersection = Rectangle.Intersect(a, b);
        return (long)intersection.Width * intersection.Height > .35 * Math.Min((long)a.Width * a.Height, (long)b.Width * b.Height);
    }

    private static double AmbiguityMargin(double score) => Math.Max(.012, .20 * (1 - score));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _matchedFrame?.Dispose();
        _matchedFrame = null;
        _matchedDetections = null;
        _timerReader.Dispose();
        foreach (var icon in _icons) icon.Dispose();
        _icons.Clear();
    }
}
