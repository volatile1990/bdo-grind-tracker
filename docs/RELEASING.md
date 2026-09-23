# Store-Release erstellen

Grindcrest wird ausschließlich über den [Microsoft Store](https://apps.microsoft.com/detail/9NQPWC1CMWS0)
verteilt und aktualisiert. GitHub dient der Quellcodeverwaltung und den automatisierten
Prüfungen; ein Build veröffentlicht noch keine neue Store-Version.

1. Änderungen und Versionshinweise vorbereiten. Für jede Einreichung eine höhere
   numerische Paketversion verwenden; die vierte MSIX-Stelle bleibt `0`.
2. Das Store-Paket unter Windows erstellen:

   ```powershell
   ./scripts/Build-StoreRelease.ps1 -Version 1.9.2 -RequireWindowsOcr
   ```

   Die Beispielversion durch die neue Release-Version ersetzen. Das Skript führt
   die .NET- und JavaScript-Tests aus und prüft Runtime, Paketidentität und Inhalt.
   `-RequireWindowsOcr` verlangt verfügbare OCR-Sprachpakete für `en-US` und `de-DE`.
   Voraussetzungen und Ausgabedateien stehen in der [Paketierungsanleitung](MICROSOFT_STORE.md#paket-erstellen).
3. Alternativ **GitHub Actions → Build Grindcrest for Microsoft Store → Run workflow**
   verwenden. Das Artefakt `grindcrest-store-package` enthält das MSIX; die
   verpflichtende native OCR-Prüfung benötigt einen entsprechend ausgestatteten Windows-PC.
4. Paket, Beschreibung und Versionshinweise im Partner Center für **Grindcrest**
   einreichen. Microsoft übernimmt Zertifizierung, Signierung und Verteilung.
   [Einreichung und Prüferhinweise](MICROSOFT_STORE.md#einreichen).
5. Ein echtes Store-Upgrade zwischen zwei freigegebenen Versionen prüfen,
   einschließlich Session-/Verlaufserhalt und neuer Paketversion nach dem Neustart.
   [Prüfablauf](MICROSOFT_STORE.md#prüfung-eines-echten-store-upgrades).

Die Produktidentität bleibt `Grindcrest.Grindcrest`, Store-ID `9NQPWC1CMWS0`.
Publisherangaben und unterstützte Windows-Versionen werden in
[MICROSOFT_STORE.md](MICROSOFT_STORE.md) beziehungsweise der [README](../README.md)
gepflegt. Store-Texte liegen unter `docs/release-notes/<Version>-store.txt`,
ausführliche Versionshinweise unter `docs/release-notes/<Version>.md`.
