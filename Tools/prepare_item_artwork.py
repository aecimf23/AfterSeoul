"""Prepare reproducible catalog/prompt manifests. Does not generate or edit pixels."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
items = json.loads((ROOT / 'Assets/StreamingAssets/Data/items.json').read_text(encoding='utf-8-sig'))['items']
order = {'Equipment': 0, 'Medical': 1, 'Food': 2, 'Ammo': 3, 'Container': 4, 'Junk': 5}
items.sort(key=lambda i: (order.get(i['category'], 9), i['slot'], i['id']))
shapes = {
    'WPN01': 'black AK74 style rifle, curved magazine, folding stock',
    'WPN02': 'tan M4 style carbine, straight telescoping stock, carry rail',
    'WPN03': 'long wooden bolt action Mosin style rifle',
    'WPN04': 'compact black polymer semi automatic pistol',
    'WPN05': 'dark HK416 style tactical carbine',
    'WPN06': 'integrally suppressed AS VAL style rifle, skeleton stock',
    'WPN07': 'compact black Vector style 9mm submachine gun, tall angular receiver, long straight magazine',
    'WPN08': 'compact MP5 style submachine gun',
    'WPN09': 'long tube magazine semi automatic shotgun',
    'WPN10': 'tan scoped semiautomatic marksman rifle with bipod',
    'WPN11': 'short wood stock SKS style carbine',
    'WPN12': 'wooden skeleton stock SVD style scoped sniper rifle',
    'WPN13': 'compact P90 style bullpup with top mounted translucent magazine',
    'WPN14': 'short MP7 style personal defense weapon, folding stock',
    'WPN15': 'black FAL style battle rifle with long barrel',
    'WPN16': 'wood and steel M1 style battle rifle',
    'WPN17': 'AK style rifle with tan modular rail',
    'WPN18': 'green hunting bolt action scoped rifle',
    'WPN19': 'short pump action shotgun with brown foregrip',
    'WPN20': 'Korean K2 style black 5.56 assault rifle, long barrel, triangular handguard, folding stock',
    'WPN21': 'large silver heavy semi automatic pistol',
    'WPN22': 'futuristic dark precision rifle with angular receiver',
    'WPN23': 'yellow black handheld stun pistol',
    'WPN26': 'black AK pattern shotgun with large box magazine',
    'WPN27': 'light machine gun with drum magazine and bipod',
    'MED01': 'red rectangular zippered first aid pouch',
    'MED02': 'small olive nylon first aid pouch',
    'MED03': 'large orange medical duffel bag',
    'MED05': 'small orange hard plastic pocket first aid box',
    'MED06': 'long green surgical roll kit with metal instruments',
    'MED07': 'compact tan surgical instrument kit',
    'MED10': 'small round red gold balm tin',
    'MED13': 'white blue hemostatic gauze packet',
    'MED14': 'white orange painkiller pill bottle',
    'MED15': 'square desert tan first aid pouch',
    'MED16': 'white rolled cloth bandage',
    'MED17': 'black red tourniquet with windlass',
    'MED18': 'folded orange foam splint',
    'JUNK01': 'large graphics card with twin cooling fans',
    'JUNK02': 'white red compact medical vein scanner',
    'JUNK05': 'small blue retro handheld game console',
    'JUNK06': 'gold cryptocurrency coin',
    'JUNK13': 'small gold skull figurine',
    'JUNK14': 'gold wristwatch with bracelet',
    'JUNK39': 'silver necklace and pendant',
    'JUNK40': 'small bronze horse statuette',
}
entries = []
prompts = []
for start in range(0, len(items), 40):
    batch = items[start:start + 40]
    number = start // 40 + 1
    resource = f'ItemIcons/catalog-{number:02d}'
    rows = []
    for row in range(5):
        cells = []
        for col in range(8):
            index = row * 8 + col
            if index >= len(batch):
                cells.append('EMPTY transparent cell')
                continue
            item = batch[index]
            subject = shapes.get(item['id'], item['shortName'])
            if item['slot'] != 'None': subject += ' (' + item['slot'] + ')'
            else: subject += ' (' + item['category'] + ')'
            if 'Blueprint' in subject: subject += ', rolled blue paper with a tiny schematic of the named object'
            cells.append(subject)
            entries.append({'id': item['id'], 'resource': resource, 'column': col, 'row': row, 'subject': subject})
        rows.append(f'Row {row + 1}, left to right: ' + '; '.join(cells))
    prompt = '''Use case: stylized-concept. Asset type: production mobile survival game inventory sprite atlas.
Create ONE landscape 1536x1024 RGBA PNG with genuine transparent alpha background. EXACTLY 8 columns and 5 rows, 40 equal cells, NO labels, text, borders, grid lines, backdrop, floor, or shadow outside objects. Each object must be centered in its own cell with a generous 22 percent empty transparent margin on EVERY side; no object may cross its cell boundary. Top-to-bottom row-major order is mandatory.
Style: detailed hand-painted semi-realistic worn survival equipment, crisp dark outlines, subtle top-left highlights, restrained realistic colors, strong distinct silhouettes readable at 80px. Small three-quarter product view. Weapons diagonal to fit. Distinguish variants by shape, pouch construction, visible materials and color accents. Keep every object isolated. Do not substitute a generic icon for the named object. For hypothetical survival tools depict a plausible physical object. Blueprints/documents should have small pictorial markings but no readable words.
''' + '\n'.join(rows)
    prompts.append({'resource': resource, 'prompt': prompt})
dest = ROOT / 'Assets/Resources/ItemIcons/catalog.json'
dest.write_text(json.dumps({'columns': 8, 'rows': 5, 'entries': entries}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(ROOT / 'docs/item-artwork-prompts.json').write_text(json.dumps(prompts, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'{len(entries)} item-specific slots across {len(prompts)} sheets')
