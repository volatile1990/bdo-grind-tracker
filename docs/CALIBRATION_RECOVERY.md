# Profilwahl und optionale Rare-Kalibrierung

Eine am 11. September 2026 bereitgestellte Aufnahme enthielt lesbare Hermesia-
Lootzeilen, die wegen ihrer Position verworfen wurden. Die zuletzt gespeicherte
Profil-XML lag in einem älteren Ordner; die bisherige Wahl nach Ordnerdatum nahm
ein anderes Profil. Beide aktiven Rare-Einträge enthielten relative Nullkoordinaten
mit `PendingType=RightBottom` und abweichenden `PosX/PosY`. Ein gespeichertes,
zum normalen Lootlayout passendes Preset enthielt eine alternative Rare-Position.

## Auswahlregeln

- Unter den bisher akzeptierten numerischen Profilnamen werden nur Ordner mit
  einer `gameVariable.xml` berücksichtigt. Das Änderungsdatum dieser Datei
  entscheidet, bei Gleichstand die stabile ordinale Pfadreihenfolge. Eine neue,
  defekte Konfiguration wird nicht stillschweigend durch ein altes Profil ersetzt.
- Der Haupt-Lootanker bleibt ausschließlich der eindeutige, sichtbare aktive
  `UIData`-Eintrag 159. Ein Preset kann einen fehlenden Hauptfeed nicht aktivieren.
- Ein eindeutiger aktiver Rare-Eintrag 161 mit gültiger relativer Position hat
  Vorrang. Verborgene oder fehlende aktive Rare-Einträge bleiben ausgeschaltet.
- Bei sichtbarem Rare-Log mit fehlenden/ungültigen Koordinaten wird ein Ersatz
  gesucht. Das konkret beobachtete Muster `(0,0)`, `RightBottom` und mindestens
  einem endlichen `PosX/PosY != 0` gilt ebenfalls als unklar. Andere gültige
  Bildschirmrandpositionen werden weiter wie zuvor gelesen.
- Kandidaten stammen nur aus flachen `UISettingPreset0/1/2`-Einträgen derselben
  Profil-XML. Revert-, Battle-, fremde Profil- und Charakterdaten sind ausgeschlossen.
  Ihr sichtbarer Hauptanker muss innerhalb eines Pixels zur aktuellen Position
  passen. Vorhandene Angaben zu Auflösung/Skalierung dürfen nicht widersprechen.
  Rare-Koordinaten müssen endlich, im relativen Bereich 0..1, ungleich dem Paar
  `(0,0)` und als Ausschnitt verwendbar sein.
- Kandidaten werden nach berechneten Pixelkoordinaten zusammengefasst. Genau eine
  Position erlaubt einen Ersatz, verschiedene Positionen bleiben mehrdeutig.
  Die Presetnummer ist keine Priorität und belegt kein aktives Preset.

Diese Regeln ändern weder UI-Skalierung noch BDO-Dateien, OCR-Gates oder
Item-Zählregeln. Dateiaktualität und ein passendes Preset sind Auswahlhinweise;
bei widersprüchlichen Daten lässt sich die sichtbare Oberfläche nicht allein
aus dem Cache beweisen. Die Aufnahme bleibt der erforderliche Live-Gegentest.

## Optionale Fehler und Nachladen

Ungültige oder mehrdeutige Rare-Daten deaktivieren nur den optionalen Rare-Pfad.
Der normale Feed kann weiterlaufen; der App-Status nennt die eingeschränkte
Rare-Erfassung. Leere Rare-Bilder sind kein Auslöser für eine neue Auswahl.

Gespeicherte Änderungen werden vor den nächsten passenden Frames eingelesen.
Normale Änderungen an Profil, Skalierung, Auflösung oder Randbeschnitt behalten
ihre bisherigen Stoppregeln. Verändert ein Rare-Versatz während einer Session
die Höhe/Verankerung des erfassten Bandes, bleibt Rare bis zum Neustart deaktiviert.
Eine normale Rare-Verschiebung oder ein Quellenwechsel behält den Zählerzustand;
unveränderte Normalbilder setzen ihre visuellen Zustände fort.

## Diagnose und Prüfungen

`observations.jsonl` enthält beim ersten Frame und bei Änderungen zusätzlich
`captureCalibration`: Profil-ID, Auswahlregel, Auflösung, Skalierung, Normal-/Rare-
Anker sowie Rare-Status, Quelle und Grund. Absolute Windows-Nutzerpfade und andere
Cache-Inhalte werden nicht aufgenommen. Ältere Aufnahmen bleiben lesbar;
Kalibrierungsmetadaten beeinflussen keine Replay-Zählung.

Regressionstests decken den minimierten gemeldeten Fall, gültige Standard- und
Randpositionen, verborgenes/fehlendes Rare-Log, ungültige Werte, verschiedene
Presets, die Profilzeitstempel, optionalen Ausfall und Nachladen ohne Doppelzählung ab.
