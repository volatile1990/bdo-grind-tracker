# Spot-Nachrichten in EN, DE, FR und SP

## Verwendung

Unter **Einstellungen → Diagnose → Rotation Monitor → Sprache der Spot-Nachrichten**
kann Englisch, Deutsch, Französisch oder Spanisch gewählt werden. Die Voreinstellung
„Wie eingestellte Spielsprache“ folgt der für die Loot-Erkennung eingestellten bzw.
erkannten Sprache. Die Sprache wird vor einer neuen Session gewählt und in den
Einstellungen sowie im Session-Checkpoint gespeichert.

Der Rotation Monitor und seine automatische Starterkennung verwenden dafür
Windows OCR mit `en-US`, `de-DE`, `fr-FR` bzw. `es-ES`. Das passende Windows-Sprachpaket
muss installiert sein. Ein fehlendes Paket wird als Erkennungsfehler gemeldet;
es wird nicht stillschweigend eine andere OCR-Sprache benutzt.

Dies erweitert die **Spot-Nachrichten der fünf Rotationsprofile**:
Hermesia, Aphrodon, Magaia, Event Horizon und Zephyros. Die Loot-Erkennung unterstützt weiterhin
Deutsch und Englisch. Eine vollständige französische oder spanische Loot-Session ist
damit nicht zugesichert. Die Oberflächensprache bleibt separat einstellbar.

## Enthaltene Meldungen

Die App bettet `data/rotation-message-patterns.json` ein. Der Katalog enthält
54 Bannertexte mit jeweils vier Sprachfassungen sowie die zwei bestehenden
Magaia-Namensanzeigen in vier Sprachen: insgesamt 224 Erkennungsregeln.
Jede Regel enthält Spot, Ereignisart, Sprache, Volltext, Suchphrase und Matching-Modus.

Die Sprachfassungen führen dieselben Ereignisse der fünf Profile aus.
Sacred Power hat fünf Textvarianten, Event Horizons Raumrekonstruktion zwei und
Magaia-AFK zwei. Varianten erzeugen keine zusätzlichen Rotationsphasen.

Magaia-Namen werden in der separaten Monster-Namensleiste erkannt. In DE, FR und SP
muss die vollständige normalisierte Zeile passen. Event-Namensvarianten werden
nicht als zusätzliche Ereignisse verwendet.

Weitere Spot-Kandidaten und ihre noch offenen Abläufe stehen in
[ROTATION_GUIDE_RESEARCH.md](ROTATION_GUIDE_RESEARCH.md).

## Erkennung und Grenzen

- Bestehende englische OCR-Anker bleiben wegen der vorhandenen Aufnahmen erhalten.
- Neue Sprachen verwenden Unicode-Normalisierung (NFC, Kleinschreibung, `ß → ss`)
  und Wortgrenzen. Akzente und Umlaute werden erhalten; es gibt keinen ungeprüften
  ASCII-Fallback.
- Nicht eindeutige Kurztexte wie „V-Vater“ müssen als vollständige Zeile erscheinen.
- Das spanische normale Aphrodon-Hog-Banner muss eine vollständige Zeile sein.
  Dadurch löst das längere Agris-Banner nicht gleichzeitig Hog aus.
- Event Horizons absichtlich beschädigte Raumzeit-Meldung verlangt mehrere stabile
  Textteile derselben Zeile. Es wird keine fehlerfreie Übersetzung erfunden.
- Magaia-Fragmente werden auch in den neuen Sprachen je sichtbarer Wiederholung
  gezählt; die beiden OCR-Durchläufe zählen dieselbe Wiederholung nicht doppelt.

Die Tests prüfen Originaltexte, Großschreibung, zerlegte Unicode-Zeichen,
Sprachwahl, Namens-/Textkollisionen, Zählung, automatische Starterkennung sowie
Speicherung und UI-Bindung. Die bisherigen Rotations- und Aufnahme-Regressionstests
decken die bestehende englische Erkennung ab.

**Es liegen keine neuen DE-/FR-/SP-Spielaufnahmen vor.** Die Live-Erkennungsqualität,
Zeilenumbrüche und Bildschirm-Ausschnitte dieser Sprachen sind deshalb noch nicht
mit echten Aufnahmen bestätigt. Ein vorhandener Text allein bestätigt weder seine aktuelle Auslösung
noch die zeitliche Reihenfolge im Spiel.
