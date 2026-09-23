# Automatischer Grindstart

In der **Live-Session** neben **Tracking starten** und **Neue Session** wird mit
**Grind automatisch erkennen** die standardmäßig ausgeschaltete Automatik ein-
oder ausgeschaltet. Beim ersten erkannten Monsterdrop startet die Session samt
Uhr sofort. Eine neue automatische Session bleibt bis zu fünf getrennten
Drop-Ankünften vorläufig: Nach einer vollen Minute ohne neuen Drop wird sie
verworfen, und die Automatik wartet wieder auf einen Grind. Jeder neue Drop setzt
diese Minute zurück. Wiederholte OCR-Lesungen, mehrere Items derselben Ankunft
und Mengenkorrekturen zählen nicht als getrennte Drops.
Eindeutige Rotationsmeldungen verkürzen das: Zeigt der Spot im Wartezustand seine
unverwechselbare Startmeldung (Aphrodon `A golden fragrance rides the wind.`,
Magaia `The sinners are summoned.`), merkt sich die Automatik die Meldung für drei
Minuten. Der nächste erkannte Trashloot startet die Session dann sofort als bestätigt,
ohne die fünf Drops abzuwarten, und die gemerkte Meldung ist damit verbraucht. Die
Meldung allein startet nie eine Session: Man kann an einem Spot vorbeilaufen und sie
auslösen; erst der Trashloot belegt, dass wirklich gegrindet wird. Der Wartezustand
liest dafür alle drei Sekunden denselben Meldungsausschnitt wie der Rotation Monitor,
jeden Ausschnitt nur einmal für alle Spots, die ihn teilen.

Unverwechselbar ist eine Startmeldung nur, wenn die Rotation sie nicht selbst als
Schritt oder Hintergrundmeldung benutzt. Hermesias Opfergabe eröffnet zwar die
Rotation, erscheint darin aber rund zwanzigmal und sagt deshalb nichts über den
Rotationsbeginn; Event Horizon startet ohne Meldung. Beide Spots melden hier nichts.

Die so erkannte Meldung wird beim Sessionstart an den Rotation Monitor übergeben:
Die erste Rotation wird ab ihrem Banner gemessen, nicht erst ab dem Trashloot, der
die Session bestätigt hat. Der Spot ist zu diesem Zeitpunkt meist noch unbekannt,
deshalb übernimmt sie das vorläufige Profil des gemeldeten Spots. Dasselbe Profil
übernimmt der erkannte Spot später unverändert: Solange ein Frame den Spot noch nicht
kennt, folgt der Rotation Monitor der Erkennung dieses Frames statt einem noch leeren
Sessionspot — ein Rückfall auf „unbekannt" würde die laufende Rotation verwerfen.

Ab der fünften Ankunft wird die Session gespeichert und verwendet die normale
Auto-Pause-Einstellung. Vorher werden weder Verlauf noch Wiederherstellungspunkt
gespeichert oder Garmoth-Uploads erlaubt. Manuell gestartete und bereits bestehende
Sessions bleiben erhalten; eine automatisch pausierte Session wird fortgesetzt.
Ein Spot- oder Klassenwechsel benötigt weiterhin
**Neue Session**.

Nur der Schalter schaltet die automatische Erkennung aus. Eine manuelle Pause
stoppt die Session; solange der Schalter aktiviert bleibt, überwacht die Automatik
anschließend wieder neue Drops und setzt eine bestehende Session fort. Auch
**Neue Session** und ein App-Neustart benötigen keine separate Reaktivierung.
Alte gespeicherte Automatik-Pausen werden ignoriert.

Nach einem Erkennungs- oder Startfehler erfolgt frühestens nach zehn Sekunden
ein neuer Versuch. Fehlende OCR-Sprachpakete, ungültige Kalibrierung,
ungesicherte Einstellungen und noch laufende abgebrochene OCR verhindern weiterhin
eine Erfassung; nach Behebung arbeitet die eingeschaltete Automatik weiter.

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
- Nach einer visuellen Änderung reicht das erste positive OCR-Ergebnis mit
  bekanntem Monster-Trash für den vorläufigen Start. Ein unverändertes vorhandenes
  Log löst keinen visuellen Verdacht aus. Globale Items, Eventloot und direkte
  Silberdrops allein lösen keinen Start aus.
- Höchstens drei aktuelle Bilder mit zusammen maximal 128 MiB werden im RAM
  gepuffert. Nach Bestätigung übernimmt der Live-Reconciler diese Bilder in
  zeitlicher Reihenfolge über seine bestehende begrenzte Warteschlange und
  OCR-Zeitbegrenzung. Es gibt keine separate Übernahme von Probe-Lootsummen.
- Reagiert native OCR nicht auf Abbruch, bleiben genau ihr Analyzer und ihre
  Bilddaten bis zu ihrem Ende erhalten. Weitere automatische oder manuelle Starts
  warten auf dieses Ende; Wiederholungsversuche erzeugen keine weiteren Worker.

## Grenzen und Prüfung

Die Bereitschaft verwendet weiterhin einzelne vollständige Fensteraufnahmen;
lediglich der Bildvergleich arbeitet auf der kleinen Textregion. Eine garantierte
CPU-/GPU-Prozentzahl lässt sich daraus nicht ableiten. Auf Windows mit laufendem
BDO müssen Verbrauch, wechselnde HDR-/UI-Einstellungen und die Erkennungsrate
praktisch gemessen werden.

Sehr kurze erste Drops oder pixelidentische Meldungen zwischen den Stichproben
können fehlen. Der begrenzte Puffer kann ältere Startmeldungen verdrängen. Die
aktive Sessionzeit beginnt unmittelbar mit dem automatischen Start nach dem
ersten erkannten Monsterdrop, ohne rückwirkend geschätzte Sekunden.

Portable Tests prüfen die Bestätigungslogik, Glyphstruktur und Einstellungs-/UI-
Verträge. Windows-Tests prüfen zusätzlich native Vordergrundereignisse,
Aufnahmelifecycle, Abbruch, Bildübergabe und Sessionsteuerung. Ein Cross-Build auf
macOS ersetzt diese Windows-Laufzeittests nicht.
