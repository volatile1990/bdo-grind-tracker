"""Rebuild the reviewed September 2026 spot snapshot from public reference data.

Run from the repository root. This imports no credentials, sessions or live prices.
Existing Edania definitions and user-confirmed drop quantities remain authoritative.
"""
import json
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'docs/reference-evidence'
SOURCE = json.loads((EVIDENCE / 'screenshot-spots-source.json').read_text(encoding='utf-8'))
ENRICHED = json.loads((EVIDENCE / 'screenshot-spots-items-enriched.json').read_text(encoding='utf-8'))
IDS = {
    212: 'gavinya-coastal-cliff', 200: 'stars-end', 201: 'sycraia-abyssal-ruins-lower',
    169: 'elvia-orzekea', 198: 'dehkia-gyfin-rhasia-temple-upper', 148: 'tungrad-ruins',
    153: 'darkseekers-retreat', 167: 'fortunate-golden-pig-cave', 199: 'dehkia-mirumok-ruins',
    149: 'winter-tree-fossil-280', 168: 'unlucky-golden-pig-cave', 162: 'dehkia-ii-ash-forest',
    161: 'dehkia-ii-oluns-valley', 157: 'yzrahid-highlands', 124: 'elvia-hexe-sanctuary',
    121: 'elvia-quint-hill', 166: 'dokkebi-forest', 146: 'dehkia-thornwood-forest',
    147: 'city-of-the-dead', 163: 'dehkia-cadry-ruins', 143: 'dehkia-ash-forest',
    160: 'dehkia-crescent-shrine', 151: 'dehkia-cyclops-land', 110: 'jade-starlight-forest',
    145: 'dehkia-tunkuta', 156: 'dehkia-hystria-ruins',
    208: 'dark-energy-floodlands-great-red-sea', 209: 'dark-energy-floodlands-orbita',
    210: 'dark-energy-floodlands-zephyros',
}
NAMES = {201: 'Sycraia Abyssal Ruins (Lower)', 149: 'Winter Tree Fossil (280ap)',
         162: '[Dehkia II] Ash Forest', 161: "[Dehkia II] Olun's Valley",
         208: '(Great Red Sea) Dark Energy Floodlands', 209: '(Orbita) Dark Energy Floodlands',
         210: '(Zephyros) Dark Energy Floodlands'}
GLOBAL = ['Ancient Spirit Dust', 'Black Stone', 'Caphras Stone', 'Empty Picture Frame',
          "Laila's Petal", 'Pure Black Stone']
FLOOD = ["Flawless Herald's Crystal", 'HAN Crystal of Dusky Ruin', 'Silent Crystal of Origin',
         'Tainted Armor Fragment', 'Faded Dark Energy', 'Deboreka Necklace', 'Deboreka Earring',
         'Deboreka Belt', 'Deboreka Ring', *GLOBAL]
ASH_GENERIC = 'dehkia-ash-forest-unspecified'

def quoted(value):
    return json.dumps(value, ensure_ascii=False)

def write(path, content):
    (ROOT / path).write_text(content, encoding='utf-8', newline='\n')

items = [item for item in ENRICHED['items'] if not item.get('verificationError')]
by_key = defaultdict(list)
by_name = {}
for item in items:
    by_key[item['key']].append(item)
    by_name.setdefault(item['name'], item)

spots = []
for raw in SOURCE['spots']:
    spot = {'id': IDS[raw['id']], 'garmothId': raw['id'], 'name': NAMES.get(raw['id'], raw['name'])}
    spot['trash'] = by_key[raw['keys'][0]][0]['name']
    spot['loot'] = sorted(set(FLOOD if raw['id'] in (208, 209, 210) else
                             [i['name'] for key in raw['keys'] for i in by_key[key]] + GLOBAL))
    mapping = defaultdict(set)
    for key in raw['keys']:
        for item in by_key[key]:
            if item['name'] in spot['loot']:
                mapping[item['name']].add(key)
    # The visible name cannot distinguish several treasure-part variants.
    spot['drops'] = {name: next(iter(keys)) for name, keys in mapping.items() if len(keys) == 1}
    spot['ambiguousDrops'] = {name: sorted(keys) for name, keys in mapping.items() if len(keys) > 1}
    spots.append(spot)
ash = [s for s in spots if s['id'] in ('dehkia-ash-forest', 'dehkia-ii-ash-forest')]
spots.append({'id': ASH_GENERIC, 'name': '[Dehkia] Ash Forest (Stufe wählen)',
              'trash': "Tainted Specter's Cloth", 'loot': sorted(set(i for s in ash for i in s['loot']))})
