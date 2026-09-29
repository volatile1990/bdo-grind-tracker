# Rotation Monitor: zusätzliche Spots

Stand und Abrufdatum: **27. September 2026**.

## Ergebnis und Prüfkriterium

36 konkrete, zuvor nicht registrierte Spots geprüft: **1 A, 27 B, 8 C**.
Zephyros ist implementiert und für den lokalen Main-Stand freigegeben. Die vier Ausgangsprofile sind
Hermesia, Aphrodon, Magaia und Event Horizon. Die Registrierung stimmt mit dem
Ausgangsbestand der Kandidatenliste überein. Die drei Sammelprofile
`dark-energy-floodlands`, `dehkia-ash-forest-unspecified` und
`winter-tree-fossil-unspecified` sind keine zusätzlichen Kandidaten.

- **A:** Jede Pflichtphase, ihre Reihenfolge und ihre Banner sowie Start und Ende
  sind eindeutig belegt. Nur A wird implementiert.
- **B:** Ablauf oder Bannerzuordnung unvollständig. Fehlende Belege ergeben B.
- **C:** Der dokumentierte Betrieb besteht aus Feldgrind bzw. unabhängigen
  Ereignissen; daraus ergibt sich keine feste Gesamtrotation für diese Plattform.

Die Klassen sind die Bewertung dieser Recherche, keine Behauptung über sämtliche
Spielversionen. Nach zusätzlicher Freigabe wurden Stundenaufnahmen per Kommandozeile
heruntergeladen und offline ausgewertet. Drei englische Videos wurden vollständig
in Zweisekunden-Stichproben per OCR gelesen; ausgewählte Übergänge wurden visuell
geprüft. Keine Prüfung jedes Einzelbildes auf Schnitte. Titel und Suchtreffer allein
gelten nicht als Ablaufbeleg. Videos und Arbeitsdateien bleiben außerhalb des Repositories.

**M** zählt die verfügbaren Meldungseinträge einschließlich unsicherer Kandidaten,
nicht verschiedene sichtbare Texte oder bestätigte Ereignisse. **P** ist die Zahl
vollständig belegter Pflichtphasen: `offen` bei B, `—` bei C. „Ergänzt“ ist nur bei
Zephyros **ja**, sonst **nein**. Die B-Tabellen nennen mögliche Textbezüge als
Arbeitshypothesen; daraus wurden keine Trigger oder Zeitgrenzen gebaut.

## Edania und Gavinya

