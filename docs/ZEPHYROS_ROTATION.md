# Zephyros Rotation Monitor

Stand: zusammen mit den EN/DE/FR/SP-Übersetzungen für den lokalen Main-Stand freigegeben.

## Ablauf

Das Profil erfasst den belegten normalen Hatchery-Zyklus in vier Abschnitten:

1. **Wellen:** Die Aktivierung der Brutstätte startet die Messung.
2. **Schattenritter und Wellen:** Das Bannerpaar zur freigesetzten Energie und
   zum Schattenritter öffnet einen gemeinsamen Abschnitt. Einzelne Rittertode
   und die dazwischenliegenden Packs haben keine separat belegten Bannergrenzen.
3. **Beelzebub:** Die Ankunft des Bosses beginnt den Bossabschnitt.
4. **AFK:** Nach dem Boss kündigen zwei Banner die schwächer werdende Energie
   und deren verbleibenden Ausstoß an. Die erneute Bereitschaft beendet die Rotation.

Ein weiterer Turmschlag startet den nächsten Lauf. Bereitschaft allein startet
keinen neuen Lauf. Loot startet dieses Profil nicht. Die zwei Banner eines
Übergangs zählen gemeinsam als ein Ereignis. Es gibt keine angenommenen
Wellenzähler, festen Dauern oder Special Events.

## Aufnahmebeleg

Quelle: [LoonyStorm, Zephyros / Succ Wiz, 27. Mai 2026](https://www.youtube.com/watch?v=UtAX9-qc-RY),
Abruf und Auswertung am 27. September 2026. Das heruntergeladene Video dauert
3.683 Sekunden; untersucht wurde die englische 1920×1080-Fassung mit 60 Bildern/s.
Alle zwei Sekunden wurde ein Bild für die Offline-OCR gelesen. Ausgewählte
Übergänge wurden zusätzlich visuell und mit der tatsächlichen Profil-OCR geprüft.
Die lange Aufnahme zeigt fünf vollständige Zyklen und einen weiteren Beginn.
Das ist keine Prüfung jedes Einzelbildes auf Schnitte.

Die folgenden Zeiten sind erste erkannte Zweisekunden-Stichproben, keine
bildgenauen Startzeiten:

| Zyklus | Aktivierung | Ritter/Wellen | Beelzebub | AFK | Bereit |
| --- | --- | --- | --- | --- | --- |
| 1 | [0:14](https://www.youtube.com/watch?v=UtAX9-qc-RY&t=14s) | 2:16 | 9:08 | 9:38 | [11:48](https://www.youtube.com/watch?v=UtAX9-qc-RY&t=708s) |
| 2 | 11:52 | 13:54 | 20:54 | 21:26 | 23:36 |
| 3 | 23:38 | 25:42 | 32:38 | 33:08 | 35:18 |
| 4 | 35:22 | 37:24 | 44:14 | 44:44 | 46:54 |
| 5 | 46:58 | 49:00 | 56:00 | 56:34 | 58:44 |

Die gemessenen Gesamtzeiten liegen zwischen 692 und 706 Sekunden. In diesen
Stichproben dauert AFK jeweils 130 Sekunden. Beides bleibt eine Beobachtung
zu dieser Aufnahme und wird nicht als fester Timer programmiert.
Der [Outer-Edania-Guide](https://www.blackdesertfoundry.com/edania-monster-zones-guide/)
(aktualisiert 12. August 2026) beschreibt denselben groben Normalablauf.

## Erkennung und Grenzen

Sieben Referenztexte bilden fünf Ereignisarten und vier sichtbare Abschnitte.
Alle sieben liegen in EN, DE, FR und SP vor; die 28 Regeln verwenden eindeutige
Suchphrasen. Start, Ende und Reihenfolge des Normalablaufs sind belegt: Klasse A.

- Gemeinsamer `BannerStack`, keine Änderung der bisherigen Crops.
- Die tatsächliche Windows-OCR liest Start, Ritterphase, Boss, AFK und Ende
  aus den geprüften englischen Videobildern im gemeinsamen Ausschnitt.
- DE/FR/SP sind gegen die Referenztexte getestet. Für diese Sprachen sind Crop,
  Umbrüche und OCR-Qualität noch nicht mit Spielaufnahmen geprüft.
- Vier weitere Rückzugs-/Abbruchtexte sind in dieser Aufnahme nicht belegt und
  werden nicht als aktive Trigger registriert. Keine separate Failure-Meldung
  wird behauptet. Fehlende Pflichtübergänge, Reihenfolgefehler und Unterbrechungen
  bleiben über die gemeinsame Plattform unvollständig bzw. abgebrochen.
- Für Tod, Verlassen des Bereichs und abgelaufene Mechanik ist weiterhin eine
  gezielte Gegenaufnahme nötig. Ein solcher Versuch darf keine Bestzeit bilden.
- Region im Bild: EU. Patchnummer und ursprüngliche UI-Skalierung sind nicht
  belegt; Veröffentlichungsdatum ist nicht gleich Patchstand.

## Tests

`ZephyrosRotationTests` prüft alle sieben englischen Volltexte, ähnliche und
nicht registrierte Texte, Bannerpaare, fünf aufgezeichnete Zyklen, Pflichtlücken,
Einstieg beim Boss, Unterbrechung und Neustart. Die gemeinsamen Sprachtests
prüfen zusätzlich alle 28 Sprachfassungen.

Der optionale Aufnahme-Test benötigt `ZEPHYROS_RECORDING_DIRECTORY` mit
`UtAX9-qc-RY-direct.mp4`. Videos und OCR-Arbeitsdateien liegen außerhalb des
Repositories. Der Test verwendet ausschließlich diese Datei und Windows OCR.

Prüflauf vom 27. September 2026: Solution-Build mit 0 Warnungen/Fehlern;
Gesamttest 13.385 bestanden, 4 fehlgeschlagen, 2 übersprungen. Der Aufnahme-Test
war aktiviert und bestanden. Die vier Fehler wurden bereits auf dem unveränderten
Ausgangscommit `ef08fc1` reproduziert:

- `ResolvedSpotVariantIsUsedWhenTheSessionIsCompleted`: drei Sammelprofil-Varianten.
- `InitiallyUnreadableCronStaysBaselineAndOnlyNewBoonIsChargedAcrossPauseAndRestore`.

Der Nutzer hat den lokalen Commit auf Main trotz dieser bekannten Ausgangsfehler
ausdrücklich beauftragt. Sprachintegration und Zephyros werden wegen ihrer
gemeinsamen Registrierung zusammen übernommen. Es erfolgt kein Push.