winter = next(s for s in spots if s['id'] == 'winter-tree-fossil-280')
spots.append({'id': 'winter-tree-fossil-unspecified', 'name': 'Winter Tree Fossil (AP bestätigen)',
              'trash': winter['trash'], 'loot': winter['loot']})

header = '// Reviewed public snapshot; regenerate with scripts/Import-ScreenshotSpots.py.\n'
core = header + 'namespace BdoGrindTracker.Core;\n\npublic static partial class LootSpotCatalog\n{\n'
core += '    private static LootSpot[] CreateScreenshotSpots() =>\n    [\n'
for spot in spots:
    core += f'        new({quoted(spot["id"])}, {quoted(spot["name"])},\n        [\n'
    core += ''.join(f'            {quoted(name)},\n' for name in spot['loot'])
    core += f'        ], {quoted(spot["trash"])}),\n'
core += '    ];\n\n'
core += '''    public static IReadOnlyList<LootSpot> VariantsFor(string spotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spotId);
        string[] ids = spotId switch
        {
            "dark-energy-floodlands" or "dark-energy-floodlands-zephyros" or
                "dark-energy-floodlands-orbita" or "dark-energy-floodlands-great-red-sea" =>
                ["dark-energy-floodlands-zephyros", "dark-energy-floodlands-orbita", "dark-energy-floodlands-great-red-sea"],
            "dehkia-ash-forest-unspecified" or "dehkia-ash-forest" or "dehkia-ii-ash-forest" =>
                ["dehkia-ash-forest", "dehkia-ii-ash-forest"],
            "winter-tree-fossil-unspecified" or "winter-tree-fossil-280" => ["winter-tree-fossil-280"],
            _ => [spotId],
        };
        return Array.AsReadOnly(ids.Select(id => Spots.FirstOrDefault(spot => spot.Id == id))
            .OfType<LootSpot>().ToArray());
    }

    internal static string? DetectScreenshotSpot(string trashName)
    {
        if (trashName == "Tainted Specter's Cloth") return "dehkia-ash-forest-unspecified";
        if (trashName == "Winter Tree Snow Crystal") return "winter-tree-fossil-unspecified";
        var candidates = Spots.Where(spot => spot.PrimaryTrashItemName == trashName).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0].Id : null;
    }
}
'''
write('src/BdoGrindTracker.Core/LootSpotCatalog.ScreenshotSpots.cs', core)

existing_vocabulary = ROOT / 'data/items.en.txt'
old_lines = existing_vocabulary.read_text(encoding='utf-8').splitlines()
old_names = {line for line in old_lines if line and not line.startswith('#')}
new_names = sorted({name for spot in spots for name in spot['loot']} - old_names)
if new_names:
    write('data/items.en.txt', '\n'.join(old_lines) + '\n\n# Additional screenshot spots, September 2026.\n' + '\n'.join(new_names) + '\n')

german_path = ROOT / 'data/items.de.json'
german = json.loads(german_path.read_text(encoding='utf-8'))
known_german = {i['canonicalName'] for i in german['items']}
for name in sorted({name for spot in spots for name in spot['loot']} - known_german):
    i = by_name[name]
    german['items'].append({'canonicalName': name, 'itemId': i['itemId'],
                            'germanName': i['germanName'], 'source': i['germanSource']})
write('data/items.de.json', json.dumps(german, indent=2, ensure_ascii=False) + '\n')

# Keep the preexisting appraisal policy. Only newly imported fixed items use
# their current verified vendor value when available; market prices stay live.
existing_prices = (ROOT / 'src/BdoGrindTracker.App/Pricing/LootPriceCatalog.cs').read_text(encoding='utf-8')
import re
priced = set(re.findall(r'(?:Fixed|Market|new LootPriceDefinition)\("([^"]+)"', existing_prices))
price_items = sorted({n for s in spots for n in s['loot']} - priced)
prices = header + 'namespace BdoGrindTracker.App.Pricing;\n\ninternal static partial class LootPriceCatalog\n{\n'
prices += '    private static LootPriceDefinition[] ScreenshotDefinitions() =>\n    [\n'
for name in price_items:
    i = by_name[name]
    if i['tax'] == 1:
        value = f'Market({quoted(name)}, {i["itemId"]})'
    elif i['tax'] == 0:
        # Artifact appraisal includes the existing metadata's processing value.
        price = i['price'] if i['key'] == '100001004_0' else (i.get('vendorPrice') if i.get('vendorPrice') is not None else i['price'])
        value = f'Fixed({quoted(name)}, {price})'
    else:
        value = f'new({quoted(name)}, LootPriceKind.Unknown)'
    prices += f'        {value},\n'
