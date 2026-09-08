using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>Local PP-OCRv6 Small recognition of an already located text line.
/// One CPU session per worker; no detector, Python process, downloads or GPU required.</summary>
public sealed class PaddleLootOcrRecognizer : ISecondaryLootOcrRecognizer
{
    public const string ModelSha256 = "5435FD747C9E0EFE15A96D0B378D5BD157E9492ED8FD80EDF08F30D02FA24634";
    private readonly object _gate = new();
    private readonly InferenceSession _session;
    private readonly string[] _characters;
    private readonly string _inputName;
    private bool _disposed;

    public string BackendName => "paddle-pp-ocrv6-small-onnx";
    public string LanguageTag { get; }

    private PaddleLootOcrRecognizer(string languageTag, string directory)
    {
        LanguageTag = CultureInfo.GetCultureInfo(languageTag).Name;
        var path = Path.Combine(directory, "inference.onnx");
        if (!File.Exists(path)) throw new FileNotFoundException("Das lokale Paddle-OCR-Modell fehlt.", path);
        _characters = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(directory, "characters.json")))
            ?? throw new InvalidDataException("Der Paddle-OCR-Zeichensatz fehlt.");
        if (_characters.Length != 18_710 || _characters[0] != "blank" || _characters[^1] != " " ||
            _characters.Any(string.IsNullOrEmpty))
            throw new InvalidDataException("Der Zeichensatz passt nicht zum PP-OCRv6-Small-Modell.");
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 2, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(path, options);
        try
        {
            if (_session.InputNames.Count != 1 || _session.OutputNames.Count != 1)
                throw new InvalidDataException("Unerwartete Paddle-OCR-Modellschnittstelle.");
            _inputName = _session.InputNames[0];
        }
        catch { _session.Dispose(); throw; }
    }

    public static PaddleLootOcrRecognizer Create(string languageTag, string? dataDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        // The model is multilingual. The selected language still accompanies diagnostics;
        // supported game languages and their item aliases are controlled by the app catalog.
        return new(languageTag, Path.GetFullPath(dataDirectory ??
            Path.Combine(AppContext.BaseDirectory, "data", "ocr", "paddle-v6-small")));
    }

    public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Empty() || image.Depth() != MatType.CV_8U || image.Channels() is not (1 or 3 or 4) ||
            image.Width > 8192 || image.Height > 8192 || (long)image.Width * image.Height > 4_194_304)
            throw new ArgumentException("OCR requires a non-empty, bounded 8-bit row image.", nameof(image));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var (pixels, width) = PrepareInput(image);
            using var input = OrtValue.CreateTensorValueFromMemory(pixels, [1, 3, 48, width]);
            using var options = new RunOptions();
            // Terminate requests cancellation inside ONNX Runtime, including during native inference.
            using var registration = cancellationToken.Register(() => options.Terminate = true);
            try
            {
                using var output = _session.Run(options, new Dictionary<string, OrtValue> { [_inputName] = input },
                    _session.OutputNames);
                cancellationToken.ThrowIfCancellationRequested();
                var shape = output[0].GetTensorTypeAndShape().Shape;
                if (shape.Length != 3 || shape[0] != 1 || shape[2] != _characters.Length)
                    throw new InvalidDataException("Paddle-OCR-Ausgabe und Zeichensatz stimmen nicht überein.");
                return Decode(output[0].GetTensorDataAsSpan<float>(), _characters);
            }
            catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
            { throw new OperationCanceledException(cancellationToken); }
        }
    }

    internal static (float[] Pixels, int Width) PrepareInput(Mat image)
    {
        var ratio = 48d * image.Width / image.Height;
        var width = Math.Clamp((int)ratio, 320, 3200);
        var resizedWidth = Math.Min(width, (int)Math.Ceiling(ratio));
        using var bgr = new Mat();
        if (image.Channels() == 1) Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
        else if (image.Channels() == 4) Cv2.CvtColor(image, bgr, ColorConversionCodes.BGRA2BGR);
        else image.CopyTo(bgr);
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new OpenCvSharp.Size(resizedWidth, 48), interpolation: InterpolationFlags.Linear);
        var pixels = new float[3 * 48 * width]; // Padding is zero AFTER normalization, per upstream.
        for (var y = 0; y < 48; y++)
            for (var x = 0; x < resizedWidth; x++)
            {
                var pixel = resized.At<Vec3b>(y, x);
                for (var channel = 0; channel < 3; channel++)
                    pixels[channel * 48 * width + y * width + x] = pixel[channel] / 127.5f - 1;
            }
        return (pixels, width);
    }

    internal static SecondaryLootOcrResult Decode(ReadOnlySpan<float> probabilities, IReadOnlyList<string> characters)
    {
        if (characters.Count < 2 || probabilities.Length % characters.Count != 0)
            throw new InvalidDataException("Ungültige CTC-Ausgabe.");
        var text = new StringBuilder();
        var previous = -1;
        var score = 0d;
        var count = 0;
        for (var offset = 0; offset < probabilities.Length; offset += characters.Count)
        {
            var best = 0;
            for (var i = 1; i < characters.Count; i++)
                if (probabilities[offset + i] > probabilities[offset + best]) best = i;
            if (best != 0 && best != previous)
            {
                text.Append(characters[best]);
                score += probabilities[offset + best];
                count++;
            }
            previous = best;
        }
        // Recognition-only CTC does not supply trustworthy word boxes. Do not fabricate them.
        return new(text.ToString().Trim(), count == 0 ? 0 : (float)Math.Clamp(score / count, 0, 1),
            new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
