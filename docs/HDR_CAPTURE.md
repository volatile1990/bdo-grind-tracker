# HDR-Aufnahme für OCR

Bei aktivem HDR wird der ausgewählte Monitor über `IDXGIOutput5::DuplicateOutput1`
aufgenommen. Die angebotenen Texturformate sind `R16G16B16A16_FLOAT` (10) und
`B8G8R8A8_UNORM` (87). Das tatsächlich gelieferte Format bestimmt die Verarbeitung.
Der bisherige Weg über `DuplicateOutput` liefert BGRA8; dabei können helle
HDR-Abstufungen schon vor der OCR verloren gehen.

FP16-Pixel bleiben während der GPU-Kopie erhalten. Für die OCR werden positive
scRGB-Kanäle mit `x / (1 + x)` und anschließend der sRGB-Transferfunktion in ein
8-Bit-Bitmap umgewandelt. Eine feste Kennlinie verhindert, dass wechselnde
Helligkeit an anderer Stelle des Monitors die Darstellung derselben Lootschrift
verändert. Die Umwandlung dient der Texterkennung und ist keine farbverbindliche
HDR-Bildwiedergabe. Negative und NaN-Werte werden schwarz, positive Unendlichkeit
weiß; der Alphakanal wird ignoriert. Eine Tabelle für alle Half-Bitmuster vermeidet
Potenzberechnungen pro Bildpixel.

Die neue Darstellung verwendet die bestehenden SDR-Textfilter. Der bisherige
HDR-Filter erwartet fast weiße Pixel mit Werten ab 250/254 und würde die neue
Darstellung weitgehend verwerfen. Deshalb werden zwei Eigenschaften getrennt
weitergegeben und in Diagnoseframes gespeichert:

| `isHdr` | `isToneMapped` | Bedeutung und OCR-Filter |
|---|---|---|
| false | false | SDR-Ausgabe, bisherige BGRA-Pixel und SDR-Filter |
| true | true | HDR-Ausgabe, FP16 umgewandelt, SDR-Filter |
| true | false | HDR-Ausgabe mit BGRA-Fallback, bisheriger HDR-Filter |

Bei alten Aufnahmen fehlt `isToneMapped`; das bedeutet unbekannt. Die optionale
Eigenschaft ändert weder das JSONL-Format 2 noch die Zähler-Engine
`companion-0.7.4-drop-quantity-v5`. Offline-Replay verwendet weiterhin gespeicherte
OCR-Ergebnisse und führt weder Aufnahme noch OCR erneut aus.

Fehlt Output5 oder meldet der Treiber `DXGI_ERROR_UNSUPPORTED` beziehungsweise
`E_INVALIDARG` für den neuen Aufruf, bleibt die bisherige Aufnahme verfügbar.
Andere Fehler werden weitergereicht. Nicht unterstützte gelieferte Pixeltypen
werden abgewiesen, statt ihre Bytes als BGRA zu interpretieren. Nach
`DXGI_ERROR_ACCESS_LOST` wird wie bisher die Ausgabe neu ermittelt und die
Duplizierung neu aufgebaut; dabei werden auch HDR-Zustand und Formatauswahl erneut
bestimmt. Es gibt keinen Zugriff auf den Spielprozess oder Änderungen am Spiel.

Die Diagnose speichert weiterhin ausschließlich die kalibrierten PNG-Ausschnitte
und JSONL bei aktivierter Aufzeichnung. Die PNGs enthalten genau die Darstellung,
die auch an die OCR ging. FP16-Daten und ganze Monitorbilder werden nicht exportiert.

## Validierung und verbleibender Praxistest

Automatisierte Tests prüfen die Unterscheidbarkeit heller Werte über 1,
Kanalreihenfolge, Zeilenabstände, monotone Abbildung, Sonderwerte, unveränderte
BGRA-Pixel und die Übergabe der Filterwahl bis zum Analyzer. Diagnosemetadaten
bleiben mit dem vorhandenen Replay kompatibel.

Ein vorheriges, lokales Experiment vom 7. September 2026 erhielt auf dem
HDR-Monitor dieser Maschine Format 10 und Werte bis 6. Es zeigte andere
Desktopfenster und belegt ausschließlich die technische Verfügbarkeit des Formats.
Es ist kein Nachweis besserer Loot-Erkennung. Die neue Kennlinie und die
resultierenden Textmasken müssen mit einer neuen Aufnahme während des Grinds
gegen den tatsächlichen Inventarzuwachs geprüft werden. SDR-Aufnahmen behalten
ihre bisherigen Pixel und Filter.

## Microsoft-Referenzen

- [DuplicateOutput1 und Formatkonvertierung](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgioutput5-duplicateoutput1)
- [HDR-Aufnahme und scRGB](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range#capturing-hdr-and-wcg-screen-content)
- [HDR-Bildaufnahme und FP16](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
