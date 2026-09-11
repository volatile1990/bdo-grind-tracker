# Lifetime-Test 7: Globaler Rahmen-Drop und Fensteraufnahme ohne Markierung

Test 7 ergänzt **Empty Picture Frame / Leerer Rahmen** im globalen Lootpool.
An allen sechs unterstützten Spots gelten **1–10 Stück pro Drop**, auch ohne
bereits erkannten Spot. Die bisherige `lifetime-v3`-Zähllogik bleibt erhalten.

Die Fensteraufnahme fordert vor dem Start `GraphicsCaptureAccessKind.Borderless`
an und setzt `GraphicsCaptureSession.IsBorderRequired` auf `false`. Damit
fordert Grindcrest den gelben Aufnahmerahmen nicht mehr an. Der Zugriff erfolgt
über die öffentliche WinRT-Schnittstelle; globale Windows-Einstellungen werden
nicht geändert. Auf älteren Windows-Versionen ohne diese API bleibt die Aufnahme
verfügbar. Windows kann die Markierung weiterhin erzwingen, wenn die Berechtigung
verweigert wird oder eine andere Aufnahme-App denselben Rahmen verlangt.
Siehe [Microsoft: IsBorderRequired](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired).

Der native Test unter `artifacts/lifetime-test-7-qa/native` bestätigt erlaubten
Borderless-Zugriff und das abgeschaltete Session-Flag auf diesem Rechner für
HDR und BGRA8. Beide Pfade liefern weiter frische Bilder, korrekte Ausschnitte,
200-ms-Takt und den erwarteten Fehler bei Minimierung. Das Hilfsfenster gehört
dem Testprozess; das laufende Spiel wird dabei nicht bedient. Die folgenden
Zählernachweise stammen weiterhin aus Test 6.

## Zählmodell aus Test 6

Dieser Stand verwendet `LifetimeLootReconciler` mit `lifetime-v3` und dem
Messungsmarker `visual-occupancy-v1`. Er ergänzt den Rohtextzähler aus Test 5 um
eine eng begrenzte, durch Bilddaten bestätigte Belegungsregel. Er übernimmt die
auf den akzeptierten OCR-Lesungen der ersten
Aufnahme verifizierte Konfiguration: fünf Slots, vier Lebensdauern
1250/1350/1450/1550 ms, jeweils bis zu 24 Hypothesen, Lernbeginn mit der ersten
lesbaren Dropmenge. Die unabhängige C#-Implementierung enthält keine extrahierte
Garmoth-Laufzeitdatei.

Die bestehende Windows-OCR, Zifferntemplates, Recovery und gezielte
Paddle-Nachprüfung verwenden ihre bisherigen Bildausschnitte. Der neue Zähler
erhält zusätzlich die ungekürzten Rohtexte; sein eigener Parser wertet sie gegen
den aktuellen Itemkatalog aus. Die separate alte Alignment-Nachprüfung wird für
dieses Modell nicht verwendet.
Die experimentellen engen Paddle-Bänder und Parserproben sind nicht aktiviert.
Das Aufnahmeintervall beträgt weiterhin 200 ms. Die alte visuelle
Fade-/Zuordnungsheuristik von `temporal-v2` läuft im neuen Zählmodell nicht.
`lifetime-v1`, `lifetime-v2` und die historischen Temporal-Modelle bleiben für
ihre jeweiligen Replays erhalten.

## Neue Belegungsbestätigung in Test 6

Die neue Regel greift nur bei einer bestimmten Folge: Zwei vorherige Frames
zeigen denselben vollständigen, zusammenhängenden Stapel mit gleichen Namen
und Mengen. Im aktuellen Frame kommen weitere belegte Plätze hinzu, während
der alte Stapel entsprechend nach oben verschoben weitergelesen wird. Zusätzlich
muss die oberste Zeile dieses aktuellen Stapels durch die Bildmessung bestätigt
sein. Das Modell berücksichtigt dann nur Hypothesen, die genügend Plätze für
diesen Stapel enthalten. Eine akzeptierte OCR-Zeile allein erzwingt diese
Abdeckung nicht; der Gegenfall eines einmaligen zusätzlichen OCR-Phantomslots
bleibt geschützt.

