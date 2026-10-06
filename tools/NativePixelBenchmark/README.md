# Native HDR conversion benchmark

Build and run locally on Windows x64 in Release:

```powershell
dotnet run --project tools/NativePixelBenchmark -c Release -- --json artifacts/native-pixel-benchmark.json
```

The app project builds and copies `Grindcrest.Native.dll` automatically and requires Visual Studio C++ x64 build tools. A managed-only development build can use `-p:NativeKernelsEnabled=false`. A missing or incompatible DLL causes the performance benchmark to fail, so a managed fallback cannot silently masquerade as native performance.

The default run uses 15 samples, two conversions per sample, and five warmup conversions per implementation. The benchmark lowers its own process priority to `BelowNormal` and leaves all other processes untouched. For a steadier comparison, run with the game closed and use more warmup and longer measurement batches:

```powershell
dotnet run --project tools/NativePixelBenchmark -c Release -- --warmup 40 --samples 31 --iterations 20 --json artifacts/native-pixel-benchmark-idle.json
```

Each resolution uses the same deterministic, padded RGBA16F input and preallocated pinned destination buffers. Buffer allocation, random input generation, half-float lookup creation, DLL loading, and bitmap allocation are outside the timed region. The benchmark checks byte-for-byte equality before and after measurement. Managed and native batches alternate order to reduce cache and ordering bias. Both conversions run on one calling thread.

Results include median and p95 wall time, total benchmark-process CPU time, and allocations on the measuring thread per conversion for 1920×1080, 2560×1440, and 3840×2160. Each timing sample is a batch average per conversion; the p95 is the p95 of those averages. Process CPU measurements have OS timer granularity; short batches may round to zero. The JSON contains every sample as well as the summaries. These measurements describe the HDR conversion kernel and do not establish Black Desert FPS or frametime improvements.

The test suite additionally covers every half-float bit pattern, RGB ordering, alpha, row padding, guard bytes, odd widths, negative destination strides, concurrent calls, and explicit managed dispatch. A direct export test bypasses the wrapper to verify that the C ABI rejects malformed and overflowing buffer spans before accessing memory.

To verify fallback when the DLL is actually absent, copy the benchmark output to an isolated folder, remove only that copy of `Grindcrest.Native.dll`, and launch a new process:

```powershell
$source = (Resolve-Path tools/NativePixelBenchmark/bin/Release/net9.0-windows10.0.19041.0).Path
$isolated = Join-Path (Resolve-Path artifacts).Path ('native-fallback-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $isolated -Recurse
Remove-Item -LiteralPath (Join-Path $isolated 'Grindcrest.Native.dll')
dotnet (Join-Path $isolated 'NativePixelBenchmark.dll') --verify-managed-fallback
```

Keep the original output DLL in place. The separate process is required because native availability is cached per process. Fallback verification checks both raw-buffer dispatch and owned bitmap creation.
