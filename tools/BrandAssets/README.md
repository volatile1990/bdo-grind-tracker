# Brand asset container builder

Windows-only .NET 9 CLI. It never opens a window, invokes a game, removes a
background or changes the design. It resamples an existing transparent square
PNG into a Windows ICO containing seven 32-bit PNG frames (16, 24, 32, 48, 64,
128 and 256 pixels). An optional header PNG is 128 pixels square.

```powershell
dotnet run --project tools/BrandAssets -c Release -- path/to/logo.png path/to/logo.ico path/to/header.png
dotnet run --project tools/BrandAssets -c Release -- path/to/logo.png path/to/logo.ico path/to/header.png --force
dotnet run --project tools/BrandAssets -c Release -- --self-test
```

Input must be a PNG, square, 16–8192 pixels wide, at most 64 MiB, and contain
both visible and transparent pixels. Source and output paths must differ.
An existing output is refused unless `--force` is explicit; the source is never
overwritten. Outputs are individually staged and atomically moved into place.
The source PNG is read only. Smaller frames use high-quality bicubic resampling
with alpha; no recoloring, cropping, compositing with a background or aesthetic
editing is performed.

Every package is checked for ICO type/count/offsets, PNG signatures, exact
dimensions and alpha channel, then decoded with the Windows ICO reader. The
self-test uses only a temporary synthetic alpha fixture and removes its own
temporary directory afterwards; it does not read or alter any app/game images.
