"""Convert a Ragnarok+ barter collector snapshot to the offline RoAura catalog."""
import argparse
import hashlib
import io
import json
import sqlite3
from collections import defaultdict
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from PIL import Image


def export(source, output):
    db = sqlite3.connect(source.resolve().as_uri() + '?mode=ro', uri=True)
    cards = defaultdict(dict)
    for path, kind, payload in db.execute('SELECT path, kind, payload FROM card ORDER BY path'):
        if kind in ('barter', 'item'):
            cards[kind][int(path.rsplit('/', 1)[1])] = json.loads(payload)
    by_name = defaultdict(set)
    for item_id, item in cards['item'].items():
        by_name[item['title'].strip().casefold()].add(item_id)
    for shop in cards['barter'].values():
        for trade in shop.get('trades') or []:
            for row in [trade, *(trade.get('costs') or [])]:
                if row.get('item_id') and row.get('name'):
                    by_name[row['name'].strip().casefold()].add(row['item_id'])

    with ZipFile(output, 'w', ZIP_DEFLATED) as archive:
        cached = {}

        def image(key, size):
            if not key:
                return None
            if (key, size) not in cached:
                row = db.execute('SELECT data FROM asset WHERE key = ?', (key,)).fetchone()
                cached[key, size] = None
                if row:
                    with Image.open(io.BytesIO(row[0])) as raw:
                        raw.thumbnail((size, size))
                        data = io.BytesIO()
                        if size >= 320:
                            raw.convert('RGB').save(data, format='JPEG', quality=83, optimize=True)
                            extension = '.jpg'
                        else:
                            raw.convert('RGBA').quantize(colors=128).save(data, format='PNG', optimize=True)
                            extension = '.png'
                    entry = hashlib.sha256(data.getvalue()).hexdigest() + extension
                    if entry not in archive.NameToInfo:
                        archive.writestr(entry, data.getvalue())
                    cached[key, size] = entry
            return cached[key, size]

        def item_id(row):
            matches = by_name[row.get('name', '').strip().casefold()]
            return row.get('item_id') or (next(iter(matches)) if len(matches) == 1 else None)

        offers = []
        for shop_id, shop in sorted(cards['barter'].items()):
            maps = []
            for location in shop.get('maps') or []:
                maps.append(dict(code=location.get('code'), pins=[
                    ' '.join(filter(None, (pin.get('label'), pin.get('coordinates'))))
                    for pin in location.get('pins') or []],
                    image=image(location.get('marked_image_key'), 420) or image(location.get('image'), 420)))
            for trade in shop.get('trades') or []:
                reward_id = item_id(trade)
                item = cards['item'].get(reward_id, {})
                text = item.get('text', '')
                description = text.partition('\nDESCRIPTION\n')[2].split('\nSCRIPT\n')[0].split('\nTRADING & CRAFTING\n')[0].split('\nMARKET\n')[0]
                offers.append(dict(shopId=shop_id, ordinal=trade['ordinal'], name=trade['name'],
                    itemId=reward_id, quantity=trade.get('quantity'), npc=shop['title'], maps=maps,
                    npcIcon=image(shop.get('npc_icon'), 96), icon=image(trade.get('icon'), 64),
                    description=description,
                    details='\n\n'.join(part.split('STATS\n', 1)[-1] for part in item.get('fields', []) if 'STATS\n' in part),
                    costs=[
                        dict(itemId=item_id(cost) if cost.get('kind') == 'item' else None,
                            name=cost.get('name'), quantity=cost.get('quantity'),
                            kind=cost.get('kind'), icon=image(cost.get('icon'), 40))
                        for cost in trade.get('costs') or []]))
        archive.writestr('offers.json', json.dumps(offers, ensure_ascii=False, separators=(',', ':')))
    db.close()
    print(f'{len(offers)} offers; {sum(not x["itemId"] for x in offers)} without a resolved reward ID')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    export(args.source, args.output)