| Spot / ID | Klasse | M / P | Belegter Ablauf; Hauptquelle | Bannerzuordnung, offene Punkte und benötigte Aufnahme |
| --- | --- | --- | --- | --- |
| Aetherion Castle · `aetherion` | B | 11 / offen | Turm → Kreise/Geister → Black Wings → Muraka → Turm erneut. [E1] | Wurzel-, Black-Wings- und Muraka-Texte passen inhaltlich. Zahl der Wiederholungen und reguläres Ende gegenüber Abbruch offen. Zwei vollständige Turmzyklen samt auslaufender Pause aufnehmen. |
| Nymphamaré Castle · `nymphamare` | B | 6 / offen | Schamanen/Turm → Boss → Spire → Bubble/Fisch. [E1] | Kontamination, Reinigung und Dreamfish sind Kandidaten. Vollständige Start-/Endgrenzen und Bossübergänge fehlen. Aufnahme ab unberührtem Turm bis zur nächsten Aktivierung, einschließlich Aufstieg. |
| Orbita Castle · `orbita` | B | 24 / offen | Zentraler Turm → drei dünne, drei dicke Säulen → Wellen → Titan → Pause. [E1], [V3] | V3 zeigt drei Sacred- und drei Corrupted-Golems, Rift, Titan und Wiederbeginn. Aufnahme von 2025: Textvarianten und 54–56 s Reinigung widersprechen teilweise dem neueren Guide. Aktuelle englische Aufnahme und Reparatur-/Reset-Gegenprobe fehlen. |
| Tenebraum Castle · `tenebraum` | B | 15 / offen | Vier Türme mit je drei Wellen; Manticore bei 66 %, 33 % und Tod; Pause. [E1], [V2] | V2 belegt vier Turmfolgen, Bosskontrolle und sechs Befreiungen. Stärkere Kontrolle beendet den Versuch dort nicht. Kein eindeutiges AFK-Endbanner gefunden; nächster Turmschlag darf nicht als Ende der eigentlichen Pause gelten. Natürliches Pausenende und Abbruch gezielt aufnehmen. |
| Zephyros Castle · `zephyros` | A | 11 verfügbar, 7 verwendet / 4 | Aktivierung → Wellen → Ritter/Wellen → Beelzebub → AFK → Bereitschaft. [V1], [E1] | Fünf vollständige Zyklen belegen den Normalablauf. Bannerpaare ergeben je ein Ereignis. Vier ungeprüfte Abbruchkandidaten bleiben ausgeschlossen. EN/DE/FR/SP-Profil implementiert; [Details](ZEPHYROS_ROTATION.md). |
| Aresion Temple · `aresion` | B | 43 / offen | Der Spotabschnitt enthält keine vollständige Mechanikbeschreibung. [E2] | Fünf Flammen-/Brazier-Kandidaten, 38 weitere unsichere Bosskandidaten. Kein belegter Pflichtpfad. Grindaufnahme ab Aktivierung durch zwei Zyklen; Wochenboss getrennt halten. |
| Scales of Judgment · `scales-of-judgment` | B | 15 / offen | Der Spotabschnitt enthält keine vollständige Mechanikbeschreibung. [E2] | Judgment, Gleichgewicht, Chaos und Reset erscheinen plausibel, Reihenfolge und Wiederholungen unbestätigt. Erfolgreichen Lauf, fehlgeschlagene Prüfung und erneute Aktivierung aufnehmen. |
| Gavinya Coastal Cliff · `gavinya-coastal-cliff` | B | 7 / offen | Offizielle Einführung bestätigt Schwefelgolems, keinen vollständigen Ablauf. [G1] | Hornstone-Absorption mit Erfolg/Gegenmaßnahme, Stalagmit und Schwarm sind Kandidaten. Start, Pflicht-/Bonuszweige und Abschluss offen. Aufnahme mit zerstörtem und unzerstörtem Hornstone samt erneutem Spawn. |

## Weitere Regionen

