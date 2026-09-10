using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BrandAssets;

internal static class Program
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];
    private static readonly int[] MsixTargetSizes = [16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256];
    private static readonly (string Name, int Size)[] MsixAssets =
    [
        ("StoreLogo.png", 50),
        ("Square44x44Logo.png", 44),
        ("Square150x150Logo.png", 150),
        .. MsixTargetSizes.SelectMany(static size => new (string Name, int Size)[]
        {
            ($"Square44x44Logo.targetsize-{size}.png", size),
            ($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size),
            ($"Square44x44Logo.targetsize-{size}_altform-lightunplated.png", size)
        })
    ];
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                SelfTest();
                return 0;
            }
            if (args.Length == 0 || args.Any(static value => value is "--help" or "-h"))
            {
                Console.WriteLine("BrandAssets input.png output.ico [header.png] [--force]");
                Console.WriteLine("BrandAssets --msix input.png outputDirectory (directory must be empty)");
                Console.WriteLine("BrandAssets --self-test");
                Console.WriteLine("Requires a square PNG containing visible and transparent pixels. No UI is opened.");
                return args.Length == 0 ? 2 : 0;
            }
            if (args[0] == "--msix")
            {
                if (args.Length != 3 || args.Skip(1).Any(static arg => arg.StartsWith("--", StringComparison.Ordinal)))
                    throw new ArgumentException("Expected --msix input.png outputDirectory.");
                PackageMsix(args[1], args[2]);
                return 0;
            }

            var force = args.Contains("--force", StringComparer.Ordinal);
            var paths = args.Where(static arg => arg != "--force").ToArray();
            if (paths.Length is < 2 or > 3 || paths.Any(static arg => arg.StartsWith("--", StringComparison.Ordinal)))
                throw new ArgumentException("Expected input.png output.ico [header.png] [--force].");
            Package(paths[0], paths[1], paths.Length == 3 ? paths[2] : null, force);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
            UnauthorizedAccessException or ExternalException or InvalidDataException)
        {
            Console.Error.WriteLine("Brand asset packaging failed: " + exception.Message);
            return 1;
        }
    }

    private static void Package(string inputPath, string iconPath, string? headerPath, bool force)
    {
        inputPath = Path.GetFullPath(inputPath);
        iconPath = Path.GetFullPath(iconPath);
        headerPath = headerPath is null ? null : Path.GetFullPath(headerPath);
        if (!string.Equals(Path.GetExtension(inputPath), ".png", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(iconPath), ".ico", StringComparison.OrdinalIgnoreCase) ||
            headerPath is not null && !string.Equals(Path.GetExtension(headerPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input/header must be .png and icon output must be .ico.");
        var allPaths = new[] { inputPath, iconPath, headerPath }.Where(static path => path is not null).ToArray();
        if (allPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allPaths.Length)
            throw new ArgumentException("Input and output paths must be distinct; the source is never overwritten.");
        foreach (var output in new[] { iconPath, headerPath }.Where(static path => path is not null))
            if (!force && File.Exists(output))
                throw new IOException($"Output already exists: {output}. Use --force only to replace generated outputs.");
        var sourceBytes = ReadSourceBytes(inputPath);

        using var stream = new MemoryStream(sourceBytes, writable: false);
        using var source = new Bitmap(stream);
        ValidateSource(source);
        var frames = IconSizes.Select(size => ResizePng(source, size)).ToArray();
        var icon = BuildIcon(frames);
        VerifyIcon(icon);
        WriteOutput(iconPath, icon, force);
        if (headerPath is not null)
            WriteOutput(headerPath, frames[Array.IndexOf(IconSizes, 128)], force);
        Console.WriteLine($"ICO: {iconPath} ({string.Join(", ", IconSizes)} px; {icon.Length:N0} bytes)");
        if (headerPath is not null) Console.WriteLine($"Header: {headerPath} (128 x 128 PNG)");
        Console.WriteLine("Source PNG untouched; alpha retained. Only resampling/container packaging was performed.");
    }

    private static void PackageMsix(string inputPath, string outputDirectory)
    {
        inputPath = Path.GetFullPath(inputPath);
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (File.Exists(outputDirectory) ||
            Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new IOException("MSIX output directory must be empty or not yet exist.");

        var sourceBytes = ReadSourceBytes(inputPath);
        using var stream = new MemoryStream(sourceBytes, writable: false);
        using var source = new Bitmap(stream);
        ValidateSource(source);
        var assets = MsixAssets.Select(asset => (asset.Name, asset.Size, Bytes: ResizePng(source, asset.Size))).ToArray();
        foreach (var asset in assets)
        {
            var outputPath = Path.Combine(outputDirectory, asset.Name);
            WriteOutput(outputPath, asset.Bytes, force: false);
            Console.WriteLine($"MSIX: {outputPath} ({asset.Size} x {asset.Size} PNG)");
        }
        Console.WriteLine("Source PNG untouched; alpha retained. Only resampling was performed.");
    }

    private static byte[] ReadSourceBytes(string inputPath)
    {
        if (!string.Equals(Path.GetExtension(inputPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input must be .png.");
        var info = new FileInfo(inputPath);
        if (!info.Exists || info.Length is < 24 or > 64 * 1024 * 1024)
            throw new InvalidDataException("Input PNG is missing, empty or exceeds 64 MiB.");
        var sourceBytes = File.ReadAllBytes(inputPath);
        if (!sourceBytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new InvalidDataException("Input does not have a PNG signature.");
        return sourceBytes;
    }

    private static void ValidateSource(Bitmap source)
    {
        if (source.Width != source.Height || source.Width is < 16 or > 8192)
            throw new InvalidDataException("Source must be square and between 16 and 8192 pixels wide.");
        var hasTransparent = false;
        var hasVisible = false;
        for (var y = 0; y < source.Height && (!hasTransparent || !hasVisible); y++)
        for (var x = 0; x < source.Width && (!hasTransparent || !hasVisible); x++)
        {
            var alpha = source.GetPixel(x, y).A;
            hasTransparent |= alpha < 255;
            hasVisible |= alpha > 0;
        }
        if (!hasTransparent || !hasVisible)
            throw new InvalidDataException("Source must contain both visible and transparent pixels; an opaque background is not removed automatically.");
    }

    private static byte[] ResizePng(Bitmap source, int size)
    {
        using var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(target))
        using (var attributes = new ImageAttributes())
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0,
                source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        using var output = new MemoryStream();
        target.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static byte[] BuildIcon(IReadOnlyList<byte[]> frames)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon (not cursor)
        writer.Write((ushort)frames.Count);
        var offset = checked(6 + frames.Count * 16);
        for (var index = 0; index < frames.Count; index++)
        {
            var size = IconSizes[index];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); // truecolor: no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // RGBA
            writer.Write((uint)frames[index].Length);
            writer.Write((uint)offset);
            offset = checked(offset + frames[index].Length);
        }
        foreach (var frame in frames) writer.Write(frame);
        return output.ToArray();
    }

    private static void VerifyIcon(byte[] bytes, bool describe = false)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(input);
        Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Invalid ICO header.");
        Require(reader.ReadUInt16() == IconSizes.Length, "Wrong ICO image count.");
        var entries = new List<(int Size, uint Length, uint Offset)>();
        var expectedOffset = 6 + IconSizes.Length * 16;
        foreach (var expectedSize in IconSizes)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            Require((width == 0 ? 256 : width) == expectedSize && height == width, "Wrong ICO dimensions.");
            Require(reader.ReadByte() == 0 && reader.ReadByte() == 0, "Unexpected palette/reserved byte.");
            Require(reader.ReadUInt16() == 1 && reader.ReadUInt16() == 32, "Wrong ICO color format.");
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            Require(offset == expectedOffset && (long)offset + length <= bytes.Length, "Invalid ICO payload offset.");
            entries.Add((expectedSize, length, offset));
            expectedOffset = checked((int)(offset + length));
        }
        Require(expectedOffset == bytes.Length, "Unexpected bytes after last ICO frame.");
        foreach (var (size, length, offset) in entries)
        {
            using var payload = new MemoryStream(bytes, (int)offset, (int)length, writable: false);
            Require(bytes.AsSpan((int)offset, PngSignature.Length).SequenceEqual(PngSignature), "ICO payload is not PNG.");
            using var frame = new Bitmap(payload);
            Require(frame.Width == size && frame.Height == size, "PNG payload dimensions disagree with ICO entry.");
            Require(Image.IsAlphaPixelFormat(frame.PixelFormat), "PNG payload lost its alpha channel.");
            if (describe) Console.WriteLine($"Verified ICO entry: {size}x{size}, RGBA PNG, offset={offset}, bytes={length}");
        }
        // Exercise the Windows/.NET ICO reader, independently of the entry parser.
        using var iconStream = new MemoryStream(bytes, writable: false);
        using var icon = new Icon(iconStream, 256, 256);
        Require(icon.Width == icon.Height && IconSizes.Contains(icon.Width),
            $"Windows ICO reader returned an unexpected size: {icon.Width} x {icon.Height}.");
    }

    private static void WriteOutput(string path, byte[] bytes, bool force)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: force);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private static void SelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "grindcrest-brand-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "synthetic-alpha.png");
            var output = Path.Combine(directory, "synthetic.ico");
            var header = Path.Combine(directory, "synthetic-header.png");
            using (var fixture = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            {
                for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++)
                    fixture.SetPixel(x, y, x < 16 || y < 16 || x >= 240 || y >= 240
                        ? Color.Transparent : Color.FromArgb(x < 128 ? 128 : 255, 80, 140, 220));
                fixture.Save(source, ImageFormat.Png);
            }
            var originalBytes = File.ReadAllBytes(source);
            Package(source, output, header, force: false);
            VerifyIcon(File.ReadAllBytes(output), describe: true);
            using (var result = new Bitmap(header))
            {
                Require(result.Width == 128 && result.Height == 128, "Wrong header dimensions.");
                Require(result.GetPixel(0, 0).A == 0, "Transparent background lost.");
                Require(result.GetPixel(32, 64).A == 128, "Semitransparent alpha changed.");
                Require(result.GetPixel(96, 64).A == 255, "Opaque alpha changed.");
            }
            ExpectFailure(() => Package(source, output, header, force: false), "Existing output was not protected.");
            ExpectFailure(() => Package(source, output, source, force: true), "Source overwrite was not rejected.");
            Package(source, output, header, force: true);
            var msixDirectory = Path.Combine(directory, "msix");
            PackageMsix(source, msixDirectory);
            Require(Directory.GetFiles(msixDirectory).Length == 48, "Expected 3 base logos and 45 MSIX target-size variants.");
            foreach (var (name, size) in MsixAssets)
            {
                using var result = new Bitmap(Path.Combine(msixDirectory, name));
                Require(result.Width == size && result.Height == size, $"Wrong MSIX dimensions: {name}.");
                Require(Image.IsAlphaPixelFormat(result.PixelFormat), $"MSIX PNG lost its alpha channel: {name}.");
                Require(result.GetPixel(0, 0).A == 0 && result.GetPixel(size / 4, size / 2).A == 128 &&
                    result.GetPixel(size * 3 / 4, size / 2).A == 255, $"MSIX PNG alpha changed: {name}.");
            }
            ExpectFailure(() => PackageMsix(source, msixDirectory), "Existing MSIX assets were not protected.");
            ExpectFailure(() => PackageMsix(source, directory), "Nonempty MSIX directory was accepted.");
            ExpectFailure(() => PackageMsix(source, source), "MSIX source overwrite was not rejected.");
            Require(originalBytes.SequenceEqual(File.ReadAllBytes(source)), "Original PNG was modified.");
            using (var opaque = new Bitmap(256, 256))
            using (var graphics = Graphics.FromImage(opaque))
            {
                graphics.Clear(Color.Red);
                ExpectFailure(() => ValidateSource(opaque), "Opaque input was accepted.");
            }
            using (var nonsquare = new Bitmap(256, 128))
                ExpectFailure(() => ValidateSource(nonsquare), "Nonsquare input was accepted.");
            using (var invisible = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
                ExpectFailure(() => ValidateSource(invisible), "Fully transparent input was accepted.");
            Console.WriteLine("SELF-TEST PASS: 7 ICO PNG frames, offsets, Windows ICO decode, 128px header, 48 MSIX PNG logos including unplated/lightunplated variants, alpha 0/128/255, unchanged source, overwrite/dimension/alpha guards.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException) { return; }
        throw new InvalidDataException(message);
    }
}