prices += '    ];\n}\n'
write('src/BdoGrindTracker.App/Pricing/LootPriceCatalog.ScreenshotSpots.cs', prices)

garmoth = header + 'namespace BdoGrindTracker.App.Integrations.Garmoth;\n\ninternal static partial class GarmothCatalog\n{\n'
garmoth += '    private static readonly Dictionary<string, int> ScreenshotSpots = new(StringComparer.Ordinal)\n    {\n'
garmoth += ''.join(f'        [{quoted(s["id"])}] = {s["garmothId"]},\n' for s in spots if 'garmothId' in s)
garmoth += '    };\n\n    private static readonly Dictionary<string, string> ScreenshotDropKeys = new(StringComparer.Ordinal)\n    {\n'
for name, item in sorted(by_name.items()):
    keys = {i['key'] for i in items if i['name'] == name}
    if len(keys) == 1 and str(item['itemId']) == item['key'].split('_')[0]:
        garmoth += f'        [{quoted(name)}] = {quoted(item["key"])},\n'
garmoth += '    };\n\n    private static readonly Dictionary<string, Dictionary<string, string>> ScreenshotSpotDropKeys = new(StringComparer.Ordinal)\n    {\n'
for spot in spots:
    if 'garmothId' not in spot: continue
    garmoth += f'        [{quoted(spot["id"])}] = new(StringComparer.Ordinal)\n        {{\n'
    garmoth += ''.join(f'            [{quoted(name)}] = {quoted(key)},\n' for name,key in sorted(spot['drops'].items()))
    garmoth += '        },\n'
garmoth += '    };\n}\n'
write('src/BdoGrindTracker.App/Integrations/Garmoth/GarmothCatalog.ScreenshotSpots.cs', garmoth)

quantities = json.loads((ROOT / 'data/drop-quantities.json').read_text(encoding='utf-8'))
known_pairs = {(e['spotId'], e['itemName']) for e in quantities['entries']}
for spot in spots:
    for name in [*spot['loot'], '[Event] Mysterious Ore']:
        if (spot['id'], name) in known_pairs: continue
        entry = {'spotId': spot['id'], 'itemName': name, 'minimumQuantity': None,
                 'maximumQuantity': None, 'source': 'Screenshot-Spots 13.09.2026: Dropmenge nicht verifiziert.'}
        if spot['id'].startswith('dark-energy-floodlands-'):
            old = next(e for e in quantities['entries'] if e['spotId'] == 'dark-energy-floodlands' and e['itemName'] == name)
            entry.update(old, spotId=spot['id'])
        if name == 'Empty Picture Frame':
            entry.update(minimumQuantity=1, maximumQuantity=10,
                         source='Nutzervorgabe vom 11.09.2026: globaler Lootpool, Minimum 1, Maximum 10')
        quantities['entries'].append(entry)
trash_names = {spot['trash'] for spot in spots} | {
    'Chilled Soul Piece', 'Contaminated Coral Piece', 'Lightlost Core', 'Ancient Soldier Fragment',
    'Hardened Lava Chunk', 'Tainted Armor Fragment', 'Faded Dark Energy', 'Branch of Abundance',
    'Black Crystal Fragment', "Elion Follower's Helmet", 'Scorched Belt Ornament',
    "Elion Follower's Mark", 'Broken Gloves of the Void',
}
for entry in quantities['entries']:
    name = entry['itemName']
    maximum = (1000 if name in trash_names else 100 if name in
               ('Black Stone', 'Caphras Stone', 'Ancient Spirit Dust') else
               10 if name == "Laila's Petal" else None)
    if maximum is not None:
        minimum = (entry['minimumQuantity'] or 1) if name in trash_names else 1
        entry.update(minimumQuantity=minimum, maximumQuantity=maximum,
                     source='Nutzervorgabe vom 13.09.2026: bestehende Trash-Minima erhalten; neue Spots Trash 1–1000; Trash-Maximum überall 1000; Black Stone/Caphras Stone/Ancient Spirit Dust 1–100; Laila\'s Petal 1–10')
write('data/drop-quantities.json', json.dumps(quantities, indent=2, ensure_ascii=False) + '\n')
write('docs/reference-evidence/screenshot-spots-reviewed.json', json.dumps({'spots': spots}, indent=2, ensure_ascii=False) + '\n')
print(f'Generated {len(spots)} additional catalog entries, {len(price_items)} new price definitions, {len(new_names)} new OCR names.')