| Spot / ID | Klasse | M / P | Belegter Ablauf; Hauptquelle | Bannerzuordnung, offene Punkte bzw. Ausschlussgrund |
| --- | --- | --- | --- | --- |
| Star’s End · `stars-end` | C | 5 / — | Aktueller Umbau: beweglicher Packgrind mit Objekt- und Explosionsmechanik. [S1] | Kein fester Gesamtzyklus. Zwei Kandidaten betreffen ausdrücklich Vessel of Inquisition; drei weitere sind unsicher. Der alte Vessel-Bereich darf nicht als Ablauf des umgebauten Feldspots übernommen werden. |
| Sycraia Abyssal Ruins (Lower) · `sycraia-abyssal-ruins-lower` | B | 12 / offen | Im Kampf kann ein mehrstufiges Memory-Ereignis entstehen. [S2] | Alle zwölf Kandidaten mehrdeutig; Upper/Lower, alte/neue Gegner sowie mehrfach identischer Failure-Text offen. Aufnahme im nachweislich neuen Lower-Bereich vom Auftreten bis Ende, einschließlich eines Fehlschlags. |
| Elvia Orzekea · `elvia-orzekea` | B | 10 / offen | Kämpfe steigern Alarm, finale Alarmstufe ruft Alketa. [O1] | Alarm 1/2/3/final und Alketa passen; Psyche, Neuaufstellung, Abschluss und Wiederbeginn nicht vollständig belegt. Grindaufnahme bis nach Psyche-Zerstörung und erneutem Alarm 1; kein Dungeonbosslauf. |
| Tungrad Ruins · `tungrad-ruins` | C | 6 / — | Packweise Visionary bekämpfen, danach geschwächte Gegner. [U1] | Packereignisse und Putarek-Meldungen bilden keinen belegten festen Gesamtzyklus. Einzelne Events wären ein anderer Funktionsumfang als diese Rotationsdefinition. |
| Darkseekers’ Retreat · `darkseekers-retreat` | B | 30 / offen | Guide beschreibt Feldgrind mit Ember-/Artifact-Ereignis. [U1] | Der Guide deckt die 18 Light’s-Resonance-Kandidaten nicht ab; zwölf weitere Zuordnungen unsicher. Daher kein pauschales C. Aktuellen Bereich und Light’s-Resonance-Ablauf bis Reset aufnehmen, alte Artifact-Ereignisse getrennt kennzeichnen. |
| Fortunate Golden Pig Cave · `fortunate-golden-pig-cave` | B | 17 / offen | Fünf Räumungen → König → Auswurf; Zugang mit zusätzlichem Zufallsereignis. [P1] | Alle Zuordnungen zur Variante unsicher; Zeitangaben widersprüchlich. Fortunate-Eintritt, alle Räumungen, König, Auswurf und Bonusereignis getrennt aufnehmen. |
| Unlucky Golden Pig Cave · `unlucky-golden-pig-cave` | B | 16 / offen | Fünf Räumungen → König → Auswurf. [P1] | Dieselbe Bannerfamilie wie Fortunate; Pflichtwellen nicht jeweils eindeutig markiert. Unlucky-Eintritt bis Auswurf sowie Timeout aufnehmen; keine feste Dauer übernehmen. |
| Winter Tree Fossil (280) · `winter-tree-fossil-280` | B | 7 / offen | Interaktion am Fossil startet Wellen; zwei Schwierigkeitsgrade. [W1] | Sieben Kandidaten nicht sicher der 280-Variante zugeordnet. Hard-Auswahl, sämtliche Wellen und Ende aufnehmen; zweiten Start und Ice-Spirit-Abbruch einschließen. |
| Yzrahid Highlands · `yzrahid-highlands` | B | 16 / offen | Wiederkehrende Beinfolge; Entladung setzt auf Anfangsbein zurück. [Y1] | 30/60/90-%-Texte passen zur Entladung; Kern-, Elite- und Beinübergänge nicht lückenlos zugeordnet. Aufnahme über zwei Entladungen mit allen Bein-/Kernphasen und sichtbarem Bannerstapel. |
| Elvia Hexe Sanctuary · `elvia-hexe-sanctuary` | B | 6 / offen | Seelen sammeln → Witmirth → weitere Untote → Hexe Marie. [C1] | Witmirth- und Marie-Texte passen, doch Akkumulation und Wiederbeginn haben keinen vollständig zugeordneten Bannerpfad. Zwei vollständige Ereignisse inklusive Rückkehr zum normalen Grind aufnehmen. |
| Elvia Quint Hill · `elvia-quint-hill` | C | 2 / — | Troll-HP aktiviert nahe Trolle; Schamane versteinert lokal. [C1] | Beide Texte sind lokale Kampfreaktionen, kein gemeinsamer Start-/Endzyklus des Spots. |
| Dokkebi Forest · `dokkebi-forest` | B | 11 / offen | Kkebidol/Ritual → Duoksini → Ruhe. [D1] | Nur zwei Feldtextkandidaten; neun Duoksini-/Gumiho-Dialoge nicht sicher dieser Mechanik zugeordnet. Aufnahme ab Kkebifire bis zur nächsten Aktivierung; Bossdialoge nicht ungeprüft übernehmen. |
| City of the Dead · `city-of-the-dead` | C | 5 / — | Messenger-Cast im Pack unterbrechen, geschwächte Gegner beseitigen. [U1] | Packbezogene Cast-/Commander-Meldungen ergeben keine feste Gesamtrotation. |
| Jade Starlight Forest · `jade-starlight-forest` | C | 10 / — | Packroute, Lampen/Braziers, Explosionen und mögliche Elites. [W1] | Die Objekt- und Elite-Texte hängen vom gewählten Pack ab. Kein fester Bannerpfad mit Gesamtende. |

