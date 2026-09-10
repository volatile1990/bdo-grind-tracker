# Grindcrest — Logo und Namensgebung

**Grindcrest** verbindet „Grind“ (die wiederkehrenden Sessions) mit „Crest“
(Wappen/Signet). Ein kantiges goldenes G umschließt einen Loot-Kristall.
Die Bildmarke harmoniert mit der bestehenden dunklen, goldbetonten Oberfläche.
Der Name ist ein kreativer Produktvorschlag; eine Marken- oder Domainprüfung war
nicht Teil dieser lokalen Implementierung. Kein offizielles Pearl-Abyss-Produkt.

## Dateien

- `grindcrest-logo.png`: generierter PNG-Master, 1254 × 1254 px, echte RGBA-Transparenz.
- `grindcrest-header.png`: technisch auf 128 × 128 px skaliert, für das App-Logo.
- `grindcrest.ico`: derselbe Entwurf als Windows-Icon mit 16/24/32/48/64/128/256 px.

Der Store-Build erzeugt aus demselben PNG zusätzlich die drei MSIX-Basislogos
und je 15 `Square44x44Logo.targetsize-*`-Varianten für 16/20/24/30/32/36/40/44/48/60/64/72/80/96/256 px.
Jede Größe liegt auch als `altform-unplated` und `altform-lightunplated` vor,
damit Windows das transparente Logo auf dunklen und hellen Taskleisten ohne
farbige Hintergrundfläche darstellen kann. Alle 48 PNG-Dateien behalten die
Transparenz des Masters; das Motiv wird ausschließlich skaliert.

Die finale Bildmarke wurde mit der integrierten Bildgenerierung erstellt und
anschließend mit demselben Werkzeug vereinfacht und freigestellt. Keine CLI/API-
Fallback-Generierung. Keine BDO-/Companion-/Garmoth-Logos oder Charaktere als Vorlage.

`tools/BrandAssets` übernimmt ausschließlich Skalierung und ICO-Verpackung,
keine gestalterische Bearbeitung. Der Original-Master bleibt unverändert:

```powershell
dotnet run --project tools/BrandAssets -c Release -- data/branding/grindcrest-logo.png data/branding/grindcrest.ico data/branding/grindcrest-header.png --force
```

## Verwendete Prompts

### 1. Entwurf

```text
Use case: logo-brand
Asset type: production app logo mark and Windows desktop application icon for "Grindcrest", an independent Black Desert grind-session and loot/silver tracker.
Primary request: Design ONE original, distinctive, compact emblem that combines a faceted loot crystal with a bold angular letter G forming a protective crest. The single mark should suggest valuable loot and precise tracking, not a generic shield, sports team mascot or cryptocurrency.
Style/medium: refined vector-like flat emblem, beautifully balanced negative space, crisp geometric silhouette with a subtle fantasy-game character. Strong thick shapes readable at 32px. At most two flat gold tones.
Color palette: warm gold #DBAB48 with a restrained pale-gold facet #F4CD78, designed for a nearly black #0D1116 application background. This palette is the existing application's UI palette.
Composition: square 1024 x 1024 PNG, emblem centered and occupying about 82% of width and height; ample uniform transparent safety margin; no perspective.
Background: genuinely transparent alpha background, not a checkerboard illustration, no colored backdrop.
Text: none. Only the emblem/abstract G, no wordmark, initials labels, captions or extra text.
Constraints: original identity, no Black Desert / Pearl Abyss / BDO Companion / Garmoth logos or characters. No watermark. No gradients, 3D, bevels, glow, shadows, mockups, fine flourishes or extra symbols. One final coherent icon, not a grid of options.
```

### 2. Vereinfachung

```text
Use case: logo-brand.
Edit target: the attached first Grindcrest logo.
Make one targeted refinement: reduce it to a clean SMALL-ICON identity while preserving the central crystal inside an angular G.
Remove the entire outer compass arc, all four outside ornaments, all stray particles and all glow. Remove every bevel, texture and gradient. Reduce to bold flat filled silhouettes, not outlines: a warm-gold angular G surrounding ONE simple crystal whose two broad flat facets use warm gold #DBAB48 and pale gold #F4CD78. Use only these two solid colors plus transparent negative space. The G and the crystal together must form one compact, balanced emblem, with broad clean negative-space gaps. No fragmented flourishes.
Composition: square PNG with genuine clean transparent alpha, precisely centered emblem occupying approximately 80% of width and height, an even 10% empty margin all around. All transparent areas must have no visible colored noise or halos. Strong silhouette readable at 24px. No writing, no tagline, no added symbols, no shadow, no background, no 3D. Maintain the original G-with-crystal concept, simplify only.
```

### 3. Finale Freistellung

```text
Use case: background-extraction.
Input image: edit target, the simplified gold Grindcrest G-and-crystal emblem. The white and grey checkerboard is unfortunately baked into the image and must NOT remain.
Primary request: remove the entire checkerboard/background and deliver an actual RGBA PNG with fully transparent alpha pixels everywhere outside the gold mark, including inside its negative spaces. This is background removal, not a mockup of transparency.
Preserve the gold angular G shape and crystal exactly; preserve positioning and square framing. Change only the background. No drawn checkerboard, no replacement solid background, no extra artwork or writing. Crisp clean cutout edges with antialiasing, zero white or grey halos. The final image must have a real transparent alpha channel.
```

Der zweite Zwischenschritt enthielt ein eingebranntes Schachbrett und wurde nicht
ausgeliefert. Der finale Master wurde auf tatsächlichen Alphakanal geprüft.
