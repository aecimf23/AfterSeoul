"""Build a searchable local HTML index of original generated PNGs (no pixel edits)."""
import html
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
manifest = json.loads((root / 'Assets/Resources/ItemIcons/catalog.json').read_text(encoding='utf-8-sig'))
items = {i['id']: i for i in json.loads((root / 'Assets/StreamingAssets/Data/items.json').read_text(encoding='utf-8-sig'))['items']}
locale = json.loads((root / 'Assets/Resources/Locales/ko.json').read_text(encoding='utf-8-sig'))
cards = []
for e in manifest['entries']:
    item = items[e['id']]
    name = locale.get('ITEM_' + e['id'] + '_NAME', item['shortName'])
    query = html.escape(' '.join([e['id'], name, item['shortName'], item['category'], item['slot']]).lower(), quote=True)
    left = e['column'] * 192
    top = e.get('top', round(e['row'] * 1024 / 5))
    bottom = e.get('bottom', round((e['row'] + 1) * 1024 / 5))
    cards.append(f'''<article data-query="{query}"><svg role="img" aria-label="{html.escape(name, quote=True)}" viewBox="{left} {top} 192 {bottom-top}"><image href="../Assets/Resources/{e['resource']}.png" width="1536" height="1024"/></svg><h2>{html.escape(name)}</h2><p>{html.escape(e['id'])}</p><small>{html.escape(item['category'])} · {html.escape(item['slot'])}</small></article>''')
page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>AFTER SEOUL 아이템 아이콘</title><style>
*{box-sizing:border-box}body{margin:0;background:#101722;color:#e0e6ef;font:16px system-ui,sans-serif}header{position:sticky;top:0;z-index:1;background:#101722f5;padding:24px 5%;border-bottom:1px solid #3d4958}h1{margin:0 0 10px;font-size:26px}header p{color:#acb8c8}input{width:min(600px,100%);padding:13px;color:#fff;background:#202c3b;border:1px solid #68778b;border-radius:8px;font-size:16px}main{padding:24px 5%;display:grid;grid-template-columns:repeat(auto-fill,minmax(155px,1fr));gap:14px}article{padding:12px;background:#1a2433;border:1px solid #354153;border-radius:10px;min-width:0}article[hidden]{display:none}svg{width:100%;height:150px;display:block;overflow:hidden}h2{font-size:15px;margin:12px 0 6px;min-height:38px}article p{font:11px monospace;overflow-wrap:anywhere;color:#85bfca}small{font-size:10px;color:#9ba8b8}#count{margin-left:12px;color:#a7d2ba}
</style><header><h1>AFTER SEOUL · 아이템 아이콘 462종</h1><p>장비, 식량, 의료품, 부품, 퀘스트·확장 콘텐츠용 개별 그림. 기능 미지원 물품도 사전 제작했습니다.</p><input id="search" aria-label="아이템 검색" placeholder="이름 · ID · 종류 검색"><span id="count">462개</span></header><main>''' + '\n'.join(cards) + '''</main><script>
const search=document.getElementById('search'),cards=[...document.querySelectorAll('article')];search.addEventListener('input',()=>{const q=search.value.toLowerCase().trim();let count=0;for(const card of cards){card.hidden=!card.dataset.query.includes(q);if(!card.hidden)count++;}document.getElementById('count').textContent=count+'개';});
</script></html>'''
(root / 'docs/item-artwork-gallery.html').write_text(page, encoding='utf-8')
print('Gallery: 462 named searchable cards')
