# Third-party notices

Die verwendeten Bibliothekslizenzen sind über die referenzierten NuGet-Pakete
dokumentiert.

## PaddleOCR and ONNX Runtime

Die zusätzliche lokale Texterkennung verwendet das unveränderte offizielle
`PP-OCRv6_small_rec_onnx`-Modell von PaddlePaddle, Revision
`b8f84f0b80c529de40b4fbb3544b84fa7233a513`, unter Apache License 2.0.
Der Zeichensatz stammt aus der Modellkonfiguration, mit CTC-Leerklasse und
abschließendem Leerzeichen. Lizenz: `licenses/PaddleOCR-Apache-2.0.txt`.
Quellen und Prüfsummen: `data/ocr/paddle-v6-small/SOURCES.md`.

`Microsoft.ML.OnnxRuntime` 1.29.0 führt das Modell lokal auf der CPU aus.
Lizenz: `licenses/ONNXRuntime-MIT.txt`; enthaltene weitere Komponenten:
`licenses/ONNXRuntime-ThirdPartyNotices.txt`.
Quellen: https://github.com/PaddlePaddle/PaddleOCR,
https://huggingface.co/PaddlePaddle/PP-OCRv6_small_rec_onnx und
https://github.com/microsoft/onnxruntime.

## Blazor Hybrid and WebView2

Das Frontend verwendet `Microsoft.AspNetCore.Components.WebView.WindowsForms`
9.0.120 und die zugehörigen ASP.NET-Core-Komponenten von Microsoft unter der
MIT-Lizenz. Quellcode: https://github.com/dotnet/maui und https://github.com/dotnet/aspnetcore.

`Microsoft.Web.WebView2` 1.0.3179.45 wird nach den Microsoft Software License Terms
des NuGet-Pakets verwendet. Der Lizenztext liegt in
`licenses/Microsoft.Web.WebView2.txt`. Die separat installierte WebView2 Evergreen
Runtime wird nicht mit diesem Paket gebündelt.

## Velopack

Installer und Updatefunktion verwenden Velopack 1.2.0 unter der MIT-Lizenz.
Quellcode: https://github.com/velopack/velopack (Commit
`f2edcbcafb81da5b3c884aaea330e225ad91d8b6`). Der vollständige Lizenztext liegt in
`licenses/Velopack.txt` und wird mit der Anwendung ausgeliefert.

## BDO Companion digit templates

Für die geforderte Verhaltensparität enthält die OCR-Assembly 30 Ziffern-PNGs, die aus
der lokal untersuchten BDO-Companion-Version 0.7.4 rekonstruiert wurden. Die
Quelldatei sowie SHA-256, RVA, Größe und PNG-Hash jedes Assets sind in
`CompanionDigitCatalog.cs` dokumentiert. Die Companion-EXE selbst, Quellcode und interne
Datenbanken werden nicht mitgeliefert.

## Black Desert item icons

Die optionalen lokalen Itemicons wurden am 2026-09-02 von den in
`data/icons/SOURCES.md` dokumentierten BDO-Codex-Itemseiten geladen und ohne inhaltliche
Änderung von WebP nach PNG konvertiert. Sie dienen nur der Darstellung bereits erkannter
Sitzungssummen und nicht der Erkennung.

Black Desert und die Spielinhalte sind Eigentum von Pearl Abyss. BDO Codex, Pearl Abyss
und BDO Companion stehen in keiner Verbindung zu diesem Projekt und unterstützen es
nicht.
