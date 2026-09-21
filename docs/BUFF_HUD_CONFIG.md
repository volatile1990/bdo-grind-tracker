# Buff-HUD: belegter Konfigurations- und Positionsvertrag

Stand: 2026-09-21, statisch gelesener PC-Client, Archivversion 3454. Die Untersuchung liest ausschließlich installierte PAZ-Inhalte. Sie verwendet weder Spielprozess noch Speicherzugriff, Lua-Ausführung oder Spieleingaben. Vollständige Clientskripte bleiben in ignorierten Rechercheartefakten und sind kein Bestandteil des Produkts.

## Panel und gespeicherte Position

`luacscript/x64/include/global_define_cpp_enum.luac` weist in der erhaltenen Lua-Zeilennummer 988 den Wert **119** an `CppEnums.PAGameUIType.PAGameUIPanel_AppliedBuffList` zu. Derselbe Enum weist in Zeile 1029 dem bereits verwendeten `PAGameUIPanel_ItemLogRenew` den Wert **159** zu. Somit ist UIData-Index 119 die gespeicherte Buffleiste. Der interne Index 58 im UI-Bearbeitungsfenster ist hingegen nur dessen eigene Panel-Liste und kein UIData-Index.

`luacscript/x64/widget/buff/appliedbuff_main.luac` lädt `UI_Data/Widget/Buff/UI_BuffPanel.xml` als `Panel_AppliedBuffList`. Seine Funktion `PaGlobalFunc_AppliedBuffList_ResetPosition` liest für Panel 119 mit `ToClient_GetUiInfo` die gespeicherten relativen X-/Y-Positionen (Lua-Zeilen 46–47). Die Feld-Enums lauten `PanelSaveType_RelativePositionX = 8`, `RelativePositionY = 9`, `IsShow = 2`.

Für eine gespeicherte reguläre PC-Position enthält diese Funktion ausdrücklich folgende Formeln (Lua-Zeilen 67–73):

```text
panelX = getOriginScreenSizeX() * relativeX - panel.GetSizeX() / 2
panelY = floor(getOriginScreenSizeY() * relativeY - panel.GetSizeY() / 2 + 0.9)
```

Die relativen Koordinaten bezeichnen daher die **Panelmitte**. Für einen Bildschirm-Crop werden die Layoutmaße mit dem effektiven UI-Maßstab in Pixel umgerechnet; die Ankerkorrektur verwendet weiterhin die nominale Panelgröße **410 × 162**, auch wenn die gezeichnete Buffleiste breiter ist. Der Console-Zweig besitzt eine andere Y-Positionierung und gehört nicht zum PC-Vertrag.

Die Lua-Positionswerte sind UI-Layoutkoordinaten. Als zusätzlicher Einheitenbeleg teilt `luacscript/x64/globalpreloadui.luac` in Zeilen 72–81 die aktuelle Control-Position durch `ToClient_GetUIScale()`, bevor es die um halbe Controlbreite beziehungsweise Controlhöhe korrigierten Werte an `SetPosX/Y` übergibt. Bei einem Vollbild-Crop und Maßstab `s` lautet die physische Y-Formel daher `s * floor(frameHeight / s * relativeY - 162 / 2 + 0.9)`: erst im Layout runden, danach skalieren. Auch absolute gespeicherte Positionen und der Randwert 1 werden mit `s` in Pixel umgerechnet.

### Standardposition und ältere absolute Positionen

Die Sonderfälle gelten jeweils nur, wenn **beide** Koordinaten den betreffenden Wert haben (`appliedbuff_main.luac`, Lua-Zeilen 49–65):

- **(0, 0):** Die Paneloberkante wird direkt auf `X = getOriginScreenSizeX() * 0.35`, `Y = getOriginScreenSizeY() * 0.75` gesetzt. Hier wird keine halbe Panelgröße abgezogen; es handelt sich um eine ausdrücklich definierte Standardposition.
- **(-1, -1), IsSaved ≤ 0:** Dieselbe Standardoberkante gilt, danach verändert `changePositionBySever` mangels gespeicherter Daten nichts.
- **(-1, -1), IsSaved > 0:** `changePositionBySever(panel, 119, true, true, false)` verwendet die gespeicherten **absoluten** `PositionX`/`PositionY` (PanelSaveType 3/4). Das letzte Argument deaktiviert Größenänderungen. Fehlende absolute Werte in diesem Fall belegen keine Standardposition.

`luacscript/x64/common/common_uimode.luac` belegt die Serverposition in `changePositionBySever`, Lua-Zeilen 38–69, insbesondere 49–50. Anschließend begrenzt `checkAndSetPosInScreen` (Zeilen 16–35) jede Achse: `pos < 0` setzt sie auf 0; andernfalls setzt `pos > screenSize - panelSize` sie auf `screenSize - panelSize`. `FGlobal_InitPanelRelativePos` (Zeilen 107–115) aktualisiert danach ausschließlich die relativen Werte; es verschiebt das Panel nicht.

### Abschließende Randkorrektur

Nach jeder Positionsvariante ruft `appliedbuff_main.luac` in Zeile 93 `FGlobal_PanelRepostionbyScreenOut` auf. Diese Funktion ist in `common_uimode.luac`, Lua-Zeilen 117–148, vollständig belegt. Für den PC führt sie pro Achse genau diese Reihenfolge aus:

```text
if pos + panelSize > screenSize:
    pos = screenSize - panelSize
else if pos < 0:
    pos = 1
```

