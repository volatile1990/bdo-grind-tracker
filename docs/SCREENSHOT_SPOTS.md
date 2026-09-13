# Screenshot-Spots – 13. September 2026

Alle 40 konkreten Spots aus den beiden Referenzbildern sind abgedeckt. Die elf
bisherigen konkreten Edania-Spots bleiben erhalten; hinzu kommen 26 weitere Spots
und drei getrennte Dark-Energy-Floodlands-Gebiete. Die bisherigen Session-IDs werden
nicht umbenannt. Drei zusätzliche Sammelprofile dienen der automatischen Erkennung,
wenn der sichtbare Trashname die Variante nicht bestimmt.

Die Ergänzung umfasst erlaubte Lootpools, englische und deutsche Erkennung,
Trash-Erkennung, NPC-Werte, Live-Marktpreis-IDs, Item-/Spot-Icons, UI-Profile,
Garmoth-Zuordnungen und die Verarbeitung öffentlicher Grind-Referenzen. Für neue
Spots werden keine Stundenmittelwerte erfunden; vorhandene öffentliche Werte
werden über den bestehenden Anbieter geladen.

## Neue Spots

Gavinya Coastal Cliff; Star's End; Sycraia Abyssal Ruins (Lower); [Elvia] Orzekea;
[Dehkia] Gyfin Rhasia Temple (Upper); Tungrad Ruins; Darkseeker's Retreat;
Fortunate Golden Pig Cave; [Dehkia] Mirumok Ruins; Winter Tree Fossil (280ap);
Unlucky Golden Pig Cave; [Dehkia II] Ash Forest; [Dehkia II] Olun's Valley;
Yzrahid Highlands; Hexe Sanctuary (Elvia); Quint Hill (Elvia); Dokkebi Forest;
[Dehkia] Thornwood Forest; City of the Dead; [Dehkia] Cadry Ruins;
[Dehkia] Ash Forest; [Dehkia] Crescent Shrine; [Dehkia] Cyclops Land;
Jade Starlight Forest; [Dehkia] Tunkuta; [Dehkia] Hystria Ruins.

Dark Energy Floodlands ist zusätzlich für Zephyros, Orbita und Great Red Sea
einzeln auswählbar. Star's End und Sycraia verwenden die aktuellen überarbeiteten
Garmoth-Spots 200 und 201.

## Gleicher Trashname

- Floodlands: Die automatische Erkennung verwendet die bestehende Sammelkennung.
  Das Gebiet wird in der Live-Session ausgewählt.
- Dehkia Ash Forest: Beide Stufen heißen beim Trash „Tainted Specter's Cloth“.
  Die Auswahl bestimmt die Stufe und den korrekten Garmoth-Trashschlüssel.
- Winter Tree Fossil: 250 und 280 AP verwenden denselben Trashnamen. Die
  unterstützte 280-AP-Variante muss vor dem Upload bestätigt werden.

Ohne Auswahl bleibt der Loot lokal erfasst. Die Stundenautomatik wartet mit dem
Versand; die Auswahl bleibt über weitere OCR-Bilder, Pausen und Neustarts erhalten.
Bereits übertragene oder unklare Uploads sperren nachträgliche Variantenwechsel.

## Mengen und Bewertung

Neue Spots verwenden Trash 1–1000. Bestehende Trash-Minima bleiben erhalten,
während das Maximum überall 1000 beträgt. Black Stone, Caphras Stone und Ancient
Spirit Dust erhalten 1–100; Laila's Petal 1–10. Andere alte bestätigte Mengen
bleiben erhalten. Unbestätigte seltene Dropmengen bleiben offen.
[Vollständige Mengenregeln](DROP_QUANTITIES.md).

Der Wortschatz enthält 168 zusätzliche Itemnamen mit passenden deutschen Namen
und echten Item-Icons. Marktpreise werden weiterhin live abgefragt. Festwerte
beruhen auf überprüften NPC-Werten; Kampfartefakte behalten den dokumentierten
Verarbeitungswert von 20,1 Millionen Silber aus den öffentlichen Metadaten.
Garmoths gemeinsamer Artefakt-Eintrag erhält die Summe der erkannten Artefakte.
Nicht eindeutig unterscheidbare Kompass-/Teleskopteile werden beim Upload
ausgelassen und in dessen Vorschau genannt. Lokal bleiben sie erfasst.

## Quellen und Darstellung

- [Geprüfte Spot-/Itemdaten, aktuelle Korrekturen und Ausschlüsse](reference-evidence/screenshot-spots-SOURCES.md)
- [Regionen, AP-Limits und verfügbare Empfehlungen](SCREENSHOT_SPOT_PRESENTATION_SOURCES.md)
- [Item-Icons und Prüfsummen](../data/icons/catalog.json)
- [Neue Spot-Icons und Prüfsummen](../data/spot-icons/screenshot-spots-sources.json)

Die erweiterten Katalogteile lassen sich mit `python scripts/Import-ScreenshotSpots.py`
aus den geprüften lokalen Referenzdaten reproduzieren. Die App liest zur Laufzeit
weder diese Recherchedateien noch eine Companion-Installation. Vorhandene
Edania-Pools und ihre späteren offiziellen Korrekturen haben Vorrang vor älteren
Cache-Daten. Nicht bestätigte AP-/DP-Empfehlungen bleiben ausgeblendet; für fehlende
Gebietsszenen verwendet die Oberfläche einen neutralen Hintergrund.
