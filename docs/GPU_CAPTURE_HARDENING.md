# Schutzmaßnahmen für die Fensteraufnahme

Die Aufnahme verwendet weiterhin ausschließlich Windows Graphics Capture für
das BDO-Fenster. Die folgenden Maßnahmen reduzieren unnötige Aufnahmearbeit und
begrenzen das Warten des Trackers auf GPU-Bilddaten. Sie belegen keine Ursache
für einen gemeldeten Freeze und garantieren keinen Schutz vor Treiber- oder
Hardwarefehlern.

## Interne Aufnahmerate

Auf Windows-Versionen mit `GraphicsCaptureSession.MinUpdateInterval` setzt der
Tracker ein Mindestintervall von **100 ms** für neue Aufnahmebilder. Die
Texterkennung behält ihren Zieltakt von **200 ms**. Der Abstand zwischen den
beiden Takten lässt Spielraum für ein frisches Bild: Der Tracker akzeptiert
weiterhin nur Bilder, deren Zeitstempel mindestens dem Beginn seiner aktuellen
Aufnahmeanfrage entspricht.

Die Verfügbarkeit wird zur Laufzeit geprüft. Ältere Windows-Versionen und eine
abgewiesene Intervallvorgabe behalten das bisherige Aufnahmeverhalten. Das
Windows-10-Mindestziel wird nicht angehoben. Weitere gleichzeitig laufende
Aufnahmeprogramme und Windows selbst können die tatsächlich anfallende
Grafikarbeit beeinflussen; die Einstellung garantiert keine bestimmte
GPU-Auslastung.

## Abbrechbares Warten auf GPU-Bilddaten

Die Kopie in die wiederverwendete Staging-Textur bleibt erhalten. Der anschließende
Lesezugriff verwendet `Map(READ, D3D11_MAP_FLAG_DO_NOT_WAIT)`. Nur wenn Direct3D
`DXGI_ERROR_WAS_STILL_DRAWING` meldet, wartet der Tracker kurz und versucht das
Lesen erneut. Ausstehende Befehle werden vor dem ersten Warten einmal mit
`Flush` übergeben.

- Zwischen Versuchen liegen bis zu **5 ms**; Pausieren kann das Warten abbrechen.
- Nach **2 Sekunden** endet die Wiederholung mit einem Aufnahmefehler.
- Andere Direct3D-Fehler beenden den Versuch unmittelbar.
- Der ursprüngliche HRESULT und ein abweichender Fehler aus
  `GetDeviceRemovedReason` bleiben in der Fehlerkette für den bestehenden
  lokalen Nachweis `last-capture-error.json` erhalten.
- Ein erfolgreich gemappter Speicherbereich wird auch dann freigegeben, wenn
  direkt danach ein Abbruch eintrifft. Die vorhandene serielle Aufnahme und
  Ressourcenfreigabe bleiben auf demselben Worker.

Ein fehlgeschlagenes Bild gelangt nicht zur Texterkennung. Der bestehende
Sessionablauf verarbeitet bereits aufgenommene Bilder zu Ende und stoppt die
Aufnahme; ein neuer Start erfolgt durch den Nutzer.

**Grenze:** Die Zeitgrenze gilt für die Wiederholung im Tracker. Ein einzelner
Treiber-/Kernelaufruf, der selbst nicht zurückkehrt, lässt sich damit nicht
unterbrechen. Auch ein systemweiter GPU-Hänger wird dadurch nicht sicher
verhindert.

## Prüfung

Automatisierte Tests simulieren verzögerte GPU-Bereitschaft, Abbruch, Timeout und
Gerätefehler. Ein COM-Testdouble prüft die Intervallübergabe und Freigabe der
zusätzlichen Windows-Schnittstelle einschließlich Fallback. Diese Tests erfassen
keine echten Spielfenster und ersetzen keinen SDR-/HDR-Praxistest auf dem
betroffenen Rechner.

## Microsoft-Referenzen

- [Fensteraufnahme](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [MinUpdateInterval](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.minupdateinterval)
- [Map](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-map)
- [DO_NOT_WAIT](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_map_flag)
- [Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-flush)
- [GetDeviceRemovedReason](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-getdeviceremovedreason)
