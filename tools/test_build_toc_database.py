import importlib.util
import pathlib
import contextlib
import io
import sqlite3
import tempfile
import unittest
from inspect_toc_database import inspect

spec = importlib.util.spec_from_file_location('toc', pathlib.Path(__file__).with_name('build_toc_database.py'))
toc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(toc)


class Fixture:
    def __init__(self, fail=False):
        self.fail = fail
        self.item = dict(id=501, name_aegis='Red_Potion', name_english='Red Potion', type='Healing', slots=0,
                         price_buy=50, price_sell=25, script='itemheal rand(45,65),0;', desc='Описание')
        self.monster = dict(id=1002, name_aegis='PORING', name_english='Poring', is_mvp=True, level=1, hp=50,
                            base_exp=2, job_exp=1, mvp_drops=[dict(item='Red_Potion', rate=2000)])

    def listing(self, kind, extra=''):
        return [self.item] if kind == 'items' else [dict(self.monster, base_exp=14, job_exp=7)]

    def get(self, path):
        if self.fail:
            raise RuntimeError('network failure')
        if path.startswith('/items/'):
            return dict(item=self.item, dropped_by=[dict(monster_id=1002, rate=7000)])
        return dict(monster=self.monster, base_exp=14, job_exp=7, mvp_exp=0,
                    drops=[dict(item_id=501, rate=7000, real_rate=10000)],
                    skills=[dict(name='Heal', level=1)], spawns=[dict(map_name='test', amount=2, respawn=60000)])


class CatalogTests(unittest.TestCase):
    def test_roundtrip_and_queries(self):
        with tempfile.TemporaryDirectory() as temp:
            path = pathlib.Path(temp) / 'db.sqlite'
            with contextlib.redirect_stdout(io.StringIO()):
                toc.build(path, Fixture(), 1)
            with sqlite3.connect(path) as db:
                self.assertEqual(db.execute('PRAGMA integrity_check').fetchone()[0], 'ok')
                self.assertEqual(db.execute('SELECT id FROM mvp').fetchall(), [(1002,)])
                raw = toc.unpacked(db.execute('SELECT payload FROM monsters').fetchone()[0])
                self.assertEqual(raw['monster']['base_exp'], 2)
                self.assertEqual(raw['base_exp'], 14)
                self.assertEqual(raw['drops'][0]['real_rate'], 10000)
                self.assertEqual(raw['spawns'][0]['respawn'], 60000)
                self.assertEqual(raw['skills'][0]['name'], 'Heal')
                self.assertEqual(db.execute('SELECT monster_id,item_id,rate FROM mvp_rewards').fetchall(), [(1002,501,2000)])
                item = toc.unpacked(db.execute('SELECT payload FROM items').fetchone()[0])
                self.assertEqual(item['item']['desc'], 'Описание')
                self.assertEqual(db.execute('SELECT monster_id,rate,real_rate FROM drops WHERE item_id=501').fetchall(), [(1002,7000,10000)])
            self.assertEqual(inspect(path)['mvp_rewards'], 1)
            self.assertEqual(inspect(path, 'item', 501)['dropped_by'][0]['monster_id'], 1002)
            manifest = path.with_suffix('.manifest.json')
            manifest.write_text('{"sha256":"incorrect"}')
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                inspect(path)

    def test_failed_import_preserves_existing_snapshot(self):
        with tempfile.TemporaryDirectory() as temp:
            path = pathlib.Path(temp) / 'db.sqlite'
            path.write_bytes(b'existing accepted snapshot')
            with self.assertRaises(RuntimeError):
                toc.build(path, Fixture(fail=True), 1)
            self.assertEqual(path.read_bytes(), b'existing accepted snapshot')

    def test_empty_snapshot_rejected(self):
        source = Fixture()
        source.listing = lambda kind, extra='': []
        with tempfile.TemporaryDirectory() as temp:
            path = pathlib.Path(temp) / 'db.sqlite'
            path.write_bytes(b'preserve me')
            with self.assertRaises(ValueError):
                toc.build(path, source, 1)
            self.assertEqual(path.read_bytes(), b'preserve me')

    def test_listing_rejects_duplicate_ids(self):
        source = toc.Source.__new__(toc.Source)
        source.get = lambda path: dict(total=2, total_pages=1, results=[dict(id=1), dict(id=1)])
        with self.assertRaises(ValueError):
            source.listing('items')

    def test_listing_rejects_changed_total(self):
        source = toc.Source.__new__(toc.Source)
        source.get = lambda path: dict(total=2 if 'page=1&' in path else 3,
                                       total_pages=2, results=[dict(id=1)])
        with self.assertRaises(ValueError):
            source.listing('items')

    def test_mvp_disagreement_rejected(self):
        source = Fixture()
        original = source.listing
        source.listing = lambda kind, extra='': [] if extra else original(kind)
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                toc.build(pathlib.Path(temp) / 'db.sqlite', source, 1)


if __name__ == '__main__':
    unittest.main()
