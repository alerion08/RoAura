#!/usr/bin/env python3
"""Snapshot the public ToC API into a compact, offline SQLite catalog.

Uses curl (TLS/proxy handling), Python stdlib and bounded parallelism. Responses
are cached outside the repository for resumable imports; no game/user data.
"""
import argparse
import concurrent.futures
import datetime
import hashlib
import json
import pathlib
import sqlite3
import subprocess
import time
import urllib.parse
import zlib

BASE = 'https://tidesofcomodo.com/api/v1'
ROOT = pathlib.Path(__file__).resolve().parents[1]


def packed(value):
    return zlib.compress(json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode(), 9)


def unpacked(value):
    return json.loads(zlib.decompress(value))


class Source:
    def __init__(self, cache):
        self.cache = pathlib.Path(cache)
        self.cache.mkdir(parents=True, exist_ok=True)

    def get(self, path):
        file = self.cache / (hashlib.sha256(path.encode()).hexdigest() + '.json')
        if file.exists():
            return json.loads(file.read_text(encoding='utf-8'))
        for attempt in range(5):
            result = subprocess.run(['curl', '-fsSL', '--max-time', '45',
                                     '--user-agent', 'RoAura-ToC-catalog/1.0', BASE + path],
                                    capture_output=True)
            if result.returncode == 0:
                try:
                    data = json.loads(result.stdout)
                    temporary = file.with_suffix('.tmp')
                    temporary.write_bytes(result.stdout)
                    temporary.replace(file)
                    return data
                except ValueError:
                    pass
            time.sleep(min(30, 2 ** attempt))
        raise RuntimeError(f'Cannot retrieve {path}; previous snapshot is unchanged')

    def listing(self, kind, extra=''):
        rows = []
        first = self.get(f'/{kind}?page=1&page_size=100{extra}')
        total, pages = first['total'], first['total_pages']
        def page_data(page):
            return first if page == 1 else self.get(f'/{kind}?page={page}&page_size=100{extra}')
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            responses = list(pool.map(page_data, range(1, pages + 1)))
        for data in responses:
            if data['total'] != total:
                raise ValueError('Catalog changed during pagination; retry with fresh cache')
            rows.extend(data['results'])
        if len(rows) != total or len({x['id'] for x in rows}) != total:
            raise ValueError(f'Incomplete or duplicate {kind} listing')
        return rows