## Dehkia

| Spot / ID | Klasse | M / P | Belegter Ablauf; Hauptquelle | Bannerzuordnung und benötigter Beleg |
| --- | --- | --- | --- | --- |
| Gyfin Rhasia Temple (Upper) · `dehkia-gyfin-rhasia-temple-upper` | B | 10 / offen | Laterne am Upper-Standort; Guide ohne vollständige Mechanikfolge. [L1] | Trial/Crusher/void-Texte mehrdeutig. Drei-Spieler-Lauf mit normaler und finaler Prüfung, Erfolg/Fehlschlag, Stabilisierung und Reset aufnehmen. |
| Mirumok Ruins · `dehkia-mirumok-ruins` | B | 9 / offen | Laterne am ausgewiesenen Standort; Guide ohne vollständige Mechanikfolge. [L1] | Old-Mirumok-Auftritt, Absorption, Schwächung und Tod plausibel. Absorptionszweige, Wellenzahl und Endgrenze offen. Vollständigen Zyklus mit erfolgreicher und verhinderter Absorption aufnehmen. |
| Ash Forest II · `dehkia-ii-ash-forest` | B | 5 / offen | Gairas-Rückzug; kleinere Gegner; mögliche Wiederkehr. [L2] | Aura, Gairas, dispersed und silence sind Kandidaten, exakter Anfang/Abschluss offen. Zwei II-Zyklen mit sichtbarer Laternenstufe und Gairas aufnehmen. |
| Olun’s Valley II · `dehkia-ii-oluns-valley` | B | 7 / offen | Golem-/Armmechanik; erster und spätere Golems können abweichen. [L2], [L3] | Sieben Texte nicht sicher auf Stufen verteilt. Aufnahme vom Einschalten der II-Laterne über ersten und mindestens zwei folgende Golems nötig. |
| Thornwood Forest · `dehkia-thornwood-forest` | B | 4 / offen | Ahib strömen; Dark Knight nach Kills; weitere Ahib. [L1] | Alle vier Kandidaten mehrdeutig; Abschluss und Bonus-/Pflichtrolle des Ritters offen. Aufnahme zweier Ritterereignisse samt Eye-of-Despair-Übergängen. |
| Cadry Ruins · `dehkia-cadry-ruins` | B | 10 / offen | Laterne/Kanone → Soldaten → Commander. [L1] | Dehkia- und allgemeine Commander-Kandidaten vermischt; Absorptionszweig und Reset offen. Aktiven Laternenlauf mit Kugel-Erfolg und -Fehlschlag aufnehmen. |
| Ash Forest I · `dehkia-ash-forest` | B | 4 / offen | Rift Seed → Geister; Barnas/Volkras reagieren aufeinander. [L1] | Vier Kandidaten nicht sicher stufenspezifisch. Zwei I-Zyklen mit Spawn, Split und Rückkehr zum Seed aufnehmen; kein II-Material zuordnen. |
| Crescent Shrine · `dehkia-crescent-shrine` | B | 7 / offen | Laterne → Saunil um Chief Gatekeeper. [L1] | Obsidian-, Verstärkungs- und Berserk-Kandidaten plausibel, Pflichtfolge und Abschluss offen. Zwei Gatekeeper-Zyklen mit zerstörter und stehen gelassener Obsidian Energy aufnehmen. |
| Cyclops Land · `dehkia-cyclops-land` | B | 7 / offen | Cyclops, Nahrung/Heilung und reagierende Gargoyles. [L1] | Auge, Nahrung und Lebensabsorption sind keine eindeutig geordnete Pflichtkette. Zwei Spawns, Gegenmaßnahme und zugelassene Heilung samt Tod/Respawn aufnehmen. |
| Tunkuta · `dehkia-tunkuta` | B | 3 / offen | Turos; Ulutuka erscheint nach Kills erneut. [L1] | Drei Chaos-/Ankunftstexte ohne eindeutiges Ende. Laterne einschalten, zwei Ulutuka-Ereignisse und Wiederkehr vollständig aufnehmen. |
| Hystria Ruins · `dehkia-hystria-ruins` | B | 4 / offen | Turm → Waffen/Tutuka → Elten bei hohem Alarm. [L1] | Extermination/Safeguard/Emergency-Texte nicht sicher jedem Wechsel zugeordnet. Aufnahme ab Aktivierung über Elten und erneuten Alarm, einschließlich nicht unterbrochener Mechanik. |