`NormalLootOccupancyTracker` vergleicht dazu die Glyphenmaske einer vorher
akzeptierten Zeile mit dem aktuellen Bildband. Eine Korrelation von mindestens
**0,90** bestätigt die physische Textbelegung. Die aktuelle, bereits verblassende
Zeile muss keine eigene starke Glyphenmaske mehr liefern. Der Vergleich prüft
Vorlagen desselben akzeptierten Itemnamens, liefert aber weder Namen noch Mengen
für den Zähler. Ein passender Vorgängerslot ist ausdrücklich **keine Ereignis-ID**:
bei wiederholten gleichen Items kann eine andere alte Zeile den stärkeren Treffer
liefern. Die Regel leitet daraus weder eine direkte Zeilenverbindung noch einen
neuen Drop ab.

Der separate Tracker ergänzt ausschließlich `OccupancyEvidence` mit
`previousSlot` und `correlation`. Die bestehende Appearance-Messung bleibt
unverändert. Alle **sechs OCR-Ausschnitte** und deren Reihenfolge, Recovery,
Paddle-Nachprüfung und Budgets bleiben erhalten; das Lebensdauermodell zählt
weiter ausschließlich **fünf Slots**. Auch der sechste physische Platz kann
Diagnose-Matches tragen, ohne eine sechste Menge zu erzeugen. Der originale
Fallback auf bereits akzeptierte Namen und Mengen bleibt erhalten.

Doppelte oder verspätete Frames ersetzen die Bildhistorie nicht. Nach einer
Lücke von mehr als 600 ms, einem Geometrie-/Skalenwechsel oder einem Wechsel
zwischen HDR-/Tone-Mapping-Repräsentationen wird sie neu aufgebaut. Sprachwechsel
und neue Sitzungen setzen sie ebenfalls zurück. Wenn die verlangte Abdeckung
in einer Lebensdauer-Variante keine gültige Hypothese übrigließe, verwendet diese
Variante für den Frame ihre bisherigen Alternativen und erhöht den kumulativen
Diagnosezähler `coverage-fallbacks`. Damit bleibt ein widersprüchlicher Bildbeleg
sichtbar, ohne die Variante unbrauchbar zu machen.

## Weitergeführte Test-5-Änderungen gegenüber Test 4

Unvollständige Texte dürfen wie im untersuchten Garmoth-Modell eine bestehende
Zeile stützen. Ein ausreichend langes Fragment eines bereits erkannten Items
bleibt als Evidenz erhalten; auch deutsche Namen werden berücksichtigt.
Jaro-Winkler-Ähnlichkeit hilft bei leicht unterschiedlichen Lesungen. Ein bloßer
Name erzeugt keine erfundene Menge. Eine Menge benötigt weiterhin eine tatsächlich
gelesene Zahl; bereits akzeptierte Mengen aus OCR und Zifferntemplates bleiben
erhalten. Spotfilter und Regeln für Einzelstücke gelten auch für später erkannte
Rohtexte. Die Erkennung führt ihre bisherigen sechs OCR-Ausschnitte aus, damit
Recovery-Reihenfolge und Budget unverändert bleiben. Das Zählmodell verwendet
ausschließlich die fünf Feedpositionen; die zusätzliche Diagnosezeile zählt nicht.

Die noch offene Ereignisgeschichte speichert Rohlesungen und wertet diese bei
jeder Projektion erneut mit dem aktuellen Parsing-Kontext aus. Ein später sicher
erkanntes Item oder ein eingegrenzter Spot kann damit frühere Lesungen besser
zuordnen. Die alte Bildaufnahme wird dabei nicht nochmals durch OCR geschickt.
Bereits abgeschlossene und verdichtete Historie bleibt wie im Garmoth-Modell
gebucht; es handelt sich nicht um eine unbegrenzt erneut gelesene Gesamtsitzung.

