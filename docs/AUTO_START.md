# Automatischer Grindstart

In der **Live-Session** neben **Tracking starten** und **Neue Session** wird mit
**Grind automatisch erkennen** die standardmäßig ausgeschaltete Automatik ein-
oder ausgeschaltet. Sie startet die aktuelle
Session erst nach bestätigten neuen Monsterdrops. Eine automatisch pausierte
Session wird fortgesetzt. Ein Spot- oder Klassenwechsel benötigt weiterhin
**Neue Session**.

Manuelle Pause sperrt den Autostart auch nach einem Neustart. **Automatik wieder
aktivieren** direkt am Schalter, ein manueller Start, **Neue Session** oder erneutes Einschalten der
Option geben ihn wieder frei. Fehlende OCR-Sprachpakete, ungültige Kalibrierung,
ungesicherte Einstellungen und noch laufende abgebrochene OCR blockieren die
Automatik; Fehler sind in der Statusanzeige sichtbar.

## Ablauf und Ressourcen

- Ein `SetWinEventHook` mit `WINEVENT_OUTOFCONTEXT` beobachtet Vordergrundwechsel.
  Sein Thread schläft in der Windows-Nachrichtenschleife. Bei inaktivem oder
  minimiertem BDO werden keine Bilder aufgenommen. Ein laufender Burst gibt die
  Fensteraufnahme bei Fokusverlust frei.
- In Bereitschaft wird höchstens einmal pro Sekunde ein Spielfensterbild gelesen.
  Die native Aufnahme wird nach jedem solchen Bild vollständig beendet. Es läuft
  kein WGC-Stream mit hoher Bildrate im Hintergrund zwischen den Prüfungen.
- Der rein verwaltete Bildvergleich liest nur die kalibrierte Textzeile des
  neuesten normalen Loots. Lokaler Kontrast und Glyphstruktur liefern einen
  Verdacht; bewegter Hintergrund allein bestätigt keinen Grind. Das erste Bild
  und Geometriewechsel bilden ausschließlich eine neue Vergleichsbasis.
- Ein Verdacht erlaubt bis zu sechs Sekunden normale Loot-OCR, mit mindestens
  200 ms Abstand nach jeder Analyse. Nach einem erfolglosen Versuch folgen
  mindestens 15 Sekunden ohne neue OCR-Versuche. Ein einzelner OCR-Aufruf hat
  zusätzlich eine Grenze von fünf Sekunden.
- Ein vorhandenes Log reicht nicht: Zwei spätere Ankünfte müssen sowohl die
  physische Dropzahl als auch bekannten Monster-Trash erhöhen. Mengenrevisionen,
  globale Items, Eventloot und direkte Silberdrops allein lösen keinen Start aus.
- Höchstens drei aktuelle Bilder mit zusammen maximal 128 MiB werden im RAM
  gepuffert. Nach Bestätigung übernimmt der Live-Reconciler diese Bilder in
  zeitlicher Reihenfolge über seine bestehende begrenzte Warteschlange und
  OCR-Zeitbegrenzung. Es gibt keine separate Übernahme von Probe-Lootsummen.
- Reagiert native OCR nicht auf Abbruch, bleiben genau ihr Analyzer und ihre
  Bilddaten bis zu ihrem Ende erhalten. Weitere automatische oder manuelle Starts
  warten auf dieses Ende; ein erneutes Aktivieren erzeugt keine weiteren Worker.

## Grenzen und Prüfung

Die Bereitschaft verwendet weiterhin einzelne vollständige Fensteraufnahmen;
lediglich der Bildvergleich arbeitet auf der kleinen Textregion. Eine garantierte
CPU-/GPU-Prozentzahl lässt sich daraus nicht ableiten. Auf Windows mit laufendem
BDO müssen Verbrauch, wechselnde HDR-/UI-Einstellungen und die Erkennungsrate
praktisch gemessen werden.

Sehr kurze erste Drops oder pixelidentische Meldungen zwischen den Stichproben
können fehlen. Der begrenzte Puffer kann ältere Startmeldungen verdrängen. Die
aktive Sessionzeit beginnt wie beim manuellen Start mit dem ersten im Livepfad
bestätigten Drop, ohne rückwirkend geschätzte Sekunden.

Portable Tests prüfen die Bestätigungslogik, Glyphstruktur und Einstellungs-/UI-
Verträge. Windows-Tests prüfen zusätzlich native Vordergrundereignisse,
Aufnahmelifecycle, Abbruch, Bildübergabe und Sessionsteuerung. Ein Cross-Build auf
macOS ersetzt diese Windows-Laufzeittests nicht.