## Floodlands

| Spot / ID | Klasse | M / P | Belegter Betrieb; Hauptquelle | Ausschlussgrund |
| --- | --- | --- | --- | --- |
| Great Red Sea · `dark-energy-floodlands-great-red-sea` | C | 13 / — | Packroute mit Orbs und gelegentlichem Boss. [E1] | Freie Route statt festem Gesamtzyklus. Gemeinsame Boss-/Energietexte bestätigen keine Pflichtreihenfolge und keine eindeutige Gebietszuteilung. |
| Orbita · `dark-energy-floodlands-orbita` | C | 13 / — | Packroute mit Orbs und gelegentlichem Boss. [E1] | Derselbe Ausschlussgrund; Titan wird nicht allein aus dem Gebietsnamen als Pflichtphase angenommen. |
| Zephyros · `dark-energy-floodlands-zephyros` | C | 13 / — | Packroute mit Orbs und gelegentlichem Boss. [E1] | Derselbe Ausschlussgrund; Beelzebub wird nicht allein aus dem Gebietsnamen als Pflichtphase angenommen. |

## Ausgewertete Stundenaufnahmen

Abruf und Offline-Auswertung am 27. September 2026. Download mit yt-dlp,
Videoanalyse mit OpenCV und Windows OCR, ohne Computer Use. Die ausgewerteten
Fassungen haben 1920×1080 Pixel bei 60 Bildern/s. Veröffentlichungsdatum und Titel
belegen weder Patchstand noch Schnittfreiheit.