Die Live-Aufnahme bindet das Black-Desert-Fenster über HWND und Prozess-ID und
verwendet Windows Graphics Capture. Kalibrierung und OCR beziehen sich auf dessen
Spielbereich ohne Fensterrahmen. Andere Desktopfenster und die Position auf dem
Monitor bestimmen die aufgenommenen Pixel nicht mehr. Der eingestellte Monitor
bleibt als Ersatzposition für das Overlay erhalten. Minimieren, Schließen,
Größenänderung oder ausbleibende frische Frames stoppen die Aufnahme mit einer
Meldung. Es gibt keinen stillen Rückfall auf den Monitor. Spielinterne Popups
bleiben Teil des Spielbilds und können den Lootfeed weiterhin verdecken.

200 ms Aufnahmeintervall und zwei Sekunden Bestätigungspuffer bleiben erhalten.
Die drei Mechanismen orientieren sich an Garmoth; OCR-Engine, Parserdetails und
Grindcrest-spezifische Spot-/Mengenregeln sind weiterhin eine eigene Implementierung.

## Zählstand und Ankünfte

Das Zählmodell liefert eine vollständige, versionierte Rohprojektion.
Spätere Bilder dürfen eine Dropgeschichte korrigieren und damit Summen erhöhen,
verringern oder Items anders zuordnen. Ein angezeigter Zwischenstand beschränkt
die Hypothesen nicht. Mengen stammen aus tatsächlich gelesenen Item/Mengen-Paaren;
der Zähler ergänzt keine Menge aus einem gewünschten Session-Endstand.

`LootTotalsProjection` transportiert die gesamten fusionierten Summen, die
aktuelle unterstützte Dropzahl und den letzten Ankunftszeitpunkt. Audit-Deltas
werden zusätzlich aufgezeichnet; die UI wendet sie nicht nochmals auf den
Snapshot an. Itemwechsel bei gleichbleibender Gesamtmenge erzeugen ebenfalls
eine Aktualisierung. Mengenkorrekturen setzen keine neue aktuelle Ankunftszeit.
Manuelle Änderungen bleiben als separater Offset erhalten.

Seit Test 4 bestätigt `LootProjectionBuffer` die sichtbaren Mengen über ein
gleitendes Minimum je Item während der letzten zwei Sekunden. Eine kurzzeitig
zu hohe Schätzung erreicht dadurch die Anzeige nicht. Fortlaufende neue Drops
verlängern die Wartezeit älterer Mengen nicht. Auch Frames mit unveränderter
Rohrevision lassen ausstehende Mengen reifen. Der Puffer hält nur Mengenstände,
keine Bilder, und ändert weder OCR noch die Hypothesen des Zählmodells.

Dashboard, Overlay, laufende Speicherung und Upload verwenden denselben
gepufferten Stand aus `FrameUiMailbox`. Ankunftszeit und Auto-Pause folgen weiter
der unverzögerten Evidenz; das spätere Freigeben einer Menge gilt nicht als neue
Ankunft. Manuelle Änderungen bleiben unabhängige relative Korrekturen und dürfen
weiter unmittelbar nach unten gehen.

Beim Pausieren, Aufnahmefehler und Beenden wird der aktuelle vollständige
Zählstand übernommen, damit keine letzten Mengen im Puffer verloren gehen.
Fortsetzen verwendet diesen Stand als Ausgangspunkt. Eine Pause ist kein
unwiderruflicher Abschluss der Inferenz.

Die Verzögerung verhindert die beobachteten kurzfristigen Rückzählungen, bietet
aber keine Garantie gegen beliebig späte Korrekturen. Eine solche Korrektur wird
weiter übernommen. Ein Maximum mit dem bisherigen Anzeigewert würde einen
erkannten Fehler dauerhaft als Überzählung festhalten und wird nicht verwendet.