def build(output, source, workers):
    items = source.listing('items')
    monsters = source.listing('monsters')
    mvps = source.listing('monsters', '&mvp=1')
    if not items or not monsters or not mvps:
        raise ValueError('Empty catalog response; previous snapshot is unchanged')
    mvp_ids = {x['id'] for x in mvps}
    if mvp_ids != {x['id'] for x in monsters if x['is_mvp']}:
        raise ValueError('MVP filter and monster flags disagree')
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix('.building')
    if temporary.exists():
        temporary.unlink()
    db = sqlite3.connect(temporary)
    db.executescript('''
        PRAGMA page_size=16384;
        CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
        CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT NOT NULL, aegis TEXT NOT NULL,
          type TEXT, slots INTEGER, buy_price INTEGER, sell_price INTEGER, payload BLOB NOT NULL);
        CREATE INDEX item_name ON items(name COLLATE NOCASE);
        CREATE INDEX item_aegis ON items(aegis);
        CREATE TABLE monsters(id INTEGER PRIMARY KEY, name TEXT NOT NULL, aegis TEXT NOT NULL,
          is_mvp INTEGER NOT NULL, level INTEGER, hp INTEGER, base_exp INTEGER, job_exp INTEGER,
          payload BLOB NOT NULL);
        CREATE INDEX monster_name ON monsters(name COLLATE NOCASE);
        CREATE TABLE drops(monster_id INTEGER NOT NULL, item_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
          rate INTEGER, real_rate INTEGER, payload BLOB NOT NULL, PRIMARY KEY(monster_id, ordinal));
        CREATE INDEX drop_item ON drops(item_id);
        CREATE TABLE mvp_rewards(monster_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, item_id INTEGER,
          aegis TEXT NOT NULL, rate INTEGER, payload BLOB NOT NULL, PRIMARY KEY(monster_id,ordinal));
        CREATE INDEX mvp_reward_item ON mvp_rewards(item_id);
        CREATE VIEW mvp AS SELECT * FROM monsters WHERE is_mvp=1;
    ''')
    try:
        for kind, listing in [('items', items), ('monsters', monsters)]:
            def detail(row):
                # Item list returns the complete item record used by ItemDetail.
                # Its only extra section, dropped_by, is indexed once from monster details.
                data = {'item': row} if kind == 'items' else source.get(f'/{kind}/{row["id"]}')
                record = data['item' if kind == 'items' else 'monster']
                if record['id'] != row['id']:
                    raise ValueError('Detail ID mismatch')
                # Preserve list-specific derived values without overwriting raw detail stats.
                data['listing_overrides'] = {k: v for k, v in row.items() if k not in record or record[k] != v}
                return record, data
            with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
                for count, (row, data) in enumerate(pool.map(detail, listing), 1):
                    name = row.get('display_name') or row['name_english']
                    if kind == 'items':
                        db.execute('INSERT INTO items VALUES(?,?,?,?,?,?,?,?)',
                                   (row['id'], name, row['name_aegis'], row['type'], row['slots'],
                                    row['price_buy'], row['price_sell'], packed(data)))
                    else:
                        db.execute('INSERT INTO monsters VALUES(?,?,?,?,?,?,?,?,?)',
                                   (row['id'], name, row['name_aegis'], int(row['id'] in mvp_ids),
                                    row['level'], row['hp'], data['base_exp'], data['job_exp'], packed(data)))
                    if kind == 'monsters':
                        for ordinal, drop in enumerate(data['drops']):
                            db.execute('INSERT INTO drops VALUES(?,?,?,?,?,?)',
                                       (row['id'], drop['item_id'], ordinal, drop.get('rate'), drop.get('real_rate'), packed(drop)))
                        for ordinal, reward in enumerate(row.get('mvp_drops', [])):
                            matches = db.execute('SELECT id FROM items WHERE aegis=?', (reward['item'],)).fetchall()
                            item_id = matches[0][0] if len(matches) == 1 else None
                            db.execute('INSERT INTO mvp_rewards VALUES(?,?,?,?,?,?)',
                                       (row['id'], ordinal, item_id, reward['item'], reward.get('rate'), packed(reward)))
                    if count % 100 == 0 or count == len(listing):
                        print(f'{kind}: {count}/{len(listing)}', flush=True)
        meta = {'schema_version': '1', 'source': BASE, 'retrieved_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'items': str(len(items)), 'monsters': str(len(monsters)), 'mvp': str(len(mvps)),
                'payload_encoding': 'zlib+utf8-json', 'images': 'remote URLs; binaries excluded',
                'item_image_template': 'https://tidesofcomodo.com/db/collection-img/{id}.png',
                'monster_image_template': 'https://tidesofcomodo.com/db/mob-img/{id}.gif'}
        db.executemany('INSERT INTO metadata VALUES(?,?)', meta.items())
        db.commit()
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('SQLite integrity check failed')
        db.execute('VACUUM')
        db.close()
        temporary.replace(output)
        report = {**meta, 'bytes': output.stat().st_size, 'sha256': hashlib.sha256(output.read_bytes()).hexdigest()}
        output.with_suffix('.manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(report, indent=2), flush=True)
    except BaseException:
        db.close()
        raise


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--cache', required=True, help='External cache directory; use a new directory for each snapshot')
    p.add_argument('--output', type=pathlib.Path, default=ROOT / 'Data/database.toc.sqlite')
    p.add_argument('--workers', type=int, default=6)
    args = p.parse_args()
    if not 1 <= args.workers <= 8:
        p.error('workers must be 1..8')
    import tempfile
    from enrich_toc_respawns import enrich
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.output.parent, prefix='.toc-import-') as temporary:
        stage = pathlib.Path(temporary) / args.output.name
        source = Source(args.cache)
        build(stage, source, args.workers)
        enrich(stage, source, args.workers)
        stage.replace(args.output)
        stage.with_suffix('.manifest.json').replace(args.output.with_suffix('.manifest.json'))


if __name__ == '__main__':
    main()
