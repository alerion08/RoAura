import contextlib
import io
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest

from selection import quest_key, select_pages, filter_catalog, snapshots
from semantic import build, coordinates, resolve_context, item_operation, validate, sha, ROOT


class ParsingTests(unittest.TestCase):
    def test_coordinate_formats_and_original_case(self):
        for text in ['prontera 249,132','prontera 249 132','prontera (249, 132)',
                     'prontera (249 132)','prontera, 249 132','(prontera): 249,132',
                     '((prontera): 249,132)']:
            with self.subTest(text=text):
                points=coordinates(text,{'prontera'})
                self.assertEqual([(p['map'],p['x'],p['y']) for p in points],[('prontera',249,132)])
        p=coordinates('Hugel (196 169)',{'hugel'})
        self.assertEqual(p,[])  # display name is not silently changed to map code

    def test_numbers_are_not_coordinates(self):
        for text in ['249 132','Level 50 reward 50,000 EXP; bring 10 Jellopy.',
                     'Receive 3,075 EXP and 1,125 Zeny','prontera 999,132',
                     'item_count 10 20','Buy 10 Jellopy for 249 132 Zeny']:
            with self.subTest(text=text):self.assertEqual(coordinates(text,{'prontera'}),[])
        self.assertIsNone(coordinates('Talk to Jade (173,238).',set())[0]['map'])
        p=coordinates('visit custom_map 249 132',set())[0]
        self.assertFalse(p['known'])

    def test_item_operation_is_clause_local(self):
        self.assertEqual(item_operation('Bring 10 Jellopy and 2 Meat','Meat'),'bring')
        self.assertEqual(item_operation('Bring 10 Jellopy and receive 2 Meat','Meat'),'reward')
        self.assertEqual(item_operation('Buy 10 Jellopy','Jellopy'),'buy')
        self.assertEqual(item_operation('Level 10; Jellopy costs 50 Zeny','Jellopy'),'mention')

    def test_context_requires_unique_immediately_preceding_npc(self):
        rows=[{'step_no':1,'instruction':'Talk to Zelmeto.','explicit_interlocutors':['Zelmeto']},
              {'step_no':2,'instruction':'Return to him.'},
              {'step_no':3,'instruction':'Talk to him.'},
              {'step_no':4,'instruction':'Talk to both.','explicit_interlocutors':['Zelmeto','Liotzburg']},
              {'step_no':5,'instruction':'Give him the materials.'}]
        r=resolve_context(rows)
        self.assertEqual(r[2],{'npc':'Zelmeto','support_step':1})
        self.assertIsNone(r[3]['npc']);self.assertIsNone(r[5]['npc'])

    def test_term_or_year_does_not_establish_archival_status(self):
        c=sqlite3.connect(':memory:')
        c.execute('CREATE TABLE wiki_pages(id,title,url,content_sha)')
        c.executemany('INSERT INTO wiki_pages VALUES(?,?,?,?)',[
            (1,'Festival Training','https://irowiki.org/classic/Festival_Training','s'),
            (2,'Training 2010','https://irowiki.org/classic/Training_2010','s')])
        result=select_pages(c,{})
        self.assertEqual(result[1]['state'],'quarantined')
        self.assertEqual(result[2]['state'],'included')
        self.assertFalse(any(r['state']=='excluded_archived' for r in result.values()))
        c.close()


class RealCorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source=ROOT/'output/quest_research.sqlite'
        if not cls.source.exists():raise unittest.SkipTest('Pinned source archive not present')
        cls.temp=tempfile.TemporaryDirectory();cls.out=Path(cls.temp.name)
        with contextlib.redirect_stdout(io.StringIO()):cls.report=build(cls.source,cls.out)
        cls.db=sqlite3.connect(cls.out/'quest_semantic.sqlite')
        cls.db.row_factory=sqlite3.Row

    @classmethod
    def tearDownClass(cls):
        cls.db.close();cls.temp.cleanup()

    def test_all_active_original_steps_are_preserved(self):
        source=sqlite3.connect(self.source)
        active={r['quest_id'] for r in select_pages(source).values() if r['state']=='included'}
        self.assertEqual(active,{r[0] for r in self.db.execute('SELECT quest_id FROM quests')})
        actual={(r[0],r[1]):r[2] for r in self.db.execute('SELECT quest_id,step_order,original_instruction FROM quest_steps')}
        expected={(quest_key(url),n):text for url,n,text in source.execute('SELECT p.url,s.step_no,s.instruction FROM wiki_steps s JOIN wiki_pages p ON p.id=s.page_id') if quest_key(url) in active}
        self.assertEqual(actual,expected);source.close()

    def test_event_exclusion_and_false_positive_retention(self):
        names={r[0] for r in self.db.execute('SELECT original_name FROM quests')}
        self.assertIn('WoE 2 Guild Dungeon Event',names)
        self.assertIn('Nifflheim Spotlight',names)
        self.assertNotIn('2013 Easter Event',names)
        self.assertNotIn('Adventure of the Tarnished Lamp',names)  # quarantined, not falsely retired
        selection=json.loads((self.out/'semantic_selection.json').read_text())['records']
        self.assertEqual(len(selection),286)
        self.assertEqual(sum(r['state']=='excluded_archived' for r in selection),59)
        self.assertEqual(sum(r['state']=='quarantined' for r in selection),10)
        exported=json.loads((self.out/'active_quests.json').read_text())['quests']
        self.assertTrue(all(r['state']=='included' for r in exported))

    def test_partial_localization_is_not_faked(self):
        self.assertEqual(self.report['fully_localized_steps'],76)
        self.assertEqual(self.report['not_fully_localized_steps'],2677)
        self.assertFalse(self.report['complete'])
        self.assertEqual(self.db.execute('SELECT count(*) FROM ready_steps').fetchone()[0],76)
        missing=self.db.execute("SELECT count(*) FROM review_queue WHERE reason='localization_missing'").fetchone()[0]
        self.assertEqual(missing,2677)

    def test_source_hashes_and_manual_curation_guard(self):
        folder=self.out/'changed-curation';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Juice_Quest.json';data=json.loads(path.read_text())
        data['steps']['1']['instruction_sha256']='wrong';path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'changed; re-review'):
            build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_human_reviewed_database_cannot_be_silently_replaced(self):
        folder=self.out/'human';folder.mkdir()
        target=folder/'quest_semantic.sqlite';shutil.copyfile(self.out/'quest_semantic.sqlite',target)
        c=sqlite3.connect(target);c.execute("UPDATE step_descriptions SET review_state='human_reviewed' WHERE step_id=(SELECT step_id FROM step_descriptions LIMIT 1)");c.commit();c.close()
        before=target.read_bytes()
        with self.assertRaisesRegex(ValueError,'Human-reviewed'):build(self.source,folder)
        self.assertEqual(before,target.read_bytes())

    def test_rebuild_is_byte_identical_and_uses_cache(self):
        before=(self.out/'quest_semantic.sqlite').read_bytes();report=(self.out/'semantic_report.json').read_bytes()
        stream=io.StringIO()
        with contextlib.redirect_stdout(stream):build(self.source,self.out)
        self.assertIn('"cache_hits": 217',stream.getvalue())
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())
        self.assertEqual(report,(self.out/'semantic_report.json').read_bytes())

    def test_rewards_are_not_required_items(self):
        q='irowiki-classic:Prontera_Culverts_Entrance_Quest'
        self.assertEqual(self.db.execute('SELECT count(*) FROM step_items i JOIN quest_steps s USING(step_id) WHERE s.quest_id=?',(q,)).fetchone()[0],0)
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='item'",(q,)).fetchone()[0],3)
        juice='irowiki-classic:Juice_Quest'
        self.assertEqual(self.db.execute("SELECT count(*) FROM step_items i JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND i.operation='consume'",(juice,)).fetchone()[0],0)
        self.assertTrue(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='requirement_conflict'",(juice,)).fetchone())

    def test_contextual_delivery_and_optional_exchange(self):
        rows=self.db.execute("SELECT e.original_name,i.quantity FROM step_items i JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id='irowiki-classic:Factory_Quest' AND s.step_order=5 ORDER BY e.original_name").fetchall()
        self.assertEqual([tuple(r) for r in rows],[('Flexible Tube',20),('Rusty Screw',10),('Used Iron Plate',10)])
        self.assertEqual(self.db.execute("SELECT count(*) FROM evidence_context c JOIN evidence e USING(evidence_id) WHERE e.quest_id='irowiki-classic:Factory_Quest'").fetchone()[0],2)
        self.assertEqual(self.db.execute("SELECT count(*) FROM step_items i JOIN quest_steps s USING(step_id) WHERE s.quest_id='irowiki-classic:Gemstone_Exchanging' AND i.required=0 AND i.condition_text IS NOT NULL").fetchone()[0],3)

    def test_ids_aliases_and_typed_spans(self):
        for item_id in [715,716,717]:
            self.assertEqual(self.db.execute('SELECT count(*) FROM items WHERE game_id=?',(item_id,)).fetchone()[0],1)
        self.assertEqual(self.db.execute("SELECT count(*) FROM maps WHERE map_code='prontera'").fetchone()[0],1)
        self.assertGreater(self.db.execute("SELECT count(*) FROM description_mentions WHERE entity_type='npc' AND field='full_ru'").fetchone()[0],20)
        self.assertEqual(validate(self.db)['invalid_mentions'],0)
        c=sqlite3.connect(':memory:');self.db.backup(c)
        c.execute('UPDATE description_mentions SET start_offset=start_offset+1 WHERE mention_id=(SELECT mention_id FROM description_mentions LIMIT 1)')
        with self.assertRaisesRegex(ValueError,'invalid_mention_span'):validate(c)
        c.close()

    def test_literal_answer_and_no_invented_images_or_server_verification(self):
        self.assertEqual(tuple(self.db.execute("SELECT exact_text,correct FROM dialogue_options WHERE exact_text='Volunteer.'").fetchone()),('Volunteer.',1))
        self.assertEqual(self.db.execute('SELECT count(*) FROM step_images').fetchone()[0],0)
        self.assertEqual(self.db.execute('SELECT count(*) FROM variants WHERE server IS NOT NULL').fetchone()[0],0)
        self.assertEqual(self.db.execute("SELECT count(*) FROM evidence WHERE status='server_verified'").fetchone()[0],0)
        self.assertEqual(self.db.execute("SELECT count(*) FROM locations WHERE point_verified<>0").fetchone()[0],0)
        self.assertGreater(self.db.execute('SELECT count(*) FROM locations WHERE map_id IS NULL').fetchone()[0],0)

    def test_legacy_catalog_filter_removes_dependent_rows(self):
        source=sqlite3.connect(self.source);out=sqlite3.connect(':memory:')
        out.executescript('''PRAGMA foreign_keys=ON; CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT);
          CREATE TABLE quests(quest_key TEXT PRIMARY KEY);
          CREATE TABLE steps(quest_key TEXT REFERENCES quests,step_no INTEGER);
          CREATE TABLE deps(quest_key TEXT REFERENCES quests,prerequisite_key TEXT REFERENCES quests);''')
        keep='irowiki-classic:Juice_Quest';remove='irowiki-classic:2013_Easter_Event'
        out.executemany('INSERT INTO quests VALUES(?)',[(keep,),(remove,)])
        out.executemany('INSERT INTO steps VALUES(?,1)',[(keep,),(remove,)])
        out.execute('INSERT INTO deps VALUES(?,?)',(keep,remove))
        filter_catalog(out,source)
        self.assertEqual(out.execute('SELECT * FROM quests').fetchall(),[(keep,)])
        self.assertEqual(out.execute('SELECT * FROM steps').fetchall(),[(keep,1)])
        self.assertEqual(out.execute('SELECT * FROM deps').fetchall(),[])
        self.assertFalse(out.execute('PRAGMA foreign_key_check').fetchall())
        out.close();source.close()

    def test_identical_npc_names_are_not_merged(self):
        rows=self.db.execute("""SELECT s.step_order,n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Lost_Child_Quest' AND s.step_order IN (5,6) ORDER BY s.step_order""").fetchall()
        self.assertEqual(len(rows),2)
        self.assertNotEqual(rows[0][1],rows[1][1])
        for order,eid in rows:
            actual=self.db.execute("""SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
              WHERE s.quest_id='irowiki-classic:Lost_Child_Quest' AND s.step_order=? AND m.surface='Idle Merchant'""",(order,)).fetchall()
            self.assertEqual({r[0] for r in actual},{eid})

    def test_same_surface_item_and_creature_have_distinct_spans(self):
        for field in ('full_ru','full_en'):
            rows=self.db.execute("""SELECT entity_type FROM description_mentions m JOIN quest_steps s USING(step_id)
              WHERE s.quest_id='irowiki-classic:Amatsu_Dungeon_Entrance_Quest' AND s.step_order=6
              AND m.field=? AND m.surface='Nine Tails' ORDER BY start_offset""",(field,)).fetchall()
            self.assertEqual([r[0] for r in rows],['monster','item'])
        self.assertFalse(self.db.execute("""SELECT 1 FROM step_monsters m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Amatsu_Dungeon_Entrance_Quest' AND m.operation='kill'""").fetchone())

    def test_pond_kid_is_not_the_lost_child(self):
        rows=self.db.execute("""SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Lost_Child_Quest' AND s.step_order=9
          AND field='full_ru' AND surface='Kid' ORDER BY start_offset""").fetchall()
        self.assertEqual(len(rows),2);self.assertNotEqual(rows[0][0],rows[1][0])

    def test_shared_zhed_identity_and_export_evidence(self):
        rows=self.db.execute("SELECT entity_id FROM entities WHERE original_name='High Priest Zhed'").fetchall()
        self.assertEqual(len(rows),1)
        examples=json.loads((self.out/'semantic_examples.json').read_text())
        for slug in ('Lost_Child_Quest','Rachel_Sanctuary_Quest'):
            example=next(r for r in examples if r['quest_id']=='irowiki-classic:'+slug)
            self.assertIn(rows[0][0],{e['entity_id'] for e in example['entities']})

    def test_abyss_header_requirements_have_html_evidence(self):
        rows=self.db.execute("""SELECT e.original_name,r.amount,s.kind FROM requirements r
          JOIN entities e USING(entity_id) JOIN evidence v ON v.evidence_id=r.evidence_id JOIN sources s USING(source_id)
          WHERE r.quest_id='irowiki-classic:Abyss_Lake_Entrance_Guide' AND r.step_id IS NULL ORDER BY e.original_name""").fetchall()
        self.assertEqual([tuple(r) for r in rows],[(x,'1','html_snapshot') for x in ('Dragon Canine','Dragon Scale','Dragon Tail')])

    def test_server_donations_are_not_player_costs(self):
        q='irowiki-classic:Rachel_Sanctuary_Quest'
        rows=dict(self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind LIKE 'server_donation_%'",(q,)))
        self.assertEqual(rows,{'server_donation_count':'10000','server_donation_total_zeny':'500000000'})
        self.assertFalse(self.db.execute("SELECT 1 FROM requirements WHERE quest_id=? AND kind='zeny'",(q,)).fetchone())
        rewards=dict(self.db.execute('SELECT kind,SUM(CAST(amount AS INTEGER)) FROM rewards WHERE quest_id=? GROUP BY kind',(q,)))
        self.assertEqual(rewards['base_exp'],1100000);self.assertEqual(rewards['job_exp'],600000)
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id=? AND item_id IS NOT NULL",(q,)).fetchone())

    def test_turtle_cost_is_per_trip_and_exact_answer_preserved(self):
        row=self.db.execute("SELECT amount,condition_text FROM requirements WHERE quest_id='irowiki-classic:Turtle_Island_Entrance_Quest' AND kind='zeny'").fetchone()
        self.assertEqual(row[0],'10000');self.assertIn('Per journey',row[1])
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id='irowiki-classic:Turtle_Island_Entrance_Quest' AND kind='zeny'").fetchone())
        self.assertTrue(self.db.execute("SELECT 1 FROM dialogue_options WHERE exact_text='How can I get there?' AND correct=1").fetchone())

    def test_ayothaya_material_totals_are_not_double_deliveries(self):
        q='irowiki-classic:Ayothaya_Dungeon_Entrance_Quest'
        rows=self.db.execute("""SELECT e.original_name,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=3 ORDER BY e.original_name""",(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(n,1) for n in ('Holy Water','Needle Packet','Solid Husk','Spool')])
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND item_id=(SELECT entity_id FROM items WHERE game_id=603)",(q,)).fetchone()[0],1)

    def test_curation_rejects_stale_metadata_duplicates_and_broken_locales(self):
        mutations=[
            ('metadata',lambda d:d.update(source_sha256='stale'),'metadata/source changed'),
            ('html',lambda d:d.update(snapshot_sha256='stale'),'HTML snapshot changed'),
            ('steps',lambda d:d['steps'].update({'99':dict(d['steps']['1'])}),'step coverage mismatch'),
            ('identity',lambda d:d['steps']['2'].update(step_id=d['steps']['1']['step_id']),'Duplicate curated step identity'),
            ('short_digits',lambda d:d['steps']['1'].update(short_ru=d['steps']['1']['short_ru']+' 999'),'numeric mismatch'),
            ('answer',lambda d:d['steps']['2'].update(full_ru=d['steps']['2']['full_ru'].replace('How can I get there?','Как попасть туда?')),'exact dialogue answer'),
            ('protected_en',lambda d:d['steps']['1'].update(full_en=d['steps']['1']['full_en'].replace('Alberta','Альберта')),'protected name'),
        ]
        for name,mutate,message in mutations:
            with self.subTest(name=name):
                folder=self.out/('invalid-'+name);shutil.copytree(ROOT/'curation',folder)
                path=folder/'Turtle_Island_Entrance_Quest.json';data=json.loads(path.read_text());mutate(data);path.write_text(json.dumps(data))
                before=(self.out/'quest_semantic.sqlite').read_bytes()
                with self.assertRaisesRegex(ValueError,message):build(self.source,self.out,curation_dir=folder)
                self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())


if __name__=='__main__':unittest.main()