Normal und Rare besitzen getrennte Konten. Wie im untersuchten Garmoth-Modell
hat eine positive Normalsumme Vorrang vor der Rare-Summe desselben Items;
ohne positive Normalsumme gilt die Rare-Summe. Eine Normalrücknahme kann daher
den weiter vorhandenen Rare-Wert wieder sichtbar machen. Mengen aus beiden
Anzeigen werden nicht doppelt addiert. Rare-Stapelmengen zählen bei der
Dropzahl als Ankunft, nicht als ebenso viele Einzelereignisse.

Spot- und Mengenprior verwenden die letzten maximal 64 Dropdatensätze der
aktuell gewählten Ereignisgeschichte. Dadurch bleiben weder zurückgenommene
vorläufige IDs noch alte Mengen als zusätzliche Evidenz hängen. Sobald der
Spot ausreichend belegt feststeht, bleibt er wie bisher für die Sitzung fixiert.

## Diagnose und Lebenszyklus

Neue Aufnahmen verwenden Format 3 und die Engine `grindcrest-lifetime-v3`.
Jeder Frame enthält die vollständige `lootProjection`; Trace-Metadaten zeigen
die Lebensdauermodelle und die gewählte Variante. V3-Traces protokollieren
zusätzlich `coverage-fallbacks:N`; die historischen V1-/V2-Trace-Texte bleiben
unverändert. `lifetime-v3` benötigt den Marker `visual-occupancy-v1` und speichert
die gemessenen Belegungs-Matches an den Beobachtungen. Das Replay verwendet diese
Werte, ohne Bilder zu öffnen oder die Bildmessung erneut auszuführen. Neue
Belegungsdaten unter einem historischen Zählermodus werden abgewiesen.
Die Diagnose und ihr Replay
behalten die unverzögerten Rohprojektionen einschließlich interner Rücknahmen;
deren Semantik bleibt unverändert. Der initiale Parsing-Kontext und jede Änderung
mit Katalog, Aliasnamen und Einzelstückregeln werden mit einer Revision gespeichert.
Das Replay nutzt diese aufgezeichneten Daten statt eines inzwischen veränderten
Produktkatalogs. Fehlende oder widersprüchliche Kontexte werden abgewiesen.
Wiederholte Abschlussaufrufe
erzeugen keine erneute Buchung. Ein Stopp projiziert den bestehenden Zustand;
eine neue Sitzung setzt ihn vollständig zurück.

Aufnahmen mit `lifetime-v1` und `lifetime-v2`, einschließlich echter Header mit
`grindcrest-lifetime-v2`, sowie Format-2-Aufnahmen mit `temporal-v1` und
`temporal-v2` bleiben mit ihrem ursprünglichen Algorithmus reproduzierbar.
Ein neuer OCR-Lauf über alte PNGs
ist ausdrücklich eine neue Messung und wird getrennt gespeichert.

## Validierung dieses Teststands

- Test 6: **3.757 Tests bestanden** (Core 448, OCR 243, App 3.066).
  Der Produkt-Build hat keine Warnungen.
- Test 6: Der produktive Belegungstracker reproduziert auf allen **5.512 Frames**
  und **6.738 Normal-Beobachtungen** die eingefrorene Bildprobe exakt:
  gleiche Matches, Korrelationen und bestätigte Plätze, **null Abweichungen**.
  Die Originaldaten bleiben laut SHA-256-Prüfung unverändert. Dieser Nachweis
  prüft die Bildmessung auf vorhandenen Eingaben; er ist kein neuer OCR-Lauf.
- Test 6: Lokale Zählerprüfungen mit den gespeicherten Normal-Eingaben und
  gemessenen Belegungsdaten liefern für Aufnahme 1–4 **576 / 264 / 2038 / 604
  Helme**. V2 auf denselben Eingaben bleibt bei **576 / 264 / 2034 / 600**.
  Die zusätzlich erhaltenen Vierer stammen in der finalen Ereignisgeschichte
  ausschließlich aus den zuvor
  bildlich untersuchten Folgen 93–97 der dritten und 279–301 der vierten
  Aufnahme. Alle vier Prüfungen haben **null Coverage-Fallbacks**. Der isolierte
  Einzelbild-Phantomslot erhöht die korrekte Menge von vier nicht.