Die Funktion verwendet `panel:GetSizeX/Y()`, also die nominalen **410 × 162** Layoutpixel im wirksamen Maßstab, **nicht** die bis zu 664 Pixel breite Hintergrundfläche. Der linke/obere Ersatzwert 1 ist ebenfalls ein Layoutpixel, also `s` physische Pixel. `screenSize` kommt hierbei aus `getScreenSizeX/Y()`, während die ursprüngliche Anker- und Standardberechnung `getOriginScreenSizeX/Y()` verwendet. Die Prüfung der rechten/unteren Grenze erfolgt zuerst; dies ist bei einem Panel größer als die verfügbare Fläche kein symmetrisches Clamp. Nach dieser Panelverschiebung wird die erweiterte Suchfläche mit dem tatsächlich aufgenommenen Bild geschnitten.

## Layout und tatsächliche Suchfläche

`ui_data/widget/buff/ui_buffpanel.xml` legt fest:

| Element | Layoutgröße | Layoutposition relativ zum Panel |
|---|---:|---:|
| Panel_AppliedBuffList | 410 × 162 | gespeicherte Position |
| Static_TotalBuff_BG | 410 × 52 | 0, 0 |
| Static_Buff_BG | 410 × 52 | 0, 54 |
| Static_DeBuff_BG | 410 × 52 | 0, 108 |
| Symbolvorlagen | 32 × 32 | durch Lua gesetzt |

`appliedbuff_main.luac` setzt `_maxBuffCount = 20` (Zeile 27) und initialisiert je Zeile 20 Symbole. Die vertikalen Symbolpositionen sind Hintergrund-Y + 2, also **2, 56 und 110**. Der horizontale Abstand beträgt Symbolbreite + 1, also **33** (Zeilen 106, 116–117). Text wird unterhalb des Symbols mit `BaseFont_8_Bold` gezeichnet; die XML-Textattribute lauten `AlignVertical="Bottom"` und `Span="4 -16"`.

`luacscript/x64/widget/buff/appliedbuff_control.luac` aktualisiert den Hintergrund auf `sichtbareAnzahl * 33 + 4` × 52 (Zeile 63): maximal **664 × 52**, also breiter als das nominale Panel. Nach dem Verbergen unbenutzter Slots steht der Schleifenzähler bei 20 (Zeilen 70–72); die folgende horizontale Korrektur setzt Hintergrund-X auf `50 - (20 - 17) / 2 * 33 = 0.5` (Zeile 76). Die Symbolposition ist anschließend `HintergrundX + 33 * (index - 1) + 2` (Zeilen 81–82), also beginnend bei **X = 2.5**.

Eine vollständige Suchfläche umfasst daher **664 × 162 Layoutpixel ab der Paneloberkante**, zuzüglich kleinem Rasterungsrand. Nur 410 Pixel Breite oder nur eine 52-Pixel-Zeile würden zulässige Symbole abschneiden. Die untersuchten PC-Buffskripte enthalten keine separate Icon-Skalierung, andere Ausrichtung oder variable Zeilenanzahl; sie verwenden diese drei festen Zeilen und den allgemeinen UI-Maßstab. UI-Bearbeitung kann die Sichtbarkeit und Position ändern.

## Anwendung im Tracker

Die automatische Erkennung bindet sich an dieselbe `CompanionCalibration` und
`gameVariable.xml` wie die Loot-Erkennung. Sie liest den aktiven Eintrag 119 pro
Scan neu, begrenzt die Symbolsuche auf den berechneten Bereich und verwendet
`32 × UiScale` als erwartete Symbolbreite. Unsichtbare oder mehrdeutige Einträge,
unpassende Auflösungen und erst nach der Aufnahme geänderte Dateien liefern
keinen Befund. UI-Presets ersetzen keinen fehlenden aktiven Eintrag.

Die vorliegende reale Konfiguration enthält `RelativePosX="0.7167248726"` und
`RelativePosY="0.9344827533"` bei 3840 × 2160 und `UiScale=1.49`. Daraus entsteht
einschließlich vier Pixeln Rand der Ausschnitt `(2442, 1894, 999, 250)`. Darin
werden die aufgenommenen Harmony- und Cron-Symbole samt Restzeiten erkannt.
Regressionstests prüfen auch eine gespeicherte Verschiebung und gleiche Symbole
außerhalb dieses Bereichs.

Der Tracker unterstützt reguläre relative Koordinaten und den belegten
Standardwert `(0,0)`. Die serverabhängigen Altzustände `(-1,-1)` werden weiterhin
als unbekannt behandelt; aus lokalen absoluten Feldern wird keine möglicherweise
abweichende Serverposition geraten.

## Nachprüfbare Quellen

Die Zeilenangaben stammen aus den erhaltenen Debug-Zeilentabellen der Lua-5.1-Dateien. Read-only-Export und Disassemblierung liegen lokal unter `.artifacts/buff-hud-research/`. SHA-256 der gelesenen Originalinhalte:

| Clientdatei | SHA-256 |
|---|---|
| `ui_data/widget/buff/ui_buffpanel.xml` | `30f0744ccf58b5033b86782df743f437f60a73414ce59069266fb3f0a6137525` |
| `luacscript/x64/widget/buff/appliedbuff_main.luac` | `591f9099f0ff162b714b618845602a93ee5577ce711a306fc760d96c34b57f0a` |
| `luacscript/x64/widget/buff/appliedbuff_control.luac` | `ee3f00cf3f8e5f630952570838ad5a803ef27403b9b4d2139346c51fd43007d6` |
| `luacscript/x64/include/global_define_cpp_enum.luac` | `165708e15d9466f395689dadc45927f2ff1ee2221361e034347582b2f0dce504` |
| `luacscript/x64/common/common_uimode.luac` | `8a65a7cd87de97b822fd726338dfca014179a3dcb846c00d3989a7faaf876341` |
| `luacscript/x64/globalpreloadui.luac` | `cf657a5edec585ac725918ae8619ed7cd97ce547ff12ceaea7a4ac81d487994b` |
