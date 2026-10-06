# Native Rechenkerne

Die Windows-x64-App lagert die HDR-Umrechnung von RGBA16F nach BGRA8 in
`Grindcrest.Native.dll` aus. Oberfläche, Aufnahme, OCR-Steuerung und Sessions
bleiben in C#. Ein Aufruf über source-generiertes P/Invoke (`LibraryImport`)
verarbeitet das gesamte aufgenommene Bild.

## Verhalten

Die DLL erhält geliehene Quell- und Zielpuffer samt Zeilenabständen. Sie legt
keine eigenen Puffer an, erzeugt keine Threads und behält keine Zeiger nach dem
Aufruf. Die Tabelle für alle 65.536 Half-Bitmuster entsteht weiterhin mit der
bisherigen C#-Kennlinie. Damit bleiben Rundung, Kanalreihenfolge, NaN,
Unendlichkeit und negative Werte identisch. SDR-Bilder verwenden weiterhin die
bisherige Speicher-Kopie.

Der Host prüft ABI-Version 1 und lädt die DLL aus seinem Assembly-Verzeichnis.
Fehlt die DLL oder passen Architektur beziehungsweise Exporte nicht, wird die
bisherige C#-Umrechnung verwendet. Die Verfügbarkeitsprüfung wird gecacht; ein
fehlgeschlagener Import wird nicht pro Bild wiederholt. Die native Routine nutzt
die x64-Basisinstruktionen und benötigt keine separate C++-Runtime.

## Lokaler Build

Zusätzlich zum .NET-SDK werden die Visual-Studio-C++-Buildtools mit x64-MSVC
benötigt. `scripts/Build-NativeKernels.ps1` findet sie über `vswhere`; ein separat
gestartetes Developer-Terminal ist nicht erforderlich.

```powershell
dotnet build src/BdoGrindTracker.App -c Release
```

Das App-Projekt baut die DLL automatisch optimiert, auch bei einem Debug-Build,
und kopiert sie in App-, referenzierende Test- und Publish-Ausgaben. Das
Store-Paketprüfskript verlangt die DLL. Ein normaler Build erstellt kein MSIX.

Für einen gezielten Compilerwechsel kann
`-p:NativePixelConverterCompiler="<Pfad zu x64 cl.exe>"` zusammen mit
`-t:Rebuild` verwendet werden. Ein ausdrücklich rein verwalteter
Entwicklungsbuild ist möglich:

```powershell
dotnet build src/BdoGrindTracker.App -c Release -p:NativeKernelsEnabled=false
```

Dieser Build ignoriert auch eine DLL, die von einem früheren Build im
Ausgabeverzeichnis liegt. Für andere Zielarchitekturen ist die native Variante
standardmäßig ausgeschaltet. Die Browser-Vorschau benötigt keine C++-Buildtools.

## Gleichheit und Performance prüfen

```powershell
dotnet test tests/BdoGrindTracker.App.Tests -c Release --filter FullyQualifiedName~DesktopPixelConverterTests
dotnet run --project tools/NativePixelBenchmark -c Release -- --samples 31 --iterations 10 --warmup 40 --json artifacts/native-pixel-benchmark.json
```

Die nativen Tests dürfen die DLL-Prüfung nicht überspringen. Sie prüfen alle
Half-Bitmuster, ungerade Zeilenabstände, Padding, negative Ziel-Strides,
gleichzeitige Aufrufe und die Ablehnung ungültiger ABI-Parameter. Die bisherige
Bitmap-Konvertierung bleibt ebenfalls getestet.

Der lokale Benchmark vergleicht aufgewärmte Konvertierungen mit denselben
gepinten Puffern in wechselnder Reihenfolge. Er läuft mit eigener Prozesspriorität
`BelowNormal` und erfasst Rohmessungen, Laufzeit, Prozess-CPU-Zeit und verwaltete
Speicherbelegungen. Bildaufnahme, Bitmap-Allokation und OCR sind nicht Teil der
Zeitmessung. Median und p95 beziehen sich auf Mittelwerte pro Messblock;
CPU-Zeiten unterliegen der Windows-Timerauflösung.

Ein Vorteil in diesem Benchmark ist kein Nachweis eines FPS- oder
Frametime-Gewinns in Black Desert. Dafür müssen identische Spielszenen mit und
ohne Tracker sowie die Erkennungszuverlässigkeit verglichen werden. Einzelheiten
und die Prüfung des Fallbacks stehen unter
[`tools/NativePixelBenchmark`](../tools/NativePixelBenchmark/README.md).
