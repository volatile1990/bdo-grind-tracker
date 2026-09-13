"""One-off read-only public item verification for the requested screenshot spots."""
import concurrent.futures, html, json, pathlib, re, requests

root = pathlib.Path(__file__).parent
data = json.loads((root / 'screenshot-spots-source.json').read_text(encoding='utf-8'))
existing = {row['canonicalName']: row for row in json.loads((root.parent.parent / 'data/items.de.json').read_text(encoding='utf-8'))['items']}
excluded = []
selected = []
for row in data['items']:
    if row['name'] == 'Any Artifact' or row['name'].startswith("Sethra's Artifact"):
        excluded.append({'name': row['name'], 'reason': 'Aggregate artifact pseudo-item or life-skill artifact outside grind combat scope.'})
        continue
    item_id = int(row['key'].split('_')[0])
    if row['name'] == "Lafi Bedmountain's Upgraded Compass Parts":
        # Verified through the current item link in /us/quest/4602/1/.
        item_id = 44277
    if row['key'] == '100001004_0':
        # Candidate only. The actual item page must confirm the item identity.
        match = re.search(r'/(\d+)(?:_\d+)?\.webp$', row['icon'])
        item_id = int(match.group(1)) if match else item_id
    selected.append(dict(row, candidateItemId=item_id))

def fetch(row):
    row = dict(row)
    item_id = row['candidateItemId']
    source = f'https://bdocodex.com/de/item/{item_id}/'
    try:
        response = requests.get(source, timeout=25)
        response.raise_for_status()
        h = response.text
        title = re.search(r'<title>(.*?) - BDO Codex</title>', h)
        icon = re.search(r'<meta property="og:image" content="([^"]+)"', h)
        price = re.search(r'Verkaufspreis:\s*([\d,]+)', h)
        if not title or not title.group(1) or not icon or icon.group(1).endswith('site_logo.webp'):
            raise ValueError('Missing item title or icon')
        row.update(itemId=item_id, germanName=html.unescape(title.group(1)), germanSource=source,
                   iconUrl=html.unescape(icon.group(1)), verifiedOn='2026-09-13',
                   vendorPrice=int(price.group(1).replace(',', '')) if price else None)
        # The aggregate Garmoth key is not the real item ID; confirm the alias
        # against the English page rather than asserting identity from an icon.
        if row['key'] == '100001004_0':
            en = requests.get(f'https://bdocodex.com/us/item/{item_id}/', timeout=25)
            en.raise_for_status()
            en_title = re.search(r'<title>(.*?) - BDO Codex</title>', en.text)
            row['verifiedEnglishName'] = html.unescape(en_title.group(1)) if en_title else None
            if row['verifiedEnglishName'] != row['name']:
                row['identityWarning'] = 'English public item name differs from cache alias.'
    except Exception as error:
        row['verificationError'] = str(error)
    return row

with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    enriched = list(pool.map(fetch, selected))

result = {'source': data['source'], 'verifiedOn': '2026-09-13', 'items': enriched, 'excluded': excluded}
(root / 'screenshot-spots-items-enriched.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'rows': len(enriched), 'errors': [row['name'] for row in enriched if 'verificationError' in row],
                  'identityWarnings': [row['name'] for row in enriched if 'identityWarning' in row],
                  'fixedPriceChanges': [{'name': row['name'], 'cache': row['price'], 'verified': row.get('vendorPrice')} for row in enriched if row['tax'] == 0 and row.get('vendorPrice') is not None and row['price'] != row['vendorPrice']]}, ensure_ascii=True))
