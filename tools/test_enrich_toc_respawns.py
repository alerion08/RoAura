import contextlib
import io
import pathlib
import tempfile
import unittest
from test_build_toc_database import Fixture, toc
from enrich_toc_respawns import extract, enrich
from inspect_toc_database import inspect

class MapSource:
    def __init__(self, fail=False): self.fail = fail
    def get(self, path):
        if self.fail: raise RuntimeError('network unavailable')
        return dict(map=dict(name='test', map_name='Test map'), spawns=[dict(mob_id=1002,
            spawn_locations=[dict(amount=1, delay1=7200000, delay2=600000, spawn_time='120~130 min')])])

class RespawnTests(unittest.TestCase):
    def test_map_identity_and_exact_intervals(self):
        data = MapSource().get('/maps/test')
        rows = extract('test', data, {1002})
        self.assertEqual(rows[0]['delay1'], 7200000)
        self.assertEqual(rows[0]['delay2'], 600000)
        self.assertEqual(rows[0]['spawn_time'], '120~130 min')
        self.assertEqual(extract('test', data, {1039}), [])
        with self.assertRaises(ValueError): extract('other', data, {1002})

    def test_enrichment_roundtrip_and_failure_preserves_snapshot(self):
        with tempfile.TemporaryDirectory() as temp, contextlib.redirect_stdout(io.StringIO()):
            path = pathlib.Path(temp) / 'db.sqlite'
            toc.build(path, Fixture(), 1)
            enrich(path, MapSource())
            self.assertEqual(inspect(path)['metadata']['map_respawn_rows'], '1')
            before = path.read_bytes()
            manifest = path.with_suffix('.manifest.json').read_bytes()
            with self.assertRaises(RuntimeError): enrich(path, MapSource(True))
            self.assertEqual(path.read_bytes(), before)
            self.assertEqual(path.with_suffix('.manifest.json').read_bytes(), manifest)
