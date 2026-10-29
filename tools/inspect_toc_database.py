#!/usr/bin/env python3
"""Validate a ToC snapshot or print a complete offline item/monster record."""
import argparse
from contextlib import closing
import hashlib
import json
import pathlib
import sqlite3
import zlib
from build_toc_database import unpacked


def inspect(path, kind=None, identifier=None):
    with closing(sqlite3.connect(path.resolve().as_uri() + '?mode=ro', uri=True)) as db:
        if kind:
            table = {'item': 'items', 'monster': 'monsters', 'mvp': 'mvp'}[kind]
            row = db.execute(f'SELECT payload FROM {table} WHERE id=?', (identifier,)).fetchone()
            if row is None:
                raise ValueError(f'{kind} {identifier} not found')
            record = unpacked(row[0])
            if kind == 'item':
                record['dropped_by'] = [dict(monster_id=mid, monster_name=name, drop=unpacked(payload))
                                        for mid, name, payload in db.execute(
                                            'SELECT d.monster_id,m.name,d.payload FROM drops d JOIN monsters m ON m.id=d.monster_id '
                                            'WHERE d.item_id=? ORDER BY d.monster_id,d.ordinal', (identifier,))]
            return record
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('SQLite integrity check failed')
        meta = dict(db.execute('SELECT key,value FROM metadata'))
        uncompressed_bytes = 0
        for table, key in [('items', 'item'), ('monsters', 'monster')]:
            rows = db.execute(f'SELECT id,payload FROM {table}').fetchall()
            if len(rows) != int(meta[table]):
                raise ValueError(f'{table} count mismatch')
            for identifier, payload in rows:
                uncompressed_bytes += len(zlib.decompress(payload))
                record = unpacked(payload)
                if record[key]['id'] != identifier:
                    raise ValueError('Payload ID mismatch')
        mvp = db.execute('SELECT count(*) FROM mvp').fetchone()[0]
        if mvp != int(meta['mvp']):
            raise ValueError('MVP count mismatch')
        manifest = path.with_suffix('.manifest.json')
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if manifest.exists() and json.loads(manifest.read_text(encoding='utf-8'))['sha256'] != digest:
            raise ValueError('Manifest SHA-256 mismatch')
        unresolved = db.execute('SELECT DISTINCT item_id FROM drops WHERE item_id NOT IN (SELECT id FROM items)').fetchall()
        return dict(metadata=meta, uncompressed_record_json_bytes=uncompressed_bytes, bytes=path.stat().st_size, sha256=digest,
                    mvp_rewards=db.execute('SELECT count(*) FROM mvp_rewards').fetchone()[0],
                    unresolved_mvp_reward_aegis=[r[0] for r in db.execute('SELECT DISTINCT aegis FROM mvp_rewards WHERE item_id IS NULL')],
                    drops=db.execute('SELECT count(*) FROM drops').fetchone()[0], unresolved_drop_item_ids=[r[0] for r in unresolved])


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('database', type=pathlib.Path)
    p.add_argument('--kind', choices=['item', 'monster', 'mvp'])
    p.add_argument('--id', type=int)
    a = p.parse_args()
    if bool(a.kind) != (a.id is not None):
        p.error('--kind and --id must be supplied together')
    print(json.dumps(inspect(a.database, a.kind, a.id), ensure_ascii=False, indent=2))
