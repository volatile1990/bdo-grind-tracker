# Lokales Store-MSIX erstellen

Ein „Release“ bedeutet in diesem Projekt ausschließlich: das MSIX-Paket für den
Microsoft Store lokal unter Windows mit einer angegebenen Version bauen und prüfen.
Ein Release-Auftrag endet mit dem fertigen Paket und den lokalen Prüfergebnissen.
Er umfasst weder Commit, Push oder Tag noch GitHub-Builds, Uploads oder eine
Einreichung im Partner Center. Diese Schritte erfolgen nur auf gesonderten Auftrag.

```powershell
./scripts/Build-StoreRelease.ps1 -Version 1.15.0 -RequireWindowsOcr
```

Die Beispielversion durch die gewünschte Version ersetzen. Das Skript führt die
.NET- und JavaScript-Tests aus und prüft Runtime, Paketidentität und Inhalt.
`-RequireWindowsOcr` verlangt verfügbare OCR-Sprachpakete für `en-US` und `de-DE`.
Das MSIX und die Prüfergebnisse liegen lokal unter `artifacts/store/<Version>/`.
Voraussetzungen und Ausgabedateien beschreibt die
[Paketierungsanleitung](MICROSOFT_STORE.md#paket-erstellen).

Die Paketversion hat vier numerische Stellen; die vierte bleibt `0`.
Für eine spätere Store-Einreichung muss sie höher als die zuletzt eingereichte
Version sein. Die Produktidentität ist `Grindcrest.Grindcrest`, die Store-ID
`9NQPWC1CMWS0`.
