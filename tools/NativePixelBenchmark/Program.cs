using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BdoGrindTracker.App.Capture;

namespace NativePixelBenchmark;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.VerifyManagedFallback) return VerifyManagedFallback();
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new InvalidOperationException("This benchmark requires Windows x64.");
            if (!NativeDesktopPixelConverter.IsAvailable)
                throw new InvalidOperationException("Native HDR kernel is unavailable. Build the native DLL and copy it to the benchmark output.");

            using (var process = Process.GetCurrentProcess())
                process.PriorityClass = ProcessPriorityClass.BelowNormal;

            var results = new List<ResolutionResult>();
            Console.WriteLine($"HDR RGBA16F -> BGRA8; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}");
            Console.WriteLine("Benchmark process priority: BelowNormal.");
            Console.WriteLine($"{options.Samples} samples, {options.Iterations} conversions per sample, {options.Warmup} warmup conversions per implementation.");
            Console.WriteLine("Pinned buffers and lookup are prepared outside timing; samples alternate implementation order.");
            Console.WriteLine("Timing median and p95 describe batch averages per conversion.");
            foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440), (3840, 2160) })
            {
                var result = RunResolution(width, height, options);
                results.Add(result);
                Console.WriteLine($"{width}x{height}: native {result.Native.WallMedianMs:F3} ms median / {result.Native.WallP95Ms:F3} ms p95; managed {result.Managed.WallMedianMs:F3} / {result.Managed.WallP95Ms:F3}; speedup {result.MedianSpeedup:F2}x");
                Console.WriteLine($"  Process CPU median/p95 per conversion: native {result.Native.CpuMedianMs:F3}/{result.Native.CpuP95Ms:F3} ms; managed {result.Managed.CpuMedianMs:F3}/{result.Managed.CpuP95Ms:F3} ms; thread allocations: native {result.Native.MaxAllocatedBytesPerConversion:F1}, managed {result.Managed.MaxAllocatedBytesPerConversion:F1} B");
            }
            Console.WriteLine("CPU times include the entire benchmark process and have OS timer granularity. These are conversion measurements, not BDO FPS results.");
            if (options.JsonPath is not null)
            {
                var report = new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    runtime = RuntimeInformation.FrameworkDescription,
                    os = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    samples = options.Samples,
                    iterationsPerSample = options.Iterations,
                    warmupConversions = options.Warmup,
                    results,
                };
                File.WriteAllText(options.JsonPath, JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static ResolutionResult RunResolution(int width, int height, Options options)
    {
        var sourceStride = checked(width * 8 + 64);
        var destinationStride = checked(width * 4 + 64);
        var source = GC.AllocateUninitializedArray<byte>(checked(sourceStride * height));
        // Deterministic HDR scene with ordinary positive values, shadows, and highlights.
        // Every half representation, including NaN and infinity, is covered by parity tests.
        var random = new Random(413);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * sourceStride + x * 8;
            for (var channel = 0; channel < 3; channel++)
            {
                var bits = BitConverter.HalfToUInt16Bits((Half)(random.NextSingle() * 12f));
                source[offset + channel * 2] = (byte)bits;
                source[offset + channel * 2 + 1] = (byte)(bits >> 8);
            }
            source[offset + 6] = 0;
            source[offset + 7] = 0x3c;
        }
        var managed = new byte[checked(destinationStride * height)];
        var native = new byte[managed.Length];
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        using var pinnedSource = new PinnedBuffer(source);
        using var pinnedManaged = new PinnedBuffer(managed);
        using var pinnedNative = new PinnedBuffer(native);
        Action managedConversion = () => DesktopPixelConverter.CopyHdrPixelsManaged(pinnedSource.Pointer,
            (uint)sourceStride, pinnedManaged.Pointer, destinationStride, width, height, lookup);
        Action nativeConversion = () =>
        {
            if (!NativeDesktopPixelConverter.TryConvert(pinnedSource.Pointer, (uint)sourceStride,
                    pinnedNative.Pointer, destinationStride, width, height, lookup))
                throw new InvalidOperationException("Native conversion was rejected.");
        };
        for (var i = 0; i < options.Warmup; i++)
        {
            managedConversion();
            nativeConversion();
        }
        RequireEqual(managed, native);
        // Setup, startup loading, LUT construction, allocation, and explicit collections are not measured.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        var managedSamples = new Sample[options.Samples];
        var nativeSamples = new Sample[options.Samples];
        for (var i = 0; i < options.Samples; i++)
        {
            if ((i & 1) == 0)
            {
                managedSamples[i] = Measure(process, managedConversion, options.Iterations);
                nativeSamples[i] = Measure(process, nativeConversion, options.Iterations);
            }
            else
            {
                nativeSamples[i] = Measure(process, nativeConversion, options.Iterations);
                managedSamples[i] = Measure(process, managedConversion, options.Iterations);
            }
        }
        RequireEqual(managed, native);
        var managedSummary = Summarize(managedSamples);
        var nativeSummary = Summarize(nativeSamples);
        return new ResolutionResult(width, height, managedSummary, nativeSummary,
            managedSummary.WallMedianMs / nativeSummary.WallMedianMs);
    }

    private static Sample Measure(Process process, Action conversion, int iterations)
    {
        process.Refresh();
        var cpuStart = process.TotalProcessorTime;
        var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < iterations; iteration++) conversion();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
        process.Refresh();
        var cpuElapsed = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
        return new Sample(elapsed / iterations, cpuElapsed / iterations, (double)allocated / iterations);
    }

    private static TimingSummary Summarize(Sample[] samples) => new(
        Percentile(samples.Select(sample => sample.WallMs), .5),
        Percentile(samples.Select(sample => sample.WallMs), .95),
        Percentile(samples.Select(sample => sample.CpuMs), .5),
        Percentile(samples.Select(sample => sample.CpuMs), .95),
        samples.Max(sample => sample.AllocatedBytes), samples);

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
    }

    private static void RequireEqual(byte[] managed, byte[] native)
    {
        if (!managed.AsSpan().SequenceEqual(native))
            throw new InvalidOperationException("Native output differs from the managed reference.");
    }

    private static int VerifyManagedFallback()
    {
        if (NativeDesktopPixelConverter.IsAvailable)
            throw new InvalidOperationException("Fallback verification requires an isolated output folder without a compatible native DLL.");
        const int width = 33;
        const int height = 7;
        const uint sourceStride = width * 8 + 12;
        const int destinationStride = width * 4 + 8;
        var source = new byte[sourceStride * height];
        new Random(671).NextBytes(source);
        var managed = new byte[destinationStride * height];
        var fallback = new byte[managed.Length];
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        using var pinnedSource = new PinnedBuffer(source);
        using var pinnedManaged = new PinnedBuffer(managed);
        using var pinnedFallback = new PinnedBuffer(fallback);
        DesktopPixelConverter.CopyHdrPixelsManaged(pinnedSource.Pointer, sourceStride, pinnedManaged.Pointer,
            destinationStride, width, height, lookup);
        if (DesktopPixelConverter.CopyHdrPixels(pinnedSource.Pointer, sourceStride, pinnedFallback.Pointer,
                destinationStride, width, height, lookup))
            throw new InvalidOperationException("Managed fallback unexpectedly selected native conversion.");
        RequireEqual(managed, fallback);
        using var bitmap = DesktopPixelConverter.CopyBitmap(pinnedSource.Pointer, sourceStride, width, height,
            DesktopPixelConverter.Rgba16Float);
        if (bitmap.Width != width || bitmap.Height != height)
            throw new InvalidOperationException("Bitmap conversion failed without a native DLL.");
        Console.WriteLine("Missing or incompatible native DLL: managed dispatcher and bitmap fallback verified.");
        return 0;
    }

    private sealed class PinnedBuffer(byte[] bytes) : IDisposable
    {
        private GCHandle _handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        internal nint Pointer => _handle.AddrOfPinnedObject();
        public void Dispose() => _handle.Free();
    }

    private sealed record Sample(double WallMs, double CpuMs, double AllocatedBytes);
    private sealed record TimingSummary(double WallMedianMs, double WallP95Ms, double CpuMedianMs,
        double CpuP95Ms, double MaxAllocatedBytesPerConversion, Sample[] Samples);
    private sealed record ResolutionResult(int Width, int Height, TimingSummary Managed, TimingSummary Native,
        double MedianSpeedup);

    private sealed record Options(int Samples, int Iterations, int Warmup, string? JsonPath, bool VerifyManagedFallback)
    {
        internal static Options Parse(string[] args)
        {
            var samples = 15;
            var iterations = 2;
            var warmup = 5;
            string? jsonPath = null;
            var verifyManagedFallback = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--samples": samples = ReadPositiveInt(args, ref i); break;
                    case "--iterations": iterations = ReadPositiveInt(args, ref i); break;
                    case "--warmup": warmup = ReadPositiveInt(args, ref i); break;
                    case "--json":
                        if (++i >= args.Length) throw new ArgumentException("--json requires an output path.");
                        jsonPath = Path.GetFullPath(args[i]);
                        break;
                    case "--verify-managed-fallback": verifyManagedFallback = true; break;
                    default: throw new ArgumentException($"Unknown argument: {args[i]}");
                }
            }
            return new Options(samples, iterations, warmup, jsonPath, verifyManagedFallback);
        }

        private static int ReadPositiveInt(string[] args, ref int index)
        {
            var option = args[index];
            if (++index >= args.Length || !int.TryParse(args[index], out var value) || value <= 0)
                throw new ArgumentException($"{option} requires a positive integer.");
            return value;
        }
    }
}