| Quelle | Datum / Dauer | Beobachtung und Grenze |
| --- | --- | --- |
| [V1: Zephyros, Succ Wiz](https://www.youtube.com/watch?v=UtAX9-qc-RY) | 2026-05-27 / 61:23 | Fünf vollständige Normalzyklen. Erster Start [0:14](https://www.youtube.com/watch?v=UtAX9-qc-RY&t=14s), Ritter 2:16, Boss 9:08, AFK 9:38, Bereitschaft [11:48](https://www.youtube.com/watch?v=UtAX9-qc-RY&t=708s), Neustart 11:52. Sieben Texte, fünf Ereignisarten, vier Abschnitte; Klasse A für diesen Normalablauf. |
| [V2: Tenebraum, Awk Drakania](https://www.youtube.com/watch?v=fMNMvYq1jao) | 2026-07-25 / 61:11 | Vier Turmstarts 0:50 / 1:54 / 2:52 / 3:54, Manticore 4:56, Kontrollphasen 5:22 und 7:22, Befreiung [9:28](https://www.youtube.com/watch?v=fMNMvYq1jao&t=568s), nächster Turm 10:50. Sechs Befreiungen insgesamt. Bei [19:04](https://www.youtube.com/watch?v=fMNMvYq1jao&t=1144s) folgt auf stärkere Kontrolle dennoch Erfolg 19:32. AFK-Ende nicht durch einen eigenständigen Banner belegt. |
| [V3: Orbita, Jonlaw](https://www.youtube.com/watch?v=nWk93-d9g4k) | 2025-08-23 / 68:00 | Fünf sichtbare Abschlüsse. Erster Start 1:52, Sacred-Golems 2:54 / 3:30 / 4:16, Wechsel 7:14, Corrupted-Golems 8:18 / 9:06 / 9:52, Rift 12:34, Titan 14:06, Reinigung [15:32](https://www.youtube.com/watch?v=nWk93-d9g4k&t=932s), Bereitschaft [16:28](https://www.youtube.com/watch?v=nWk93-d9g4k&t=988s). Alter Stand und abweichende Texte/Pause verhindern die ungeprüfte Übernahme als aktuelles Profil. |

Die ebenfalls heruntergeladene [Orbita-Seraph-Aufnahme](https://www.youtube.com/watch?v=xs1vFyNq5IQ)
(2026-02-10, 62:20) wurde nur auf Eignung geprüft. Die sichtbare Sprache erscheint
portugiesisch und gehört nicht zu den vier vorhandenen Sprachfassungen.
Daraus wurde keine neue Übersetzung abgeleitet.

Bei V2 stehen die Banner oben im Bild; der Standardausschnitt in der unteren
Bildmitte würde sie verpassen. V1 wurde zusätzlich mit dem unveränderten
Produkt-Crop und dem tatsächlichen OCR-Pfad erfolgreich geprüft. Ein passender
Analyse-Crop allein wäre noch kein Nachweis für die Laufzeiterkennung.

## Grenzen, Widersprüche und Zeitangaben

- Jede B-Zeile nennt den noch fehlenden Ablaufbeleg. Auch bei plausibler
  Textbedeutung bleiben Anzahl, Reihenfolge, optionaler Zweig, Failure und
  Wiederbeginn offen, soweit sie nicht ausdrücklich belegt sind. Ein fehlendes
  Failure-Banner wird nicht durch einen generischen Text ersetzt.
- E1 nennt ungefähre Pausen von drei Minuten für Orbita, einer für Tenebraum und
  zwei für Zephyros. Diese Guide-Schätzungen wurden **nicht** als Timer verwendet.
  Für andere B-Spots ist hier keine belastbare typische Dauer ermittelt.
- P1 (2025) nennt 25/70 Minuten. Vorhandene Textkandidaten enthalten 25/60 Minuten.
  Eintrittslimit, Kampfzeit und Variante sind nicht aufgelöst; keine Dauer gilt
  dadurch als aktuell verifiziert.
- O2 beschreibt entfernten Normalmodus; O1 führt trotz neuerem Änderungsdatum
  weiterhin drei Eintrittsmodi auf. Das Seitendatum allein löst den Widerspruch
  nicht. Die Bossphasen aus O1 gehören zur Dungeoninstanz und werden nicht zur
  Alarmrotation des Grindbereichs gemacht.
- L1 mischt Stufe-I-/II-Beschreibungen. L3 dokumentiert einen Global-Lab-Stand
  mit verändertem ersten Golem; dessen Übernahme in den heutigen Live-Stand ist
  hier nicht bestätigt. Deshalb keine Übertragung der Armfolge auf Olun II.
- S1 trennt den umgebauten Feldspot vom alten Vessel-Bereich. S2 trennt neue
  Ancient-Gegner von weiter vorhandenen alten Gegnern. Diese Varianten bleiben
  bei der Bannerzuordnung ausdrücklich getrennt.
- Bei Aresion und Scales ersetzen Wochenboss-Anleitungen keine Grindbelege.
  Bei Darkseekers fehlen Belege für die zusätzliche Resonance-Familie; eine alte
  Feldgrindbeschreibung reicht nicht, um sie auszuschließen.

## Priorität für neue Aufnahmen

1. **Orbita und Tenebraum:** Orbita im aktuellen Stand gegen V3 abgleichen;
   bei Tenebraum natürliches AFK-Ende von manuellem Neustart trennen.
   **Zephyros:** Verlassen/Tod/Timeout und DE-/FR-/SP-Aufnahmen ergänzen.
2. **Orzekea, Yzrahid, Mirumok, Gyfin Upper:** Zyklus einschließlich Sonderzweigen,
   Abbruch und Wiederbeginn; Varianten/Modus zuerst im Bild zeigen.
3. **Aetherion, Nymphamaré, Aresion, Scales, Gavinya:** lückenlose Aufnahme ab
   Aktivierung, keine Schnitte über Mechanikwechsel, zwei komplette Versuche.
4. **Übrige B-Spots:** die in der jeweiligen Zeile genannte Gegenprobe durchführen.

Für jede Aufnahme: Datum/Region/Patch, konkrete Spotvariante, Spielsprache,
Auflösung und UI-Skalierung festhalten. Bannerstapel und relevante Namensleiste
sichtbar lassen. Bei den vier bestehenden Profilen jeweils DE/FR/SP ergänzen,
insbesondere lange Zeilen, Hermesia-Kurzdialog, spanisches Hog/Agris und
gestapelte Magaia-Fragmente. Erst diese Aufnahmen prüfen die praktische OCR.

## Quellen

Alle folgenden Seiten wurden am **2026-09-27** abgerufen. Die Datumsangaben stammen
von den Seiten; sie garantieren nicht, dass jeder Absatz aktualisiert wurde.

| Kürzel | Direktlink | Veröffentlichung / letzte Änderung |
| --- | --- | --- |
| E1 | [BDFoundry: Outer Edania](https://www.blackdesertfoundry.com/edania-monster-zones-guide/) | 2026-06-10 / 2026-08-12 |
| E2 | [BDFoundry: Inner Edania](https://www.blackdesertfoundry.com/edania-inner-monster-zones-guide/) | 2026-08-13 / 2026-09-07 |
| G1 | [Pearl Abyss Asia: Gavinya-Einführung](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=19633) | 2026-07-30 |
| S1 | [Garmoth: Star’s End Revamp](https://garmoth.com/guides/post/stars-end-remake) | 2026-03-23 / 2026-04-01 |
| S2 | [Garmoth: Sycraia Revamp](https://garmoth.com/guides/post/sycraia-underwater-ruins-revamp) | 2026-03-25 / 2026-04-01 |
| O1 | [BDFoundry: Orzekea, Abschnitt Monster Zone](https://www.blackdesertfoundry.com/atoraxxion-dungeon-guide-orzekea/) | 2025-03-28 / 2026-07-02 |
| O2 | [Garmoth: Orzekea](https://garmoth.com/guides/post/atoraxxion-orzekea) | 2025-04-17 / 2025-12-24 |
| U1 | [BDFoundry: Ulukita](https://www.blackdesertfoundry.com/ulukita-patch-guide/) | 2023-10-30 / 2025-05-30 |
| P1 | [BDFoundry: Golden Pig Cave](https://www.blackdesertfoundry.com/golden-pig-cave-guide/) | 2025-03-28 |
| W1 | [BDFoundry: Eternal Winter](https://www.blackdesertfoundry.com/mountain-of-eternal-winter-patch-guide/) | 2022-05-03 / 2023-07-16 |
| Y1 | [Reddit: Yzrahid, Erfahrungsbericht](https://www.reddit.com/r/blackdesertonline/comments/1mo7fgq/how_to_grind_efficiently_at_yzrahid_highlands/) | 2025-08-12; Einzelbericht |
| C1 | [BDFoundry: Calpheon Elvia](https://www.blackdesertfoundry.com/calpheon-elvias-realm-guide/) | 2022-06-03 / 2026-07-02 |
| D1 | [Garmoth: Morning Light Grind Zones](https://garmoth.com/guides/post/land-of-morning-light-grind-zones) | 2024-11-25 |
| L1 | [BDFoundry: Dehkia](https://www.blackdesertfoundry.com/dehkias-lantern-guide/) | 2023-07-19 / 2026-07-07 |
| L2 | [Garmoth: Dehkia, Tier 2](https://garmoth.com/guides/post/dehkias-lantern) | 2023-07-26 / 2026-07-02 |
| L3 | [Pearl Abyss Global Lab: Golemänderung](https://blackdesert.pearlabyss.com/GlobalLab/en-US/News/Notice/Detail?_boardNo=7385) | 2024-08-23; Testserver |

### Nicht ausgewertete Video-Hinweise

Die Abrufversuche für die ersten beiden Videos lieferten einen Fehler. Es liegen
weder ausgewertete Bilder noch Transkripte vor. Die übrigen Treffer wurden nur
als mögliche Folgebelege erfasst. Die Zeiten stammen aus den Kapitelbeschreibungen,
sind **keine verifizierten Bannerzeitpunkte** und begründen keine Klasse A.

| Thema | Fundstelle / gemeldetes Datum | Kapitelhinweis |
| --- | --- | --- |
| Mirumok/Gyfin | [MingKu, 2026-01-15](https://www.youtube.com/watch?v=XGAdEUCOOT4) | [Mirumok-Zyklus 2:15](https://www.youtube.com/watch?v=XGAdEUCOOT4&t=135s), [Gyfin-Zyklus 8:34](https://www.youtube.com/watch?v=XGAdEUCOOT4&t=514s) |
| Gavinya | [xRobse, 2026-07-30](https://www.youtube.com/watch?v=6Q_9cL8bPR4) | [Mechanik 3:14](https://www.youtube.com/watch?v=6Q_9cL8bPR4&t=194s) |
| Orzekea | [NotEfficient, 2026-08-12](https://www.youtube.com/watch?v=K9tI2oyiXL0) | [Phasen 1:19](https://www.youtube.com/watch?v=K9tI2oyiXL0&t=79s), [Orb 3:28](https://www.youtube.com/watch?v=K9tI2oyiXL0&t=208s) |
| Sycraia | [BlueSky900, 2026-08-13](https://www.youtube.com/watch?v=srM0R7jQT9Y) | [Mechanik 3:57](https://www.youtube.com/watch?v=srM0R7jQT9Y&t=237s) |

## Implementierungsstand

Zephyros ist mit Definition, Meldungsprofil, Registrierung, Darstellung und Tests
ergänzt. Es nutzt die gemeinsame Plattform ohne neue Timer oder Recovery-Regeln
und wird zusammen mit der Sprachintegration lokal auf Main übernommen. Die übrigen
35 Kandidaten sind nicht registriert. Der Build ist erfolgreich. Der Nutzer hat den
lokalen Commit trotz der vier dokumentierten Ausgangsfehler im Gesamttest
ausdrücklich beauftragt. Es erfolgt kein Push.

[E1]: https://www.blackdesertfoundry.com/edania-monster-zones-guide/
[E2]: https://www.blackdesertfoundry.com/edania-inner-monster-zones-guide/
[G1]: https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=19633
[S1]: https://garmoth.com/guides/post/stars-end-remake
[S2]: https://garmoth.com/guides/post/sycraia-underwater-ruins-revamp
[O1]: https://www.blackdesertfoundry.com/atoraxxion-dungeon-guide-orzekea/
[O2]: https://garmoth.com/guides/post/atoraxxion-orzekea
[U1]: https://www.blackdesertfoundry.com/ulukita-patch-guide/
[P1]: https://www.blackdesertfoundry.com/golden-pig-cave-guide/
[W1]: https://www.blackdesertfoundry.com/mountain-of-eternal-winter-patch-guide/
[Y1]: https://www.reddit.com/r/blackdesertonline/comments/1mo7fgq/how_to_grind_efficiently_at_yzrahid_highlands/
[C1]: https://www.blackdesertfoundry.com/calpheon-elvias-realm-guide/
[D1]: https://garmoth.com/guides/post/land-of-morning-light-grind-zones
[L1]: https://www.blackdesertfoundry.com/dehkias-lantern-guide/
[L2]: https://garmoth.com/guides/post/dehkias-lantern
[L3]: https://blackdesert.pearlabyss.com/GlobalLab/en-US/News/Notice/Detail?_boardNo=7385
[V1]: https://www.youtube.com/watch?v=UtAX9-qc-RY
[V2]: https://www.youtube.com/watch?v=fMNMvYq1jao
[V3]: https://www.youtube.com/watch?v=nWk93-d9g4k
