# Third-party notices

Die verwendeten Bibliothekslizenzen sind über die referenzierten NuGet-Pakete
dokumentiert. Der Produktpfad enthält keine Tesseract-Bibliothek und keine
Tesseract-Sprachdaten.

## Blazor Hybrid and WebView2

Das Frontend verwendet `Microsoft.AspNetCore.Components.WebView.WindowsForms`
9.0.120 und die zugehörigen ASP.NET-Core-Komponenten von Microsoft unter der
MIT-Lizenz. Quellcode: https://github.com/dotnet/maui und https://github.com/dotnet/aspnetcore.

`Microsoft.Web.WebView2` 1.0.3179.45 wird nach den Microsoft Software License Terms
des NuGet-Pakets verwendet. Der Lizenztext liegt in
`licenses/Microsoft.Web.WebView2.txt`. Die separat installierte WebView2 Evergreen
Runtime wird nicht mit diesem Paket gebündelt.

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
