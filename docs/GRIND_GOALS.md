# Grind Goals

Unter Overlay in der Hauptnavigation liegt die Monatsplanung. Wochen beginnen montags. Ziele lassen sich für einzelne Tage, Montag–Freitag, Samstag/Sonntag oder alle Tage setzen und entfernen. Gruppenaktionen gelten für die gewählte Woche innerhalb des angezeigten Monats oder den gesamten Monat. Ein neuer Wert ersetzt bestehende Ziele der gewählten Tage.

Die Eingabe erfolgt in Millionen Silber. Ziele liegen lokal in `grind-goals.json`; Vorschau und Smoke-Tests arbeiten ohne Nutzerdateien. Der Fortschritt summiert `SilverAfterTax` aus dem gespeicherten Session-Verlauf am lokalen Kalendertag von `StartedAt`. Sessions über Mitternacht zählen zum Starttag. Mehrere Versionen derselben Session-ID zählen nur einmal mit dem neuesten Stand. Die normale Session-Sicherung aktualisiert damit auch die Zielerreichung; unvollständige Bewertungen werden kenntlich gemacht. Ziele werden beim Überschreiten erreicht, nicht zwischen Tagen verrechnet.

Tests prüfen Dateispeicherung, Entfernen einzelner Ziele, beschädigte Dateien, lokale Tageszuordnung und Vermeidung doppelter Session-Werte. Desktop-Vorschau: Einzelziel, fünf Arbeitstage einer Woche und acht Wochenendtage im September 2026 geprüft.
