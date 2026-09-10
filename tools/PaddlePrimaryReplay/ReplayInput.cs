using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace PaddlePrimaryReplay;

internal sealed record ReplayOptions(string Recording, string Output, string Mode, string Data,
    float UiScale, CompanionFontType Font, string Language, int? MaximumFrames)
{
    public const string Usage = "PaddlePrimaryReplay --recording <directory> --output <new-directory> " +
        "--mode windows|paddle [--data <repo-data-directory>] [--scale 1.49] " +
        "[--font StrongSword|DejaVu|CabinDroid] [--language en|de] [--max-frames N]";

    public static ReplayOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException(Usage);
        }
        string Required(string key) => values.Remove(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing {key}. {Usage}");
        string Optional(string key, string fallback) => values.Remove(key, out var value) ? value : fallback;
        var recording = Path.GetFullPath(Required("--recording"));
        var output = Path.GetFullPath(Required("--output"));
        var mode = Required("--mode");
        var data = Path.GetFullPath(Optional("--data", Path.Combine(AppContext.BaseDirectory, "data")));
        var scale = float.Parse(Optional("--scale", "1.49"), CultureInfo.InvariantCulture);
        var font = Enum.Parse<CompanionFontType>(Optional("--font", "StrongSword"), ignoreCase: true);
        var language = Optional("--language", "en");
        int? maximum = values.Remove("--max-frames", out var limit) ? int.Parse(limit, CultureInfo.InvariantCulture) : null;
        if (values.Count > 0 || mode is not ("windows" or "paddle") ||
            language is not ("en" or "de") || !Enum.IsDefined(font) ||
            !float.IsFinite(scale) || scale is < .5f or > 3f || maximum is <= 0)
            throw new ArgumentException(Usage);
        if (!Directory.Exists(recording)) throw new DirectoryNotFoundException(recording);
        if (Path.GetRelativePath(recording, output) is var relative &&
            (relative == "." || !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                relative != ".." && !Path.IsPathRooted(relative)))
            throw new ArgumentException("The output must be outside the original recording directory.");
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("The output directory must be new or empty; cached results are never overwritten.");
        return new(recording, output, mode, data, scale, font, language, maximum);
    }
}

internal sealed record InputFileProof(string Name, long Bytes, string Sha256);

internal sealed class ReplayInput
{
    private readonly Dictionary<string, InputFileProof> _files = new(StringComparer.OrdinalIgnoreCase);
    public ReplayOptions Options { get; }
    public LootDiagnosticHeader Header { get; }
    public IReadOnlyList<LootDiagnosticEntry> Entries { get; }
    public IReadOnlyList<LootDiagnosticEntry> Selected { get; }
    public IReadOnlyCollection<InputFileProof> Files => _files.Values;
    public int FrameCount => Selected.Count(entry => entry.Kind == "frame");
    public bool IsPartial => FrameCount != Entries.Count(entry => entry.Kind == "frame");

