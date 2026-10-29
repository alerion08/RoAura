#!/usr/bin/env python3
"""Enrich an existing ToC snapshot from the official map API, atomically."""
import argparse
from contextlib import closing
import concurrent.futures
import datetime
import hashlib
import json
import pathlib
import sqlite3
from build_toc_database import Source, packed, unpacked


def extract(code, data, mvp_ids):
    if data['map']['name'] != code:
        raise ValueError('Map response does not match requested map')
    rows = []
    for spawn in data['spawns']:
        if spawn['mob_id'] not in mvp_ids:
            continue
        for location in spawn['spawn_locations']:
            rows.append(dict(map_code=code, monster_id=spawn['mob_id'], map_name=data['map']['map_name'],
                             source=f'https://tidesofcomodo.com/db/map/{code}', **location))
    return rows


def enrich(path, source, workers=6):
    path = pathlib.Path(path)
    with closing(sqlite3.connect(path)) as db:
        records = [unpacked(r[0]) for r in db.execute('SELECT payload FROM mvp')]
    ids = {r['monster']['id'] for r in records}
    codes = sorted({s['map_name'] for r in records for s in r['spawns']})
    def fetch(code):
        return extract(code, source.get('/maps/' + code), ids)
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        groups = list(pool.map(fetch, codes))
    rows = [r for group in groups for r in group]
    if not rows:
        raise ValueError('No map spawn data returned; keeping original snapshot')
    temporary = path.with_suffix('.enriching')
    try:
        with closing(sqlite3.connect(path)) as original, closing(sqlite3.connect(temporary)) as db:
            original.backup(db)
            db.execute('DROP TABLE IF EXISTS mvp_respawns')
            db.execute('CREATE TABLE mvp_respawns(id INTEGER PRIMARY KEY,payload BLOB NOT NULL)')
            db.executemany('INSERT INTO mvp_respawns(payload) VALUES(?)', [(packed(r),) for r in rows])
            updates = dict(schema_version='2', map_respawn_sources=str(len(codes)),
                           map_respawn_rows=str(len(rows)), respawns_retrieved_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
            db.executemany('INSERT OR REPLACE INTO metadata VALUES(?,?)', updates.items())
            db.commit()
            if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                raise ValueError('Enriched snapshot integrity failed')
            db.execute('VACUUM')
            metadata = dict(db.execute('SELECT key,value FROM metadata'))
        temporary.replace(path)
        metadata.update(bytes=path.stat().st_size, sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        path.with_suffix('.manifest.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(metadata, indent=2), flush=True)
    finally:
        if temporary.exists():
            temporary.unlink()


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('database', type=pathlib.Path)
    p.add_argument('--cache', required=True)
    p.add_argument('--workers', type=int, choices=range(1, 9), default=6)
    a = p.parse_args()
    enrich(a.database, Source(a.cache), a.workers)