- Die unabhängig bekannten Helmzahlen sind **576 / 324 / 2050 / 604**. Test 6
  behebt damit den lokalisierten Viererverlust der vierten Aufnahme und einen
  Viererverlust der dritten. Die verbleibenden Unterzählungen werden durch
  diese Belegungsregel nicht als gelöst ausgegeben.
- Test 6: Vollständige frische OCR aller **5.512 Frames** bestätigt **576 / 264 /
  2.038 / 604 Helme**. Aufnahme 1 enthält weiterhin 3 Black Stones, 1 Caphras
  und 1 Ring; Aufnahme 4 enthält zusätzlich 1 Caphras, 2 Black Stones und 1 Dust.
  Alle Originaldateien bleiben unverändert. Sämtliche OCR-Beobachtungen und
  ihre Reihenfolge sind bis auf die neuen Belegungsmetadaten exakt gleich
  zum eingefrorenen Test-5-Pfad. Alle vier Replays reproduzieren Endmengen und
  Ereignisabläufe exakt; kein Coverage-Fallback wird benötigt.
- Test 6: Interne Rücknahmen je Aufnahme: **10 / 13 / 21 / 22**; sichtbare
  Rücknahmen mit dem tatsächlichen Anzeigepuffer: **0 / 1 / 1 / 0**. Die beiden
  exakten Helmreferenzen bleiben ohne sichtbare Rückzählung. Die mittlere reine
  Analysezeit beträgt je Aufnahme **35–38 ms**, ohne Live-Capturezeit.
  `artifacts/lifetime-test-6-qa/verification-results.json` enthält die vollständige
  Abnahme; `package-result.json` protokolliert zusätzlich Start-/UI-Prüfung,
  Replays der ausgelieferten EXE mit V3 sowie historischen V1/V2-Diagnosen und
  das geprüfte ZIP. `build-info.json` im EXE-Ordner ordnet den Build eindeutig zu.

Die folgenden Nachweise dokumentieren den früheren Stand Test 5:

- Test 5: **3.671 Tests bestanden** (Core 427, OCR 243, App 3.001).
- Test 5: Vollständige frische OCR beider Originalaufnahmen: **576 / 3 / 1 / 1**
  in Aufnahme 1 und **264 Helme / 4 Dust** in Aufnahme 2. Die akzeptierten Lesungen
  der fünf Feedpositionen sind identisch zum geprüften Zwischenstand. Der
  historische Zähler auf denselben Eingaben liefert dieselben Summen. Originale
  blieben laut SHA-256-Prüfung unverändert; neue Replays reproduzieren Summen
  und Ereignisabläufe exakt.
- Test 5: Aufnahme 1 hat zehn interne und **keine sichtbare Rückzählung**.
  Aufnahme 2 hat 13 interne und **eine späte sichtbare Rückzählung**. Die
  bekannten **324 Helme / 7 Dust** werden damit noch nicht erreicht. Die
  neuen Rohtextregeln liefern in diesen beiden Aufnahmen keine zusätzlichen
  verwertbaren Mengen; sie sind zusätzlich mit gezielten Integrationstests geprüft.
- Test 5: Fensteraufnahme nativ mit eigenem Testfenster in SDR und HDR geprüft:
  Spielbereich ohne Rahmen, frische Farbänderungen, Bewegung außerhalb des
  Desktops und Fehler bei Minimierung. Je zehn Frames bei 200 ms Zielintervall
  ergaben im Mittel 204/206 ms. Die späteren Black-Desert-Diagnosen vom
  11. September erreichen jeweils rund 203 ms; die Unterzählung bleibt trotzdem
  sichtbar. Den vollständigen Vergleich der vier Referenzen, den reinen
  Paddle-Wechsel und die konkreten Fehlverbindungen dokumentiert
  [OCR_COMPARISON_20260911.md](OCR_COMPARISON_20260911.md).