    public ReplayInput(ReplayOptions options)
    {
        Options = options;
        var journalPath = Path.Combine(options.Recording, LootDiagnosticFormat.RecordingFileName);
        var bytes = File.ReadAllBytes(journalPath);
        AddProof(LootDiagnosticFormat.RecordingFileName, bytes);
        using var reader = new StreamReader(new MemoryStream(bytes));
        Header = JsonSerializer.Deserialize<LootDiagnosticHeader>(reader.ReadLine() ?? "", LootDiagnosticFormat.JsonOptions)
            ?? throw new InvalidDataException("Missing diagnostic header.");
        if (Header.Kind != "header" || Header.FormatVersion != LootDiagnosticFormat.Version)
            throw new InvalidDataException("Unsupported source recording schema.");
        var entries = new List<LootDiagnosticEntry>();
        DateTimeOffset? previousTime = null;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)
                ?? throw new InvalidDataException("Invalid source entry.");
            if (entry.Sequence != entries.Count + 1 || previousTime is { } previous && entry.Timestamp < previous ||
                entry.Kind is not ("frame" or "complete"))
                throw new InvalidDataException("Source sequence or timestamp continuity is invalid.");
            previousTime = entry.Timestamp;
            entries.Add(entry);
        }
        Entries = entries;
        var selected = new List<LootDiagnosticEntry>();
        var frames = 0;
        foreach (var entry in entries)
        {
            if (entry.Kind == "frame" && options.MaximumFrames is { } max && frames == max) break;
            selected.Add(entry);
            if (entry.Kind == "frame") frames++;
        }
        Selected = selected;
        if (FrameCount == 0) throw new InvalidDataException("The recording has no frames to process.");
        var summary = Path.Combine(options.Recording, LootDiagnosticFormat.CountSummaryFileName);
        if (File.Exists(summary)) AddProof(LootDiagnosticFormat.CountSummaryFileName, File.ReadAllBytes(summary));
        foreach (var entry in Selected.Where(entry => entry.Kind == "frame"))
        {
            if (entry.IsHdr is null || entry.IsToneMapped is null)
                throw new InvalidDataException($"Frame {entry.Sequence} lacks explicit HDR/tone-map provenance.");
            _ = Crop(entry, "normal");
            if (entry.RareEnabled) _ = Crop(entry, "rare");
            if (entry.RareEnabled != Selected.First(item => item.Kind == "frame").RareEnabled)
                throw new InvalidDataException("Rare channel configuration changes in this recording.");
            foreach (var crop in entry.Crops)
            {
                if (crop.Source is not ("normal" or "rare"))
                    throw new InvalidDataException("Only recordings with normal/rare crop inputs are supported.");
                var path = CropPath(crop.FileName);
                if (!_files.ContainsKey(crop.FileName)) AddProof(crop.FileName, File.ReadAllBytes(path));
            }
        }
    }

    public LootDiagnosticCrop Crop(LootDiagnosticEntry entry, string source) =>
        entry.Crops.SingleOrDefault(crop => crop.Source == source)
            ?? throw new InvalidDataException($"Frame {entry.Sequence} has no {source} crop.");

    public byte[] ReadCrop(LootDiagnosticCrop crop)
    {
        var bytes = File.ReadAllBytes(CropPath(crop.FileName));
        if (Hash(bytes) != _files[crop.FileName].Sha256)
            throw new InvalidDataException($"Source crop changed during replay: {crop.FileName}");
        return bytes;
    }

    public void VerifyOriginalFilesUnchanged()
    {
        foreach (var proof in _files.Values)
            if (Hash(File.ReadAllBytes(Path.Combine(Options.Recording, proof.Name))) != proof.Sha256)
                throw new InvalidDataException($"Original input changed during replay: {proof.Name}");
    }

    private string CropPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name ||
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Crop references must be plain PNG filenames.");
        return Path.Combine(Options.Recording, name);
    }

    private void AddProof(string name, byte[] bytes) => _files.Add(name, new(name, bytes.LongLength, Hash(bytes)));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed class ReplayGeometry
{
    public CompanionCalibration Calibration { get; }
    public Rectangle NormalPanel { get; }
    public Rectangle? RareBand { get; }
    public IReadOnlyList<Rectangle> Slots { get; }
    public string Assumption => "Recorded crop pixels are placed unscaled into disjoint virtual bands. " +
        "UI scale/font are explicit inputs; reduced normal width is reproduced using a left-clipped anchor. " +
        "Only these crop pixels are analyzed; no game configuration or full game frame is read.";

    public ReplayGeometry(ReplayInput input)
    {
        var first = input.Selected.First(entry => entry.Kind == "frame");
        var normal = input.Crop(first, "normal");
        var rare = first.RareEnabled ? input.Crop(first, "rare") : null;
        var scale = input.Options.UiScale;
        var rightExtent = checked((int)MathF.Ceiling(260 * scale));
        var normalLeftExtent = checked((int)MathF.Floor(165 * scale));
        var halfHeight = checked((int)MathF.Floor(150 * scale));
        var normalX = normal.Width - rightExtent;
        if (normalX < 0 || normalX > normalLeftExtent)
            throw new InvalidDataException("Normal crop width cannot be reconstructed at this UI scale.");
        var rareLeft = normal.Width + 64;
        var rareX = rareLeft + checked((int)MathF.Floor(125 * scale));
        var frameWidth = rare is null ? normal.Width : rareLeft + rare.Width;
        Calibration = new("", "", "", normalX, halfHeight, frameWidth, normal.Height,
            scale, input.Options.Font, 0, false, HasRareLootAnchor: rare is not null,
            RareLootAnchorX: rareX, RareLootAnchorY: halfHeight);
        NormalPanel = CompanionNormalLootGeometry.CalculatePanelBounds(Calibration);
        RareBand = rare is null ? null : CompanionNormalLootGeometry.CalculateRareBandCrop(Calibration);
        Slots = CompanionNormalLootGeometry.CalculateSlotCrops(Calibration);
        if (NormalPanel.Size != new System.Drawing.Size(normal.Width, normal.Height) ||
            rare is not null && RareBand?.Size != new System.Drawing.Size(rare.Width, rare.Height) ||
            RareBand is { } band && NormalPanel.IntersectsWith(band))
            throw new InvalidDataException("Recorded dimensions do not match reconstructed production geometry.");
        var nativeRows = Slots.Select(slot => slot.Y - NormalPanel.Y).ToHashSet();
        foreach (var entry in input.Selected.Where(entry => entry.Kind == "frame"))
        {
            if (input.Crop(entry, "normal").Width != normal.Width || input.Crop(entry, "normal").Height != normal.Height ||
                rare is not null && (input.Crop(entry, "rare").Width != rare.Width || input.Crop(entry, "rare").Height != rare.Height))
                throw new InvalidDataException("Crop dimensions change during the recording; a new geometry is required.");
            if (entry.Observations.Any(row => row.Source == LootSource.Normal && row.NativeY is { } y && !nativeRows.Contains(y)))
                throw new InvalidDataException("Recorded normal-row Y positions do not match the supplied UI scale.");
        }
    }

    public Bitmap ReconstructVerifiedFrame(ReplayInput input, LootDiagnosticEntry entry)
    {
        var frame = new Bitmap(Calibration.ScreenWidth, Calibration.ScreenHeight, PixelFormat.Format24bppRgb);
        try
        {
            var crops = new List<(byte[] Bytes, Rectangle Bounds)> { (input.ReadCrop(input.Crop(entry, "normal")), NormalPanel) };
            if (RareBand is { } rareBand) crops.Add((input.ReadCrop(input.Crop(entry, "rare")), rareBand));
            var pixels = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                if (pixels.Stride <= 0) throw new InvalidOperationException("Expected a top-down allocated replay bitmap.");
                using var destination = Mat.FromPixelData(frame.Height, frame.Width,
                    MatType.CV_8UC3, pixels.Scan0, pixels.Stride);
                destination.SetTo(Scalar.Black);
                foreach (var (bytes, bounds) in crops)
                {
                    // DXGI-derived PNGs can carry irrelevant alpha bytes. The
                    // production decoder reads BGR directly; GDI composition
                    // would instead blend those pixels and corrupt the replay.
                    using var image = Cv2.ImDecode(bytes, ImreadModes.Color);
                    if (image.Width != bounds.Width || image.Height != bounds.Height)
                        throw new InvalidDataException("PNG size disagrees with diagnostic metadata.");
                    using var target = new Mat(destination, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                    image.CopyTo(target);
                }
            }
            finally { frame.UnlockBits(pixels); }
            using var decoded = CompanionBitmapDecoder.Instance.Decode(frame);
            foreach (var (bytes, bounds) in crops)
            {
                using var expected = Cv2.ImDecode(bytes, ImreadModes.Color);
                using var actual = new Mat(decoded, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                if (Cv2.Norm(expected, actual, NormTypes.INF) != 0)
                    throw new InvalidDataException($"Reconstructed pixels differ in source frame {entry.Sequence}.");
            }
            return frame;
        }
        catch { frame.Dispose(); throw; }
    }
}
