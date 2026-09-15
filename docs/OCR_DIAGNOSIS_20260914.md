# Magaia-Test vom 14. September 2026

## Ergebnis

Der Nutzer meldet **3.786 Elion Follower's Helmet im Spiel gegenüber 3.776 im Tracker**
und bestätigt, dass der Spielzähler beim Beginn der Diagnoseaufnahme auf null stand.
Die Abweichung ist bestätigt, aber noch nicht vollständig erklärt oder behoben.

Die vollständige Prüfung der aufgezeichneten sichtbaren Lootzeilen ergibt:

| Abschnitt | Sichtbare Beute | Baseline |
|---|---:|---:|
| Viererphase, Frames 108–653 | 106 × 4 = 424 | 428 |
| Siebenerphase, Frames 684–1800 | 161 × 7 = 1.127 | 1.127 |
| Mittlerer Abschnitt, Frames 1801–2749 | 108 × 7 + 807 Bossloot = 1.563 | 1.563 |
| Später Abschnitt, Frames 2750–3188 | 94 × 7 = 658 | 658 |
| **Gesamt** | **3.772** | **3.776** |

Die verbleibenden **14 Helme** gegenüber dem gemeldeten Spielstand sind in diesen
Bildnachweisen nicht als zusätzliche Drops oder abweichende Einzelmengen belegt.
Aus dieser Differenz wird keine automatische Mengenkorrektur und keine Änderung
der allgemeinen Zählregeln abgeleitet.

## Nachgewiesener Vierer-Überzähler

Frames 238–242 enthalten `Intricately Patterned Mystical Shard x`, das im
aufgezeichneten Katalog noch fehlt. Frame 244 zeigt einen neuen Helm-Drop ×4 unten;
in Frame 245 steht dieselbe Zeile eine Position höher und eine neue ×4-Zeile unten.
Die Baseline zählt den Helm aus Frame 244 erneut.

Ein abgeleiteter Replay mit ausschließlich zusätzlichem Shard-Katalogeintrag
ändert nichts: Im alten Rohtext fehlt die Mengen-Ziffer. Ein zweiter, ausdrücklich
abgeleiteter Versuch bildet die inzwischen konfigurierte feste Einermenge nach:
nur die fünf Shard-Beobachtungen 238–242 werden als erkanntes Item mit Menge 1
und festen Grenzen 1/1 weitergegeben; Rohtexte und Helm-Mengen bleiben erhalten.
Damit ergeben sich **3.772 Helme und ein Shard**, alle anderen Endmengen bleiben gleich.
Der Mengenunterschied von −4 beginnt bei Frame 244 und bleibt bis zum Ende erhalten.
Dies ist ein Versuch mit abgeleiteten Eingaben, kein vollständiger erneuter OCR-Lauf
der neuen App. Er zeigt die Wirkung des ergänzten Katalogs und der Einermenge.

Die Zeilenzuordnung selbst wird dabei nicht vollständig repariert: Die Helm-Lesung
aus Frame 244 bleibt dem zuvor unbekannten Shard-Ereignis zugeordnet; die mehreren
korrekten Shard-Lesungen verhindern dessen Umdeutung in einen zusätzlichen Helm-Drop.

## Verifikation und Grenzen

- Aufnahme: `C:\Users\marku\AppData\Local\BdoGrindTracker\diagnostics\loot-20260914-131155-008f425199d746539fbb0d2bb673ac17`.
- 3.188 aufgezeichnete Frames und ein Abschluss; Aufnahme-Engine
  `grindcrest-lifetime-v6`, Normalzähler `lifetime-v5`, App `1.5.1+7ead9be50d23b2fa4ed76bafc74216c6bb0e0c06`.
- Originaljournal SHA-256:
  `99bb4134b7eaabde852d288da59f054a895381de8efe78334b5b166b8f607f6d`.
- Zwei Läufe mit dem ursprünglichen getesteten App-Paket reproduzieren die
  3.776 Helme aus 479 Helm-Ereignissen. Die finalen Ereignisse samt zugeordneten
  Originalbeobachtungen sind in beiden Läufen identisch.
- Gespeicherte Session und letzte Diagnoseprojektion enthalten beide 3.776;
  die Abweichung entsteht nicht erst beim Speichern.
- Alle neun Bossmengen sind in den Bildern bestätigt:
  91, 91, 87, 82, 100, 108, 66, 80 und 102; Summe 807.
- Die einzigen gelesenen Einser statt Siebener, Frames 1507 und 2362,
  sind ausblendende Nachlesungen und werden bereits korrekt als 7 gezählt.
- Die Übergänge 1274–1276 und 2866–2868 enthalten verschobene Zuordnungen;
  nachfolgende Buchungen gleichen diese innerhalb desselben Bursts aus.
- Keine Capture-Abstände über 400 ms; zusätzliches Durchsuchen der Zwischenräume
  nach Helmschrift liefert keinen weiteren belastbaren vollständigen Drop.
- Eine Folge von Aufnahmen im Abstand von ungefähr 200 ms beweist nicht, dass
  jede Spielmeldung sichtbar erfasst wurde. Ob Meldungen zwischen Aufnahmen,
  außerhalb des Ausschnitts oder bereits innerhalb der Spielanzeige fehlen,
  lässt sich aus dieser Aufnahme nicht entscheiden.

Die Originalaufnahme und die aktuelle gespeicherte Session wurden nicht verändert.
Für die offenen 14 Helme wäre zusätzliche Beobachtungsevidenz erforderlich,
beispielsweise eine durchgehende Aufnahme mit gleichzeitig sichtbarem Spielzähler
und vollständigem Lootlog. Der bestätigte Startwert null wird nicht infrage gestellt.

## Lokale Analyseartefakte

Die vollständigen Lesezeugen, Kontaktbögen und abgeleiteten Versuche liegen im
lokalen Arbeitsverzeichnis `artifacts/helmet-20260914/` (nicht Teil des Repositorys):

- `baseline-audit/current131155/baseline/final-events.json`: finale Ereignisse und Original-Lesezeugen.
- `quantity-evidence/findings.md`: alle 16 frühen Vierer-Bursts und Mengenprüfung.
- `quantity-evidence/sevens-684-1800.md`: alle 29 frühen Siebener-Bursts.
- `seven-late-manual-count.json`: alle 18 mittleren Bursts samt Ankunftssequenzen.
- `late-evidence/findings.md`: alle zwölf späten Siebener-Bursts.
- `shard-context/FINDINGS.md`: getrennte Kontext- und Einermengen-Versuche, Deltas und Hashnachweise.