Die folgenden Nachweise dokumentieren die früheren Stände Test 3 und Test 4:

- Unveränderte akzeptierte OCR-Daten, Aufnahme 1: **576 Helme, 3 Black Stones,
  1 Caphras Stone**; der Ring stammt aus dem getrennten Rare-Kanal.
- Vollständige erneute Verarbeitung aller **1012 Original-PNG-Frames** mit dem
  bisherigen OCR-Pfad: **576 Helme, 3 Black Stones, 1 Caphras Stone, 1 Ring**.
  SHA-256-Prüfungen bestätigen unveränderte Quelldateien.
- Aufnahme 2 auf bisherigen akzeptierten OCR-Daten: **264 Helme / 4 Dust**.
  Der bekannte OCR-Verlust hinter der Bossmeldung gehört nicht zur jetzigen
  Zählerumstellung und wird dadurch nicht behoben.
- Historische Komplett-Replays bleiben **472** beziehungsweise **268 / 4**,
  einschließlich identischer Ereignisabläufe.
- Tests decken korrigierbare Projektionen, Quellenfusion, Mengenprior,
  Spot-Evidenz, UI, manuelle Änderungen, Wiederholungen, ungültige Eingaben,
  Reset und die beiden aufgezeichneten Eingabefolgen ab.
- Test 4: Alle **1012 Rohprojektionen** des frischen Test-3-OCR-Laufs werden durch
  den tatsächlichen UI-Puffer abgespielt. **Zehn interne Rückzählungen** (neunmal
  vier Helme, einmal ein Black Stone) ergeben **keine sichtbare Rückzählung**;
  die Endsumme bleibt **576 / 3 / 1 / 1**. Die zurückgenommenen Mengenstufen waren
  zuvor nur rund **200–400 ms** sichtbar.
- Zusätzliche Tests prüfen kontinuierliche Drops, späte ehrliche Korrekturen,
  unveränderte und veraltete Revisionen, Aufnahmeunterbrechungen, Pause und
  Fortsetzen, finale Speicherung, manuelle Offsets und den Aktivitätszeitpunkt.

Die 576 sind ein reproduzierter Messwert dieser Aufnahme. Ein festes
Lebensdauermodell kann unter anderen Sichtbarkeitsbedingungen weiterhin Fehler
machen; der Testbuild behauptet keine generelle perfekte Zählung. Die
ausführliche Untersuchung und Gegenproben stehen in
[Unterzählung: Befunde und Ersatz](LOOT_UNDERCOUNT_20260910.md).

Test-6-Artefakte: `artifacts/lifetime-test-6-qa` für die noch laufende frische
OCR-Abnahme sowie
`artifacts/ocr-comparison-20260911/native-garmoth/recording4-event-audit/occupancy-product-parity/run1/summary.json`
für die abgeschlossene Producerparität. Die eigenständigen Raw- und
Occupancy-Fixtures aller vier Referenzen liegen unter `tests/fixtures/lifetime`;
sie benötigen zum Testen keine Benutzerdateien.

Test-5-QA-Artefakte: `artifacts/lifetime-test-5-qa/verified-ocr-recording1`,
`verified-ocr-recording2` und `artifacts/window-capture-qa`. Der verworfene
Versuch mit nur fünf OCR-Ausschnitten steht getrennt unter `final-fresh-ocr-*`;
er veränderte die Recovery-Reihenfolge und erreichte 584 statt 576 Helme.
Die Änderung gehört nicht zum Testbuild.

Frühere QA-Artefakte: `artifacts/lifetime-test-4-qa`, die Messung der kurzfristigen
Rücknahmen unter `artifacts/stable-display-qa`. Der vollständige OCR-Nachweis des
unveränderten Zählmodells liegt unter `artifacts/lifetime-test-3-qa`.
Die EXE-Ausgabe enthält eine
eigene Buildkennung und `build-info.json` für die eindeutige Testzuordnung.
