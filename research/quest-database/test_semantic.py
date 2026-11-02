import contextlib
import io
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest

from selection import quest_key, select_pages, filter_catalog, snapshots
from semantic import build, coordinates, resolve_context, item_operation, validate, sha, ROOT, SCHEMA, BRANCH_SCHEMA, ADDITION_SCHEMA, ROUTE_SCHEMA, migrate_semantic, mention_entities, entity_id_proof, Writer


class ParsingTests(unittest.TestCase):
    def test_cross_guide_id_uses_exact_pinned_name_type_and_id(self):
        url='http://db.irowiki.org/classic/item-info/6080/'
        donor={'sha256':'pinned','html':f'<div class="mw-parser-output"><a href="{url}">Manuk Coin</a></div>'}
        owner={'html':'<div class="mw-parser-output">Manuk Coin without a link</div>'}
        entity={'type':'item','name':'Manuk Coin','game_id':6080,'id_source':url,'id_evidence_snapshot':{'page':'Tribe_Investigation','sha256':'pinned'}}
        proof,link=entity_id_proof(entity,owner,{'Tribe_Investigation':donor})
        self.assertIs(proof,donor);self.assertEqual((link['type'],link['id'],link['name']),('item',6080,'Manuk Coin'))
        for changes in [{'type':'monster'},{'game_id':6081},{'name':'Splendide Coin'},{'name':'Unknown Coin','aliases':['Manuk Coin']}]:
            with self.subTest(changes=changes),self.assertRaisesRegex(ValueError,'Unverified game ID'):
                entity_id_proof(dict(entity,**changes),owner,{'Tribe_Investigation':donor})

    def test_cross_guide_id_rejects_stale_missing_and_malformed_proof(self):
        url='http://db.irowiki.org/classic/item-info/6080/'
        donor={'sha256':'pinned','html':f'<div class="mw-parser-output"><a href="{url}">Manuk Coin</a></div>'}
        entity={'type':'item','name':'Manuk Coin','game_id':6080,'id_source':url,'id_evidence_snapshot':{'page':'Tribe','sha256':'pinned'}}
        for proof in [{'page':'Missing','sha256':'pinned'},{'page':'Tribe','sha256':'stale'},{'page':[],'sha256':'pinned'},None,{}]:
            candidate=dict(entity,id_evidence_snapshot=proof)
            with self.subTest(proof=proof),self.assertRaises(ValueError):entity_id_proof(candidate,None,{'Tribe':donor})
        with self.assertRaisesRegex(ValueError,'ID source contradicts'):
            entity_id_proof(dict(entity,id_source='wrong'),None,{'Tribe':donor})
        with self.assertRaisesRegex(ValueError,'requires a game ID'):
            entity_id_proof(dict(entity,game_id=None),None,{'Tribe':donor})

    def test_own_snapshot_id_alias_proof_remains_compatible(self):
        snapshot={'html':'<div class="mw-parser-output"><a href="http://db.irowiki.org/classic/item-info/6075/">Crystallized Teardrops</a></div>'}
        entity={'type':'item','name':'Crystallized Teardrop','aliases':['Crystallized Teardrops'],'game_id':6075}
        proof,link=entity_id_proof(entity,snapshot,{})
        self.assertIs(proof,snapshot);self.assertEqual(link['id'],6075)
        self.assertIsNone(entity_id_proof({'type':'item','name':'Unknown'},None,{}))

    def test_writer_ignores_duplicates_but_never_check_constraint_failures(self):
        with contextlib.closing(sqlite3.connect(':memory:')) as db:
            db.execute('CREATE TABLE facts(id TEXT PRIMARY KEY,n INTEGER CHECK(n>0))')
            w=Writer(db);w.add('facts',id='a',n=1);w.add('facts',id='a',n=2)
            self.assertEqual(db.execute('SELECT n FROM facts').fetchone()[0],1)
            with self.assertRaises(sqlite3.IntegrityError):w.add('facts',id='bad',n=0)

    def test_literal_instance_maps_preserve_floor_and_reference_status(self):
        for text,name,x,y in [('Kruger (1@orcs 180,29)','1@orcs',180,29),('2@orcs 35 169','2@orcs',35,169),('at 2@orcs (35,169)','2@orcs',35,169)]:
            self.assertEqual([(p['map'],p['x'],p['y'],p['known']) for p in coordinates(text,set())],[(name,x,y,False)])
        self.assertTrue(coordinates('1@orcs 180,29',{'1@orcs'})[0]['known'])

    def test_instance_map_parser_rejects_email_suffixes_and_missing_separators(self):
        for text in ['user1@orcs 180,29','user@orcs 180,29','1@orcs180,29','1@orcs 999,29','2 members 180 29']:
            with self.subTest(text=text):self.assertEqual(coordinates(text,set()),[])

    def test_non_entity_surface_exclusion_requires_review_and_no_conflicting_selection(self):
        entities={'guard':('npc-id','npc','ev')};surfaces={'Continental Guard':['guard']}
        entry={field:'Join Continental Guard.' for field in ['short_ru','short_en','full_ru','full_en']}
        self.assertIn('Continental Guard',mention_entities(None,entities,surfaces,entry))
        entry['non_entity_mentions']={'Continental Guard':'Organization in this entry, not the named NPC.'}
        self.assertEqual(mention_entities(None,entities,surfaces,entry),{})
        for exclusion in [{'Unknown':'Not known.'},{'Continental Guard':''}]:
            entry['non_entity_mentions']=exclusion
            with self.assertRaises(ValueError):mention_entities(None,entities,surfaces,entry)
        entry['non_entity_mentions']={'Continental Guard':'Organization.'};entry['mention_entities']={'Continental Guard':'guard'}
        with self.assertRaisesRegex(ValueError,'Conflicting'):mention_entities(None,entities,surfaces,entry)

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


class MigrationTests(unittest.TestCase):
    def seed(self):
        db=sqlite3.connect(':memory:');self.addCleanup(db.close)
        db.executescript(SCHEMA.read_text())
        db.executescript('''
          INSERT INTO variants VALUES('v','Classic',NULL,'source_only');
          INSERT INTO sources VALUES('source','https://example.test','hash','guide',NULL,NULL,'Original');
          INSERT INTO quests VALUES('q','Quest','test','v','source',NULL,NULL,NULL,NULL,'partial','unknown','hash');
          INSERT INTO quest_steps VALUES('s','q',1,1,'numbered','Quest','Original','hash','talk','reviewed');
          INSERT INTO evidence VALUES('ev','source','q','s','Original','literal','source_literal',NULL);
          INSERT INTO step_descriptions VALUES('s','Кратко','Short','Полностью','Full','manual','human_reviewed','hash','Human');
          INSERT INTO step_actions VALUES('action','s',1,'talk',NULL,NULL,'ev');
        ''')
        return db

    def test_additive_migration_preserves_ids_human_text_and_facts(self):
        db=self.seed()
        before={table:db.execute('SELECT * FROM '+table).fetchall() for table in ('quests','quest_steps','step_descriptions','evidence')}
        migrate_semantic(db);migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],5)
        for table,rows in before.items():self.assertEqual(db.execute('SELECT * FROM '+table).fetchall(),rows)
        action=db.execute('SELECT * FROM step_actions').fetchone()
        self.assertEqual(action,('action','s',1,'talk',None,None,'ev',None))
        self.assertEqual(db.execute('PRAGMA foreign_key_check').fetchall(),[])

    def test_failed_migration_rolls_back_schema_and_version(self):
        db=self.seed();db.execute('CREATE TABLE branch_mentions(blocker TEXT)')
        before=db.execute('SELECT * FROM step_descriptions').fetchall()
        with self.assertRaises(sqlite3.OperationalError):migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],1)
        self.assertIsNone(db.execute("SELECT name FROM sqlite_master WHERE name='step_branches'").fetchone())
        self.assertEqual(db.execute('SELECT * FROM step_descriptions').fetchall(),before)
        self.assertFalse(db.in_transaction)

    def test_schema_two_migration_preserves_human_branch(self):
        db=self.seed();db.executescript(BRANCH_SCHEMA.read_text())
        db.execute("INSERT INTO step_branches VALUES('branch','s','key','group','scope','condition','ev','human_reviewed')")
        db.execute("INSERT INTO branch_descriptions VALUES('branch','Кратко','Short','Полностью','Full','manual','human_reviewed','hash','Human')")
        db.commit()
        before=db.execute('SELECT * FROM branch_descriptions').fetchall()
        migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],5)
        self.assertEqual(db.execute('SELECT * FROM branch_descriptions').fetchall(),before)
        self.assertEqual(db.execute('SELECT count(*) FROM step_source_additions').fetchone()[0],0)

    def test_failed_schema_three_migration_rolls_back_to_two(self):
        db=self.seed();db.executescript(BRANCH_SCHEMA.read_text())
        db.execute('CREATE TABLE step_source_additions(blocker TEXT)')
        before=db.execute('SELECT * FROM step_descriptions').fetchall()
        with self.assertRaises(sqlite3.OperationalError):migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],2)
        self.assertEqual(db.execute('SELECT * FROM step_descriptions').fetchall(),before)
        self.assertFalse(db.in_transaction)

    def test_schema_three_migration_preserves_additions_and_locations(self):
        db=self.seed();db.executescript(BRANCH_SCHEMA.read_text()+ADDITION_SCHEMA.read_text())
        db.execute("INSERT INTO step_source_additions VALUES('a','s',1,'Ручной фрагмент','Manual fragment','ev','human_reviewed')")
        db.execute("INSERT INTO locations VALUES('p',NULL,1,2,'ev',0)")
        db.execute("INSERT INTO step_locations VALUES('s','p','return','ev')");db.commit()
        before=db.execute('SELECT * FROM step_source_additions').fetchall()
        migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],5)
        self.assertEqual(db.execute('SELECT * FROM step_source_additions').fetchall(),before)
        self.assertEqual(db.execute('SELECT * FROM step_locations').fetchone(),('s','p','return','ev',None,None))

    def test_failed_route_migration_rolls_back_to_three(self):
        db=self.seed();db.executescript(BRANCH_SCHEMA.read_text()+ADDITION_SCHEMA.read_text())
        db.execute('CREATE TABLE step_routes(blocker TEXT)')
        with self.assertRaises(sqlite3.OperationalError):migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],3)
        self.assertIsNone(db.execute("SELECT name FROM sqlite_master WHERE name='quest_routes'").fetchone())
        self.assertEqual(len(db.execute('PRAGMA table_info(step_locations)').fetchall()),4)
        self.assertFalse(db.in_transaction)

    def test_schema_four_migration_preserves_human_route(self):
        db=self.seed();db.executescript(BRANCH_SCHEMA.read_text()+ADDITION_SCHEMA.read_text()+ROUTE_SCHEMA.read_text())
        db.execute("INSERT INTO quest_routes VALUES('route','q','optional','optional',NULL,'Manual condition','ev','human_reviewed')")
        db.execute("INSERT INTO step_routes VALUES('s','route',1,'ev')");db.commit()
        before=db.execute('SELECT * FROM quest_routes').fetchall()
        migrate_semantic(db);migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],5)
        self.assertEqual(db.execute('SELECT * FROM quest_routes').fetchall(),before)
        self.assertEqual(db.execute('SELECT * FROM step_routes').fetchall(),[('s','route',1,'ev')])
        self.assertEqual(db.execute('SELECT count(*) FROM quest_prerequisite_groups').fetchone()[0],0)
        self.assertEqual(db.execute('PRAGMA foreign_key_check').fetchall(),[])

    def test_failed_prerequisite_migration_rolls_back_to_four(self):
        db=self.seed();db.executescript(BRANCH_SCHEMA.read_text()+ADDITION_SCHEMA.read_text()+ROUTE_SCHEMA.read_text())
        db.execute('CREATE TABLE quest_prerequisite_members(blocker TEXT)')
        before=db.execute('SELECT * FROM step_descriptions').fetchall()
        with self.assertRaises(sqlite3.OperationalError):migrate_semantic(db)
        self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0],4)
        self.assertIsNone(db.execute("SELECT name FROM sqlite_master WHERE name='quest_prerequisite_groups'").fetchone())
        self.assertEqual(db.execute('SELECT * FROM step_descriptions').fetchall(),before)
        self.assertFalse(db.in_transaction)


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
        self.assertEqual(self.report['fully_localized_steps'],942)
        self.assertEqual(self.report['not_fully_localized_steps'],1808)
        self.assertFalse(self.report['complete'])
        self.assertEqual(self.db.execute('SELECT count(*) FROM ready_steps').fetchone()[0],942)
        missing=self.db.execute("SELECT count(*) FROM review_queue WHERE reason='localization_missing'").fetchone()[0]
        self.assertEqual(missing,1808)
        self.assertEqual(self.report['fully_localized_branches'],124)
        self.assertEqual(self.report['curated_quests'],74)
        self.assertEqual(self.report['pending_quests'],142)
        self.assertEqual(self.report['quest_routes'],17)
        self.assertEqual(self.report['source_additions'],21)
        self.assertEqual(self.report['action_reuse_links'],5)

    def test_juperos_four_homonymous_books_and_return_scholar_have_typed_links(self):
        q='irowiki-classic:Juperos_Ruins_History_Quest'
        for field in ['full_ru','full_en']:
            books=[r[0] for r in self.db.execute('''SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
              WHERE s.quest_id=? AND s.step_order=4 AND m.field=? AND m.surface='Book' ORDER BY m.start_offset''',(q,field))]
            self.assertEqual(len(books),4);self.assertEqual(len(set(books)),4)
        actors=[r[0] for r in self.db.execute('''SELECT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (2,5,7,9) ORDER BY s.step_order''',(q,))]
        self.assertEqual(len(actors),4);self.assertEqual(len(set(actors)),1)
        self.assertEqual(self.db.execute('''SELECT count(DISTINCT l.location_id) FROM step_locations l JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=4 AND l.role='reading_point' ''',(q,)).fetchone()[0],9)

    def test_juperos_plate_types_are_not_one_linked_id_or_four_kills(self):
        q='irowiki-classic:Juperos_Ruins_History_Quest'
        rows=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.operation,i.quantity,x.game_id FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(rows,[(8,'collect',4,None),(9,'deliver',1,None)])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='distinct_item_types'",(q,)).fetchone()[0],'4')
        example=self.db.execute("SELECT x.game_id FROM requirements r JOIN items x ON x.entity_id=r.entity_id WHERE r.quest_id=? AND r.kind='source_linked_item_example'",(q,)).fetchone()[0]
        self.assertEqual(example,7355)
        mob=self.db.execute('''SELECT m.operation,m.quantity,m.required FROM step_monsters m JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(mob),('loot_source',None,0))
        box=self.db.execute("SELECT x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND r.kind='item_per_turn_in'",(q,)).fetchone()
        self.assertEqual(tuple(box),(666,'1'))

    def test_juperos_exp_report_is_uncertain_and_header_is_not_extra_payout(self):
        q='irowiki-classic:Juperos_Ruins_History_Quest'
        xp=[tuple(r) for r in self.db.execute("SELECT kind,amount FROM rewards WHERE quest_id=? AND item_id IS NULL ORDER BY kind",(q,))]
        self.assertEqual(xp,[('reported_base_exp_per_turn_in','100000'),('source_base_exp_summary','200,000 or more')])
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone())
        limit=self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_exp_reported_uncertainty_after_plates'",(q,)).fetchone()[0]
        self.assertEqual(limit,'2')
        self.assertTrue(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='juperos_exp_and_box_uncertainty'",(q,)).fetchone())

    def test_moving_island_ship_and_gusli_material_sets_are_single_distinct_costs(self):
        q='irowiki-classic:Finding_The_Moving_Island_Quest'
        sets={}
        for n in [3,12]:
            sets[n]=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
              JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=? AND i.operation='deliver' ORDER BY x.game_id''',(q,n))]
        self.assertEqual(sets[3],[(7167,10),(7312,10),(7317,10),(7325,5)])
        self.assertEqual(sets[12],[(7065,10),(7106,20),(7197,20),(7201,30)])
        self.assertFalse(self.db.execute('SELECT 1 FROM step_items JOIN quest_steps USING(step_id) WHERE quest_id=? AND step_order=9',(q,)).fetchone())
        gusli=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND x.game_id=2707''',(q,))]
        self.assertEqual(gusli,[(12,'1')])
        retained=self.db.execute('''SELECT i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=15 AND x.game_id=2707''',(q,)).fetchone()
        self.assertEqual(tuple(retained),('possess',1))

    def test_moving_island_despawn_is_not_completion_and_encounters_are_not_fixed_quotas(self):
        q='irowiki-classic:Finding_The_Moving_Island_Quest'
        flags={r[0]:r[1] for r in self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('despawn_minutes','death_penalty_exempt','monster_exp_disabled','monster_drops_disabled')",(q,))}
        self.assertEqual(flags,{'despawn_minutes':'5','death_penalty_exempt':'1','monster_exp_disabled':'1','monster_drops_disabled':'1'})
        mobs=[tuple(r) for r in self.db.execute('''SELECT m.operation,m.quantity,m.required FROM step_monsters m JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,))]
        self.assertEqual(len(mobs),7);self.assertTrue(all(r==('mention',None,0) for r in mobs))
        action=self.db.execute("SELECT a.condition_text FROM step_actions a JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=4 AND a.operation='kill_all_spawned'",(q,)).fetchone()[0]
        self.assertIn('no fixed winning directions or wait-only completion',action)

    def test_moving_island_three_talks_learning_and_pst_windows_keep_separate_scopes(self):
        q='irowiki-classic:Finding_The_Moving_Island_Quest'
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='npc_talk_count'",(q,)).fetchone()[0],'3')
        windows=[r[0] for r in self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_departure_window' ORDER BY amount",(q,))]
        self.assertEqual(windows,sorted(['12 AM - 3 AM PST','6 AM - 9 AM PST','12 PM - 3 PM PST','6 PM - 9 PM PST']))
        self.assertTrue(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='postquest_time_wording_conflict'",(q,)).fetchone())
        route=self.db.execute("SELECT route_id,kind FROM quest_routes WHERE quest_id=? AND route_key='learn_gusli'",(q,)).fetchone()
        self.assertEqual(route[1],'optional')
        self.assertEqual([r[0] for r in self.db.execute('SELECT s.step_order FROM step_routes r JOIN quest_steps s USING(step_id) WHERE r.route_id=?',(route[0],))],[13])

    def test_moving_island_three_performance_rewards_are_exclusive(self):
        q='irowiki-classic:Finding_The_Moving_Island_Quest'
        rows=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,r.kind,r.amount,x.game_id FROM rewards r JOIN step_branches b USING(branch_id)
          LEFT JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? ORDER BY b.branch_key,r.kind''',(q,))]
        self.assertEqual(rows,[('do_not_play','base_exp','700000',None),('do_not_play','item','1',603),('learned_play','base_exp','1200000',None),('learned_play','item','1',617),('unlearned_play','base_exp','500000',None),('unlearned_play','item','1',603)])
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id=? AND branch_id IS NULL AND kind IN ('base_exp','item')",(q,)).fetchone())

    def test_hellion_exact_inputs_keep_code_alternatives_and_unknown_input_modes(self):
        q='irowiki-classic:Eye_of_Hellion_Quest'
        opts=[tuple(r) for r in self.db.execute('''SELECT s.step_order,o.exact_text,o.operation,o.condition_text FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order,o.option_order''',(q,))]
        codes=[r for r in opts if r[0]==6]
        self.assertEqual([r[1] for r in codes],['3847147298','16754213'])
        self.assertTrue(all(r[2]=='enter_text' and r[3] for r in codes))
        self.assertIn((5,'BINGO','enter_text',None),opts)
        self.assertIn((11,"I won't kill, but I'll find a way to live!",'choose',None),opts)
        self.assertIn((16,'Valaskjalf','answer_unspecified_input',None),opts)
        self.assertFalse(any(r[1]=='He wants me to help you' for r in opts))

    def test_hellion_one_gem_and_one_clue_payment_are_not_double_charged(self):
        q='irowiki-classic:Eye_of_Hellion_Quest'
        gem=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.operation,i.quantity,i.required FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND x.game_id=717 ORDER BY s.step_order''',(q,))]
        self.assertEqual(gem,[(15,'bring',1,1),(16,'deliver',1,1),(18,'use',1,0)])
        costs=[tuple(r) for r in self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('zeny','source_zeny_summary') ORDER BY kind",(q,))]
        self.assertEqual(costs,[('source_zeny_summary','10000'),('zeny','10000')])
        potion=self.db.execute('''SELECT i.required,i.condition_text FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND x.game_id=504''',(q,)).fetchone()
        self.assertEqual(potion[0],0);self.assertIn('wrong choice',potion[1])

    def test_hellion_optional_cave_uses_literal_later_context_and_eye_exception(self):
        q='irowiki-classic:Eye_of_Hellion_Quest'
        route=self.db.execute('''SELECT r.route_id,r.kind,e.method,s.step_order,e.snippet FROM quest_routes r JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=e.step_id WHERE r.quest_id=? AND r.route_key='optional_cave' ''',(q,)).fetchone()
        self.assertEqual(tuple(route[1:4]),('optional','source_optional_activity_context',22));self.assertIn('skip the Cave',route[4])
        self.assertEqual([r[0] for r in self.db.execute('SELECT s.step_order FROM step_routes m JOIN quest_steps s USING(step_id) WHERE m.route_id=? ORDER BY m.route_order',(route[0],))],[19,20])
        boss=self.db.execute('''SELECT m.required,m.quantity,x.game_id FROM step_monsters m JOIN quest_steps s USING(step_id)
          JOIN monsters x ON x.entity_id=m.monster_id WHERE s.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(boss),(0,None,None))
        eye=self.db.execute('''SELECT i.operation,i.required,i.quantity,x.game_id FROM step_items i JOIN step_branches b USING(branch_id)
          JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id=? AND b.branch_key='boss_or_owned_eye' ''',(q,)).fetchone()
        self.assertEqual(tuple(eye),('possess',0,None,7337))

    def test_hellion_tablets_shawn_and_final_rewards_do_not_invent_identity_or_quantity(self):
        q='irowiki-classic:Eye_of_Hellion_Quest'
        tablets=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND x.game_id BETWEEN 7332 AND 7336 ORDER BY s.step_order''',(q,))]
        self.assertEqual(tablets,[(6,7333),(11,7334),(12,7335),(16,7336),(18,7332)])
        npcs=[r[0] for r in self.db.execute('''SELECT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=18''',(q,))]
        self.assertEqual(len(npcs),2);self.assertEqual(len(set(npcs)),2)
        xp=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,r.amount FROM rewards r JOIN step_branches b USING(branch_id) WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY b.branch_key''',(q,))]
        self.assertEqual(xp,[('boss_or_owned_eye','1200000'),('defeated_or_skip','1000000')])
        self.assertTrue(all(r[0] is None for r in self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='item'",(q,))))
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id=? AND kind IN ('base_exp','item') AND branch_id IS NULL",(q,)).fetchone())

    def test_optional_context_route_corruption_fails_without_replacing_export(self):
        original=json.loads((ROOT/'curation/Eye_of_Hellion_Quest.json').read_text())
        cases=[('nonliteral',{'source_evidence':'invented route proof'}),('missing_step',{'source_step':999}),('alternative',{'kind':'alternative','choice_group':'invalid_context_choice'})]
        for name,changes in cases:
            folder=self.out/('invalid-context-route-'+name);shutil.copytree(ROOT/'curation',folder)
            data=json.loads(json.dumps(original));data['routes'][0].update(changes)
            (folder/'Eye_of_Hellion_Quest.json').write_text(json.dumps(data))
            destination=self.out/'quest_semantic.sqlite';before=destination.read_bytes()
            with self.subTest(name=name),self.assertRaisesRegex(ValueError,'Invalid optional route context proof'):build(self.source,self.out,curation_dir=folder)
            self.assertEqual(destination.read_bytes(),before)

    def test_archer_trunk_point_table_restores_a_mixed_threshold_not_four_quotas(self):
        q='irowiki-classic:Archer_Job_Change_Guide'
        values=[tuple(r) for r in self.db.execute('''SELECT i.game_id,r.amount,e.method FROM requirements r JOIN items i ON i.entity_id=r.entity_id
          JOIN evidence e USING(evidence_id) WHERE r.quest_id=? AND r.kind='item_point_value' ORDER BY i.game_id''',(q,))]
        self.assertEqual(values,[(1019,'1','curated_html_text'),(1066,'5','curated_html_text'),(1067,'3','curated_html_text'),(1068,'2','curated_html_text')])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='weighted_item_points_minimum'",(q,)).fetchone()[0],'25')
        contributors=[tuple(r) for r in self.db.execute('''SELECT quantity,required FROM step_items i JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=2''',(q,))]
        self.assertEqual(contributors,[(None,0)]*4)
        kills=[r[0] for r in self.db.execute('''SELECT operation FROM step_monsters m JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY operation''',(q,))]
        self.assertEqual(kills,['loot_source','mention'])

    def test_archer_job_level_and_reward_headers_do_not_invent_counts_or_leveling_gates(self):
        q='irowiki-classic:Archer_Job_Change_Guide'
        levels=[tuple(r) for r in self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('base_level','job_level')",(q,))]
        self.assertEqual(levels,[('job_level','10')])
        rewards=[tuple(r) for r in self.db.execute('''SELECT x.game_id,r.amount,s.step_order FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s USING(step_id) WHERE r.quest_id=? ORDER BY x.game_id''',(q,))]
        self.assertEqual(rewards,[(1702,None,3),(1750,None,3)])
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='job_change'",(q,)).fetchone()[0],'Archer')

    def test_archer_skill_activities_have_separate_routes_levels_and_unlocks(self):
        q='irowiki-classic:Archer_Skill_Quest'
        routes=[tuple(r) for r in self.db.execute('''SELECT r.route_key,r.kind,s.step_order FROM quest_routes r JOIN step_routes m USING(route_id)
          JOIN quest_steps s USING(step_id) WHERE r.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(routes,[('arrow_crafting','optional',1),('arrow_repel','optional',2)])
        gates=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM requirements r JOIN quest_steps s USING(step_id) WHERE r.quest_id=? AND r.kind='job_level' ORDER BY s.step_order''',(q,))]
        self.assertEqual(gates,[(1,'30'),(2,'35')])
        skills=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id) JOIN quest_steps s USING(step_id)
          WHERE r.quest_id=? AND r.kind='skill_unlock' ORDER BY s.step_order''',(q,))]
        self.assertEqual(skills,[(1,'Arrow Crafting'),(2,'Arrow Repel')])
        self.assertEqual(self.db.execute('SELECT count(*) FROM step_source_additions a JOIN quest_steps s USING(step_id) WHERE s.quest_id=?',(q,)).fetchone()[0],4)

    def test_archer_crafting_euro_scales_replace_stems_and_location(self):
        q='irowiki-classic:Archer_Skill_Quest'
        materials=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,x.game_id,i.quantity,b.choice_group FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN step_branches b USING(branch_id) JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id=? ORDER BY b.branch_key''',(q,))]
        self.assertEqual(materials,[('classic_stem',7012,41,'crafting_material_variant'),('euro_scales',906,41,'crafting_material_variant')])
        common=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=1 AND i.branch_id IS NULL ORDER BY x.game_id''',(q,))]
        self.assertEqual(common,[(501,1),(907,20),(921,7),(1019,13)])
        points=[tuple(r) for r in self.db.execute('''SELECT m.map_code,l.x,l.y,sl.condition_text FROM step_locations sl JOIN locations l USING(location_id)
          JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=1 AND sl.role='npc' ORDER BY m.map_code''',(q,))]
        self.assertEqual([(r[0],r[1],r[2]) for r in points],[('moc_ruins',118,99),('morocc',121,109)])
        self.assertTrue(all('alternative' in r[3] for r in points))

    def test_hunter_personality_answers_are_exact_choices_not_world_tasks(self):
        q='irowiki-classic:Hunter_Job_Change_Guide'
        choices=[tuple(r) for r in self.db.execute('''SELECT s.step_order,o.exact_text,o.operation,d.original_text FROM dialogue_options o JOIN dialogues d USING(dialogue_id)
          JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order''',(q,))]
        expected=['Wander around alone and search for a place.','Walk with a friend.','Hunt at a nearby field.','Go back to town.','Would it be possible to get a heal, please?','Open a chat room and wait.','Tell the person about a good place to hunt.','Guide them to their destination.','Watch, then attack when asked for help.','Try to find the owner.']
        self.assertEqual(choices,[(n,text,'choose',None) for n,text in enumerate(expected,3)])
        self.assertFalse(self.db.execute('''SELECT 1 FROM step_monsters m JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order BETWEEN 3 AND 12''',(q,)).fetchone())

    def test_hunter_random_table_is_seven_exclusive_three_item_sets(self):
        q='irowiki-classic:Hunter_Job_Change_Guide'
        actual={}
        for row in self.db.execute('''SELECT b.branch_key,x.game_id,i.quantity,e.method,b.choice_group FROM step_items i JOIN step_branches b USING(branch_id)
          JOIN items x ON x.entity_id=i.item_id JOIN evidence e ON e.evidence_id=i.evidence_id JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id=? AND s.step_order=13 ORDER BY b.branch_key,x.game_id''',(q,)):
            actual.setdefault(row[0],[]).append((row[1],row[2]));self.assertEqual(row[3],'curated_html_text');self.assertEqual(row[4],'random_arrow_material_set')
        expected=[[(7030,5),(1019,5),(509,3)],[(955,9),(935,9),(508,9)],[(1021,3),(7032,3),(914,10)],[(937,3),(507,5),(919,3)],[(925,3),(932,5),(511,3)],[(913,3),(938,1),(948,1)],[(1027,2),(942,1),(1026,1)]]
        self.assertEqual(actual,{'material_set_'+str(i):sorted(v) for i,v in enumerate(expected,1)})
        self.assertFalse(self.db.execute('''SELECT 1 FROM step_items i JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=13 AND i.branch_id IS NULL''',(q,)).fetchone())

    def test_hunter_four_kills_do_not_become_six_population_kills_or_regular_ids(self):
        q='irowiki-classic:Hunter_Job_Change_Guide'
        kills=[tuple(r) for r in self.db.execute('''SELECT s.step_order,m.quantity,x.game_id,e.original_name FROM step_monsters m JOIN quest_steps s USING(step_id)
          JOIN monsters x ON x.entity_id=m.monster_id JOIN entities e ON e.entity_id=m.monster_id WHERE s.quest_id=? AND m.operation='kill' ''',(q,))]
        self.assertEqual(kills,[(16,4,None,'Job Change Monster')])
        counts=[tuple(r) for r in self.db.execute('''SELECT CAST(r.amount AS INTEGER),x.game_id FROM requirements r JOIN monsters x ON x.entity_id=r.entity_id
          WHERE r.quest_id=? AND r.kind='monster_population' ORDER BY CAST(r.amount AS INTEGER)''',(q,))]
        self.assertEqual(counts,[(1,None),(1,None),(1,None),(3,None)])
        species={r[0] for r in self.db.execute('''SELECT x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=15''',(q,))}
        self.assertEqual(species,{1002,1015,1016,1041})

    def test_hunter_one_shared_timer_and_preparation_relocated_before_objectives(self):
        q='irowiki-classic:Hunter_Job_Change_Guide'
        timers=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM requirements r JOIN quest_steps s USING(step_id) WHERE r.quest_id=? AND r.kind='time_limit_minutes' ''',(q,))]
        self.assertEqual(timers,[(15,'3')])
        prep=[tuple(r) for r in self.db.execute('''SELECT s.step_order,support.step_order FROM step_actions a JOIN quest_steps s USING(step_id)
          JOIN evidence_context c ON c.evidence_id=a.evidence_id JOIN evidence e ON e.evidence_id=c.supporting_id JOIN quest_steps support ON support.step_id=e.step_id
          WHERE s.quest_id=? AND a.operation='join_waiting_chat' ''',(q,))]
        self.assertEqual(prep,[(15,18)])
        failures=[r[0] for r in self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='test_failure_condition'",(q,))]
        self.assertEqual(set(failures),{'kill_unlabelled_target','enter_water_puddle','exceed_shared_time_limit','player_death'})

    def test_hunter_silver_arrows_are_reward_and_necklace_is_one_returned_proof(self):
        q='irowiki-classic:Hunter_Job_Change_Guide'
        granted=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s USING(step_id) WHERE r.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(granted,[(15,1751,'200'),(19,1007,'1')])
        consumed=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND i.branch_id IS NULL''',(q,))]
        self.assertEqual(consumed,[(20,1007,1)])
        self.assertEqual(tuple(self.db.execute("SELECT amount,branch_id FROM rewards WHERE quest_id=? AND kind='job_change'",(q,)).fetchone()),('Hunter',None))

    def test_hunter_falcon_tip_and_receptionist_identities_keep_their_scope(self):
        q='irowiki-classic:Hunter_Job_Change_Guide'
        people=[r[0] for r in self.db.execute('''SELECT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order IN (13,15)''',(q,))]
        demon=self.db.execute("SELECT npc_id FROM step_npcs n JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=13",(q,)).fetchone()[0]
        receptionist=self.db.execute("SELECT entity_id FROM entities WHERE original_name='receptionist' AND entity_id IN (SELECT npc_id FROM step_npcs n JOIN quest_steps s USING(step_id) WHERE s.quest_id=?)",(q,)).fetchone()[0]
        self.assertIn(demon,people);self.assertIn(receptionist,people);self.assertNotEqual(demon,receptionist)
        branch=self.db.execute('''SELECT branch_id,condition_text FROM step_branches b JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND branch_key='optional_falcon_rental' ''',(q,)).fetchone()
        self.assertIn('Optional',branch[1])
        self.assertEqual([r[0] for r in self.db.execute('SELECT operation FROM step_actions WHERE branch_id=? ORDER BY action_order',(branch[0],))],['gain_experience_for_falconry_mastery','learn_falconry_mastery','rent_falcon'])

    def test_hunter_skill_request_and_return_share_one_set_and_only_arpesto_has_point(self):
        q='irowiki-classic:Hunter_Skill_Quest'
        facts=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order,x.game_id''',(q,))]
        self.assertEqual(facts,[(n,game_id,amount,op) for n,op in [(1,'bring'),(2,'deliver')] for game_id,amount in [(537,30),(724,5),(7115,5)]])
        points=[tuple(r) for r in self.db.execute('''SELECT l.x,l.y,sl.role FROM step_locations sl JOIN locations l USING(location_id) JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND sl.role='Arpesto' ''',(q,))]
        self.assertEqual(points,[(109,169,'Arpesto')])
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='skill_unlock'",(q,)).fetchone()[0],'Phantasmic Arrow')

    def test_restored_hunter_material_proof_corruption_preserves_valid_export(self):
        folder=self.out/'invalid-hunter-table-proof';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Hunter_Job_Change_Guide.json';data=json.loads(path.read_text())
        data['steps']['13']['branches'][0]['items'][0]['evidence']='1 999 Claw of Desert Wolf'
        path.write_text(json.dumps(data));before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Nonliteral curated section evidence'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual((self.out/'quest_semantic.sqlite').read_bytes(),before)

    def test_geffen_bard_route_choices_and_discovery_shortcut_are_separate(self):
        q='irowiki-classic:Geffen_Bard_Quest'
        actual={}
        for r in self.db.execute('SELECT route_id,route_key,kind,choice_group,e.method FROM quest_routes JOIN evidence e USING(evidence_id) WHERE quest_routes.quest_id=?',(q,)):
            members=[x[0] for x in self.db.execute('SELECT s.step_order FROM step_routes m JOIN quest_steps s USING(step_id) WHERE m.route_id=? ORDER BY m.route_order',(r[0],))]
            actual[r[1]]=(r[2],r[3],r[4],members)
        self.assertEqual(actual,{'learn_author':('optional',None,'source_optional_activity_context',[4,5]),'read_book':('alternative','lyrics_solution','source_alternative_activity_context',[10,11]),'kino_letter':('alternative','lyrics_solution','source_alternative_activity_context',[12,13])})

    def test_geffen_bard_exact_inputs_do_not_turn_book_lyric_into_an_answer(self):
        opts=[tuple(r) for r in self.db.execute('''SELECT s.step_order,o.exact_text,o.operation FROM dialogue_options o JOIN step_dialogues sd USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id='irowiki-classic:Geffen_Bard_Quest' ORDER BY s.step_order,o.option_order''')]
        self.assertEqual(opts,[(1,'Sure, no problem.','choose'),(2,'At One, I Fall in Love','enter_text'),(2,'Minty Errende','enter_text'),(3,'Kino Kitty','answer_unspecified_input'),(5,'Kino Kitty','enter_text'),(5,'I can, so stop crying.','choose'),(7,'Sorry for interrupting,','choose'),(7,"I'm looking for Kino Kitty.",'choose')])
        self.assertFalse(any("At Eight" in r[1] for r in opts))

    def test_geffen_bard_exp_bands_belong_only_to_letter_route(self):
        rows=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount,b.choice_group FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) LEFT JOIN step_branches b ON b.branch_id=r.branch_id
          WHERE r.quest_id='irowiki-classic:Geffen_Bard_Quest' AND r.kind='base_exp' ORDER BY s.step_order,CAST(r.amount AS INTEGER)''')]
        self.assertEqual(rows,[(11,None,None)]+[(13,str(n),'letter_level_reward') for n in [4500,5500,9684,13411,35757,60246,70340,92052,156786]])
        magnifier=self.db.execute('''SELECT i.quantity,i.operation,i.required FROM step_items i JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Geffen_Bard_Quest' AND x.game_id=611''').fetchall()
        self.assertEqual([tuple(r) for r in magnifier],[(None,'mention',0)])

    def test_bard_job_flower_is_one_of_eight_not_eight_mandatory_costs(self):
        flowers=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity,i.operation,b.choice_group FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN step_branches b USING(branch_id) JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id='irowiki-classic:Bard_Job_Change_Guide' AND s.step_order=1 ORDER BY x.game_id''')]
        self.assertEqual(flowers,[(n,1,'bring','flower_choice') for n in sorted([703,708,704,710,748,629,709,749])])
        shared=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Bard_Job_Change_Guide' AND i.branch_id IS NULL''')]
        self.assertEqual(shared,[(None,1,'deliver')])

    def test_bard_job_optional_trunks_and_harp_keep_exact_item_identity(self):
        offers=[tuple(r) for r in self.db.execute('''SELECT material.game_id,i.quantity,instrument.game_id,r.amount FROM step_items i JOIN step_branches b USING(branch_id)
          JOIN items material ON material.entity_id=i.item_id JOIN rewards r ON r.branch_id=b.branch_id JOIN items instrument ON instrument.entity_id=r.item_id
          JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id='irowiki-classic:Bard_Job_Change_Guide' AND b.choice_group='optional_instrument' ORDER BY b.branch_key''')]
        self.assertEqual(offers,[(1019,60,1901,'1'),(1067,60,1903,'1'),(1068,60,1903,'1'),(1066,60,1905,'1'),(1066,60,1910,'1')])
        job=self.db.execute("SELECT amount,branch_id FROM rewards WHERE quest_id='irowiki-classic:Bard_Job_Change_Guide' AND kind='job_change'").fetchone()
        self.assertEqual(tuple(job),('Bard',None))
        self.assertFalse(self.db.execute("SELECT 1 FROM dialogue_options o JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id) WHERE s.quest_id='irowiki-classic:Bard_Job_Change_Guide'").fetchone())

    def test_bard_job_snowysnow_is_corequisite_with_literal_header_proof(self):
        rows=[tuple(r) for r in self.db.execute('''SELECT r.related_id,r.kind,e.snippet,e.method FROM quest_relations r JOIN evidence e USING(evidence_id)
          WHERE r.quest_id='irowiki-classic:Bard_Job_Change_Guide' AND r.kind='source_corequisite' ''')]
        self.assertEqual(rows,[('irowiki-classic:Snowysnow_Quest','source_corequisite','Quest Corequisite(s): Snowysnow Quest','curated_single_corequisite')])
        with tempfile.TemporaryDirectory() as tmp:
            copy=sqlite3.connect(Path(tmp)/'corequisite.sqlite');self.db.backup(copy)
            copy.execute("UPDATE quest_relations SET related_id='irowiki-classic:Geffen_Bard_Quest' WHERE kind='source_corequisite'")
            with self.assertRaisesRegex(ValueError,'invalid_source_corequisite'):validate(copy)
            copy.close()

    def test_pang_voice_gate_is_at_riott_not_the_earlier_drink_stages(self):
        row=self.db.execute('''SELECT r.related_id,e.method,s.step_order FROM quest_relations r JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=e.step_id WHERE r.quest_id='irowiki-classic:Bard_Skill_Quest' AND r.kind='completion_required_at_step' ''').fetchone()
        self.assertEqual(tuple(row),('irowiki-classic:Geffen_Bard_Quest','curated_quest_relation',3))
        positions={r[0] for r in self.db.execute("SELECT amount FROM requirements WHERE quest_id='irowiki-classic:Bard_Skill_Quest' AND kind='dialogue_option_position'")}
        self.assertEqual(positions,{'2','1'})
        ordered=[r[0] for r in self.db.execute("SELECT condition_text FROM step_actions a JOIN quest_steps s USING(step_id) WHERE s.quest_id='irowiki-classic:Bard_Skill_Quest' AND s.step_order=1 ORDER BY a.action_order")]
        self.assertTrue(ordered[0].startswith('second option'));self.assertTrue(ordered[1].startswith('first option'))
        self.assertFalse(self.db.execute("SELECT 1 FROM dialogue_options o JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id) WHERE s.quest_id='irowiki-classic:Bard_Skill_Quest'").fetchone())

    def test_pang_voice_chicken_cycle_is_collection_without_invented_ids_or_kills(self):
        points=[tuple(r) for r in self.db.execute('''SELECT l.x,l.y,l.map_id FROM step_locations sl JOIN locations l USING(location_id) JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Bard_Skill_Quest' AND s.step_order=5 ORDER BY sl.location_order''')]
        self.assertEqual([(r[0],r[1]) for r in points],[(239,68),(186,204),(83,204),(147,81),(211,143)])
        self.assertTrue(all(self.db.execute('SELECT map_code FROM maps WHERE entity_id=?',(r[2],)).fetchone()[0] is None for r in points))
        self.assertFalse(self.db.execute("SELECT 1 FROM step_monsters m JOIN quest_steps s USING(step_id) WHERE s.quest_id='irowiki-classic:Bard_Skill_Quest'").fetchone())
        eggs=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.quantity,i.operation,x.game_id FROM step_items i JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id='irowiki-classic:Bard_Skill_Quest' AND s.step_order>=4 ORDER BY s.step_order''')]
        self.assertEqual(eggs,[(4,5,'collect',None),(5,5,'collect',None)])
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id='irowiki-classic:Bard_Skill_Quest' AND kind='skill_unlock'").fetchone()[0],'Pang Voice')

    def test_alternative_route_context_proof_corruption_fails_atomically(self):
        original=json.loads((ROOT/'curation/Geffen_Bard_Quest.json').read_text())
        cases=[('nonliteral',{'choice_evidence':'invented choice'}),('missing_step',{'source_step':999}),('optional',{'kind':'optional','choice_group':None}),('conflict',{'source_evidence':'book'})]
        for name,changes in cases:
            folder=self.out/('invalid-alternative-proof-'+name);shutil.copytree(ROOT/'curation',folder)
            data=json.loads(json.dumps(original));data['routes'][1].update(changes)
            (folder/'Geffen_Bard_Quest.json').write_text(json.dumps(data))
            before=(self.out/'quest_semantic.sqlite').read_bytes()
            with self.subTest(name=name),self.assertRaisesRegex(ValueError,'Invalid alternative route context proof|Conflicting route context proofs'):build(self.source,self.out,curation_dir=folder)
            self.assertEqual((self.out/'quest_semantic.sqlite').read_bytes(),before)

    def test_corequisite_header_cannot_be_replaced_with_another_quest(self):
        folder=self.out/'invalid-corequisite-header';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Bard_Job_Change_Guide.json';data=json.loads(path.read_text())
        data['relations'][0]['related_quest_id']='irowiki-classic:Geffen_Bard_Quest';path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Header does not establish a single corequisite'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual((self.out/'quest_semantic.sqlite').read_bytes(),before)

    def test_snowysnow_bard_house_is_conditional_and_only_first_answer_is_literal(self):
        q='irowiki-classic:Snowysnow_Quest'
        b=self.db.execute('''SELECT b.branch_id,b.condition_text,s.step_order FROM step_branches b JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND b.branch_key='bard_house_trigger' ''',(q,)).fetchone()
        self.assertEqual(b[2],1);self.assertIn('Bard Job Change',b[1])
        actions=[r[0] for r in self.db.execute('SELECT operation FROM step_actions WHERE branch_id=? ORDER BY action_order',(b[0],))]
        self.assertEqual(actions,['talk','talk'])
        options=[tuple(r) for r in self.db.execute('''SELECT s.step_order,o.exact_text FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(options,[(1,'Snowysnow?')])
        first=self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='dialogue_option_position'",(q,)).fetchone()[0]
        self.assertEqual(first,'1')
        self.assertTrue(self.db.execute('SELECT 1 FROM quests WHERE quest_id=?',(q,)).fetchone())

    def test_snowysnow_material_pair_and_salt_do_not_create_extra_consumption(self):
        q='irowiki-classic:Snowysnow_Quest'
        materials=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? ORDER BY s.step_order,x.game_id''',(q,))]
        self.assertEqual(materials,[(3,938,'bring',1),(3,1024,'bring',1),(4,938,'deliver',1),(4,1024,'deliver',1)])
        salt=self.db.execute('''SELECT s.step_order,r.kind,r.amount,x.game_id FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN items x ON x.entity_id=r.item_id JOIN entities e ON e.entity_id=r.item_id
          WHERE r.quest_id=? AND e.original_name='Roughest Salt' ''',(q,)).fetchone()
        self.assertEqual(tuple(salt),(7,'quest_object_reference',None,None))
        self.assertFalse(self.db.execute('''SELECT 1 FROM step_items i JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=i.item_id
          WHERE s.quest_id=? AND e.original_name='Roughest Salt' ''',(q,)).fetchone())

    def test_snowysnow_children_order_and_possible_reward_ranges_keep_unknowns(self):
        q='irowiki-classic:Snowysnow_Quest'
        order=[r[0] for r in self.db.execute('''SELECT e.original_name FROM step_actions a JOIN entities e USING(entity_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=10 ORDER BY a.action_order''',(q,))]
        self.assertEqual(order,['little boy','girl'])
        identities={r[0] for r in self.db.execute('''SELECT npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=10''',(q,))}
        named={r[0] for r in self.db.execute('''SELECT entity_id FROM entities WHERE original_name IN ('Charu Charu','Marcell')''')}
        self.assertTrue(identities.isdisjoint(named))
        presents=[tuple(r) for r in self.db.execute('''SELECT e.original_name,r.kind,r.amount,x.game_id FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN entities e ON e.entity_id=r.item_id WHERE r.quest_id=? AND r.kind='possible_item' ORDER BY e.original_name''',(q,))]
        self.assertEqual(presents,[('Candy','possible_item','5-10',None),('Candy Cane','possible_item','5-10',None),('Piece of Cake','possible_item','1-2',None),('Well-baked Cookie','possible_item','5-10',None)])
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id=? AND kind='item'",(q,)).fetchone())

    def test_piano_exclusive_source_routes_each_produce_six_keys(self):
        q='irowiki-classic:Piano_Keys_Quest'
        routes=[tuple(r) for r in self.db.execute('SELECT route_id,route_key,choice_group FROM quest_routes WHERE quest_id=? ORDER BY route_key',(q,))]
        self.assertEqual([(r[1],r[2]) for r in routes],[('part_2a','crayu_prompt'),('part_2b','crayu_prompt')])
        for route,steps in zip(routes,[[3,4,5,6],[7,8,9,10]]):
            actual=[r[0] for r in self.db.execute('SELECT s.step_order FROM step_routes m JOIN quest_steps s USING(step_id) WHERE m.route_id=? ORDER BY m.route_order',(route[0],))]
            self.assertEqual(actual,steps)
            amount=self.db.execute('''SELECT sum(CAST(r.amount AS INTEGER)) FROM rewards r JOIN step_rewards sr USING(reward_id)
              JOIN step_routes m ON m.step_id=sr.step_id JOIN items x ON x.entity_id=r.item_id WHERE m.route_id=? AND x.game_id=7184''',(route[0],)).fetchone()[0]
            self.assertEqual(amount,5)
        shared=self.db.execute('''SELECT r.amount FROM rewards r JOIN step_rewards sr USING(reward_id) JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND s.step_order=1 AND x.game_id=7184''',(q,)).fetchone()[0]
        self.assertEqual(shared,'1')
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='item_consumed_total'",(q,)).fetchone()[0],'6')

    def test_piano_tombstone_triggers_and_duplicate_original_steps_are_distinct(self):
        q='irowiki-classic:Piano_Keys_Quest'
        actors={r[0]:r[1] for r in self.db.execute('''SELECT s.step_order,n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (5,6,9,10)''',(q,))}
        self.assertEqual(actors[5],actors[10]);self.assertEqual(actors[6],actors[9]);self.assertNotEqual(actors[5],actors[6])
        ops=[tuple(r) for r in self.db.execute('''SELECT s.step_order,a.operation FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (5,6,9,10) ORDER BY s.step_order''',(q,))]
        self.assertEqual(ops,[(5,'walk_past_twice'),(6,'walk_past'),(9,'walk_past'),(10,'walk_past_twice')])
        duplicate=[tuple(r) for r in self.db.execute('SELECT step_id,original_instruction FROM quest_steps WHERE quest_id=? AND step_order IN (4,7) ORDER BY step_order',(q,))]
        self.assertEqual(duplicate[0][1],duplicate[1][1]);self.assertNotEqual(duplicate[0][0],duplicate[1][0])
        self.assertTrue(duplicate[0][0].endswith('/1'));self.assertTrue(duplicate[1][0].endswith('/2'))

    def test_piano_exact_lyrics_are_prompt_conditioned_not_unconditional_inputs(self):
        q='irowiki-classic:Piano_Keys_Quest'
        lines=['Mountain sunset to the west','Where the purple dusk falls','Surrounded by beautiful melody','You become the key that ignores its master']
        for n in [3,8]:
            options=[tuple(r) for r in self.db.execute('''SELECT o.exact_text,o.operation,o.condition_text FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
              JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=? ORDER BY o.option_order''',(q,n))]
            self.assertEqual([(r[0],r[1]) for r in options],[(line,'enter_text') for line in lines])
            self.assertTrue(all('when prompted' in r[2] for r in options))
        self.assertFalse(self.db.execute('''SELECT 1 FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND m.surface='key' AND m.entity_type='item' ''',(q,)).fetchone())
        self.assertTrue(self.db.execute("SELECT 1 FROM step_actions a JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=13 AND a.condition_text LIKE 'Each conversation%'",(q,)).fetchone())

    def test_message_rose_quantity_unknown_and_leaves_have_separate_input_output_roles(self):
        q='irowiki-classic:Message_Delivery_Quest'
        costs=[tuple(r) for r in self.db.execute('''SELECT x.game_id,r.amount FROM requirements r JOIN items x ON x.entity_id=r.entity_id
          WHERE r.quest_id=? AND r.kind='item_consumed_total' ORDER BY x.game_id''',(q,))]
        self.assertEqual(costs,[(610,'10'),(748,None)])
        inputs=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=3 ORDER BY x.game_id''',(q,))]
        self.assertEqual(inputs,[(610,'bring',10),(748,'possess',None)])
        self.assertFalse(self.db.execute('SELECT 1 FROM step_items JOIN quest_steps USING(step_id) WHERE quest_id=? AND step_order=5',(q,)).fetchone())

    def test_message_success_failure_rewards_are_not_summed_or_answer_selected(self):
        q='irowiki-classic:Message_Delivery_Quest'
        rows=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,x.game_id,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? ORDER BY b.branch_key,x.game_id''',(q,))]
        self.assertEqual(rows,[('summon_failure',610,'10'),('summon_success',607,'2'),('summon_success',608,'3'),('summon_success',610,'10')])
        self.assertFalse(self.db.execute('SELECT 1 FROM rewards WHERE quest_id=? AND branch_id IS NULL',(q,)).fetchone())
        self.assertTrue(self.db.execute("SELECT 1 FROM step_actions a JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=5 AND a.operation='choose_either_response'",(q,)).fetchone())

    def test_message_return_actor_wrong_answer_and_failure_hp_keep_source_scope(self):
        q='irowiki-classic:Message_Delivery_Quest'
        actors=[r[0] for r in self.db.execute("SELECT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order IN (1,5) AND n.role='talk' ORDER BY s.step_order",(q,))]
        self.assertEqual(len(actors),2);self.assertEqual(actors[0],actors[1])
        options=[tuple(r) for r in self.db.execute('''SELECT o.exact_text,o.correct FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,))]
        self.assertEqual(options,[('Sorry',0)])
        hp=self.db.execute("SELECT amount,condition_text FROM requirements WHERE quest_id=? AND kind='source_hp_loss'",(q,)).fetchone()
        self.assertIsNone(hp[0]);self.assertIn('Only on failed summoning',hp[1])

    def test_new_world_sidequest_prerequisites_preserve_started_access(self):
        for slug in ['Laphine_Craftsman','Collecting_Draco_Eggs']:
            q='irowiki-classic:'+slug
            rows=[tuple(r) for r in self.db.execute("SELECT related_id,kind FROM quest_relations WHERE quest_id=? AND kind LIKE 'required_%'",(q,))]
            self.assertEqual(rows,[('irowiki-classic:Two_Tribes','required_started_quest')])
            evidence=self.db.execute("SELECT e.snippet FROM quest_relations r JOIN evidence e USING(evidence_id) WHERE r.quest_id=? AND r.kind='required_started_quest'",(q,)).fetchone()[0]
            self.assertIn('Yggdrasil Dungeon',evidence)
        for slug,target in [('Tribe_Investigation','Ring_of_the_Wise_King'),('Bradium_Collection','Ring_of_the_Wise_King'),("Manuk's_Monster_Suppression",'Two_Tribes')]:
            self.assertTrue(self.db.execute("SELECT 1 FROM quest_relations WHERE quest_id=? AND related_id=? AND kind='mandatory_prerequisite'",('irowiki-classic:'+slug,'irowiki-classic:'+target)).fetchone())

    def test_new_world_repeatability_is_only_for_three_source_notices(self):
        group=['Tribe_Investigation','Laphine_Craftsman','Bradium_Collection','Collecting_Draco_Eggs',"Manuk's_Monster_Suppression"]
        for slug in group:
            q='irowiki-classic:'+slug
            rows=[tuple(r) for r in self.db.execute("SELECT r.kind,r.amount,e.method,e.snippet FROM requirements r JOIN evidence e USING(evidence_id) WHERE r.quest_id=? AND r.kind IN ('quest_repeatable','quest_cooldown_hours') ORDER BY r.kind",(q,))]
            if slug in group[1:4]:
                self.assertEqual([(r[0],r[1]) for r in rows],[('quest_cooldown_hours','24'),('quest_repeatable','1')])
                self.assertTrue(all(r[2]=='curated_html_text' for r in rows))
                self.assertIn('24 Hour cooldown',rows[0][3])
            else:self.assertEqual(rows,[])

    def test_tribe_investigation_has_one_horn_set_and_one_coin(self):
        q='irowiki-classic:Tribe_Investigation'
        items=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND x.game_id=6032 ORDER BY s.step_order''',(q,))]
        self.assertEqual(items,[(3,'bring',30),(4,'deliver',30)])
        grants=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.kind,r.amount,x.game_id FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) LEFT JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=?''',(q,))]
        self.assertEqual(grants,[(5,'item','1',6080)])
        self.assertFalse(self.db.execute('SELECT 1 FROM step_monsters JOIN quest_steps USING(step_id) WHERE quest_id=?',(q,)).fetchone())
        npc_ids=[r[0] for r in self.db.execute('''SELECT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order IN (1,5) ORDER BY s.step_order''',(q,))]
        self.assertEqual(npc_ids[0],npc_ids[1])

    def test_laphine_bound_flag_is_not_quantity_and_entrance_precedes_npc(self):
        q='irowiki-classic:Laphine_Craftsman'
        delivered=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=2 ORDER BY x.game_id''',(q,))]
        self.assertEqual(delivered,[(6075,15),(7326,15)])
        bound=self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='item_character_bound'",(q,)).fetchone()[0]
        self.assertEqual(bound,'1')
        points=[tuple(r) for r in self.db.execute('''SELECT m.map_code,l.x,l.y,sl.role,sl.location_order FROM step_locations sl JOIN locations l USING(location_id)
          JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=1 AND sl.role IN ('entrance','npc') ORDER BY sl.location_order''',(q,))]
        self.assertEqual(points,[('splendide',198,238,'entrance',1),('spl_in',97,313,'npc',2)])

    def test_bradium_conflicting_materials_and_pinned_shared_coin(self):
        q='irowiki-classic:Bradium_Collection'
        rows=[tuple(r) for r in self.db.execute('''SELECT s.step_order,e.original_name,x.game_id,i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(rows,[(1,'Bradium Fragments',None,'bring',20),(2,'Refined Bradium',6090,'deliver',20)])
        self.assertTrue(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='bradium_material_name_conflict'",(q,)).fetchone())
        loot=self.db.execute('''SELECT m.operation,m.quantity,m.required,x.game_id FROM step_monsters m JOIN quest_steps s USING(step_id)
          JOIN monsters x ON x.entity_id=m.monster_id WHERE s.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(loot),('loot_source',None,0,2024))
        donor=self.db.execute('''SELECT src.url,e.snippet FROM entity_evidence ee JOIN evidence e USING(evidence_id) JOIN sources src USING(source_id)
          JOIN items x ON x.entity_id=ee.entity_id WHERE e.quest_id=? AND x.game_id=6080 AND e.method='wiki_database_id_link_pinned_cross_guide' ''',(q,)).fetchone()
        self.assertEqual(tuple(donor),('https://irowiki.org/classic/Tribe_Investigation','http://db.irowiki.org/classic/item-info/6080/'))
        self.assertEqual(self.db.execute("SELECT count(*) FROM entities WHERE entity_type='item' AND original_name='Manuk Coin'").fetchone()[0],1)
        self.assertEqual(self.db.execute("SELECT count(*) FROM entities WHERE entity_type='item' AND original_name='Splendide Coin'").fetchone()[0],1)
        resolved=[tuple(r) for r in self.db.execute('''SELECT x.game_id,src.url FROM entity_evidence ee JOIN items x ON x.entity_id=ee.entity_id
          JOIN evidence e USING(evidence_id) JOIN sources src USING(source_id) WHERE e.quest_id='irowiki-classic:Two_Tribes'
          AND e.method='wiki_database_id_link_pinned_cross_guide' ORDER BY x.game_id''')]
        self.assertEqual(resolved,[(6080,'https://irowiki.org/classic/Tribe_Investigation'),(6081,'https://irowiki.org/classic/Laphine_Craftsman')])

    def test_draco_eggs_have_one_total_across_two_floors_and_no_kills(self):
        q='irowiki-classic:Collecting_Draco_Eggs'
        rows=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.operation,i.quantity,x.game_id FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(rows,[(1,'bring',10,6093),(2,'collect',None,6093),(3,'deliver',10,6093)])
        self.assertEqual([r[0] for r in self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_collection_floor' ORDER BY amount",(q,))],['1','2'])
        self.assertFalse(self.db.execute('SELECT 1 FROM step_monsters JOIN quest_steps USING(step_id) WHERE quest_id=?',(q,)).fetchone())
        coins=[tuple(r) for r in self.db.execute('SELECT x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? ORDER BY x.game_id',(q,))]
        self.assertEqual(coins,[(6080,'1'),(6081,'1')])

    def test_manuk_suppression_kill_counts_do_not_use_coin_quantity(self):
        q="irowiki-classic:Manuk's_Monster_Suppression"
        mobs=[tuple(r) for r in self.db.execute('''SELECT x.game_id,m.operation,m.quantity,m.required FROM step_monsters m JOIN quest_steps s USING(step_id)
          JOIN monsters x ON x.entity_id=m.monster_id WHERE s.quest_id=? ORDER BY x.game_id''',(q,))]
        self.assertEqual(mobs,[(2017,'kill',1,1),(2018,'kill',1,1)])
        grants=[tuple(r) for r in self.db.execute('SELECT kind,amount FROM rewards WHERE quest_id=? ORDER BY kind',(q,))]
        self.assertEqual(grants,[('base_exp','500000'),('item','10'),('job_exp','300000')])
        for slug,base,job in [('Laphine_Craftsman','30000','30000'),('Bradium_Collection','40000','40000'),('Collecting_Draco_Eggs','40000','40000')]:
            xp=[tuple(r) for r in self.db.execute("SELECT kind,amount FROM rewards WHERE quest_id=? AND kind IN ('base_exp','job_exp') ORDER BY kind",('irowiki-classic:'+slug,))]
            self.assertEqual(xp,[('base_exp',base),('job_exp',job)])

    def test_cross_guide_stale_pin_fails_without_replacing_database(self):
        folder=self.out/'invalid-cross-guide-pin';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Bradium_Collection.json';data=json.loads(path.read_text())
        next(e for e in data['entities'] if e['name']=='Manuk Coin')['id_evidence_snapshot']['sha256']='stale'
        path.write_text(json.dumps(data))
        destination=self.out/'quest_semantic.sqlite';before=destination.read_bytes()
        with self.assertRaisesRegex(ValueError,'ID evidence snapshot changed or missing'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(destination.read_bytes(),before)

    def test_rogue_agents_are_distinct_and_return_to_same_guild_actor(self):
        q='irowiki-classic:Rogue_Guild_Investigation_Quest'
        def actor(n,role='talk'):
            return self.db.execute('SELECT npc_id FROM step_npcs JOIN quest_steps USING(step_id) WHERE quest_id=? AND step_order=? AND role=?',(q,n,role)).fetchone()[0]
        self.assertEqual(actor(2),actor(17))
        self.assertEqual(len({actor(n) for n in [2,4,5,7,14,16]}),6)
        for n,full_target,short_target in [(6,7,5),(13,14,7),(15,16,14)]:
            for field,target in [('full_en',full_target),('short_en',short_target),('full_ru',full_target),('short_ru',short_target)]:
                ids=[r[0] for r in self.db.execute('''SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
                  WHERE s.quest_id=? AND s.step_order=? AND m.field=? AND m.surface='agent' ''',(q,n,field))]
                self.assertEqual(ids,[actor(target)],(n,field))

    def test_rogue_mask_is_retained_and_alcohol_is_two_installments(self):
        q='irowiki-classic:Rogue_Guild_Investigation_Quest'
        alcohol=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND x.game_id=970 AND i.operation='deliver' ORDER BY s.step_order''',(q,))]
        self.assertEqual(alcohol,[(10,1),(12,1)])
        mask=[tuple(r) for r in self.db.execute('''SELECT i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND e.original_name='Mask' ''',(q,))]
        self.assertEqual(mask,[('show',1)])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_requested_item_count'",(q,)).fetchone()[0],'2')
        red=self.db.execute('''SELECT i.operation,i.required FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND e.original_name='Red Herbs' ''',(q,)).fetchone()
        self.assertEqual(tuple(red),('decline',0))
        delivered=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=6 ORDER BY x.game_id''',(q,))]
        self.assertEqual(delivered,[(944,1),(1038,1),(1059,1),(1060,1),(1062,1)])

    def test_rogue_prerequisite_notes_and_food_level_are_separate(self):
        q='irowiki-classic:Rogue_Guild_Investigation_Quest'
        self.assertTrue(self.db.execute("SELECT 1 FROM quest_relations WHERE quest_id=? AND related_id='irowiki-classic:Z-Gang_Quest' AND kind='mandatory_prerequisite'",(q,)).fetchone())
        notes=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.item_id,x.game_id,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND r.kind='quest_document' ORDER BY s.step_order''',(q,))]
        self.assertEqual([r[0] for r in notes],[4,13]);self.assertNotEqual(notes[0][1],notes[1][1])
        self.assertTrue(all(r[2:] == (None,None) for r in notes))
        grants=[tuple(r) for r in self.db.execute('''SELECT s.step_order,e.original_name,r.amount,x.game_id FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=r.item_id JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND r.kind='item' ORDER BY s.step_order''',(q,))]
        self.assertEqual(grants,[(15,'Level 4 Food Item','1',None),(17,'Gift Box','1',644)])

    def test_crow_search_inputs_are_alternatives_and_scenes_are_not_kills(self):
        q='irowiki-classic:Crow_of_Destiny_Quest'
        opts=[tuple(r) for r in self.db.execute('''SELECT o.exact_text,o.operation,o.condition_text FROM dialogue_options o JOIN dialogues d USING(dialogue_id)
          JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=2 ORDER BY o.option_order''',(q,))]
        self.assertEqual([(r[0],r[1]) for r in opts],[('The Crow of the Fate','enter_text'),('Oliver Hilpert','enter_text')])
        self.assertTrue(all('alternative' in r[2].lower() for r in opts))
        self.assertEqual(self.db.execute('SELECT count(*) FROM step_monsters JOIN quest_steps USING(step_id) WHERE quest_id=?',(q,)).fetchone()[0],0)
        actors=[tuple(r) for r in self.db.execute('''SELECT s.step_order,e.original_name FROM step_npcs n JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=n.npc_id WHERE s.quest_id=? AND s.step_order IN (6,7) ORDER BY s.step_order''',(q,))]
        self.assertEqual(actors,[(6,'Female Researcher'),(7,'Eva')])

    def test_crow_photos_album_and_reward_installments(self):
        q='irowiki-classic:Crow_of_Destiny_Quest'
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='shown_photo_count'",(q,)).fetchone()[0],'2')
        album=self.db.execute('''SELECT r.amount,x.game_id FROM rewards r JOIN entities e ON e.entity_id=r.item_id
          JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND e.original_name="Mammi's Photo Album" ''',(q,)).fetchone()
        self.assertEqual(tuple(album),(None,None))
        box=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND x.game_id=617''',(q,))]
        self.assertEqual(box,[(19,'1')])
        xp=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.kind,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) WHERE r.quest_id=? AND r.kind IN ('base_exp','job_exp') ORDER BY r.kind''',(q,))]
        self.assertEqual(xp,[(21,'base_exp','900000'),(21,'job_exp','900000')])

    def test_crow_torn_and_final_notes_keep_identity_uncertainty(self):
        q='irowiki-classic:Crow_of_Destiny_Quest'
        refs={}
        for n in [19,20,21]:
            refs[n]={r[0] for r in self.db.execute('''SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
              WHERE s.quest_id=? AND s.step_order=? AND m.field='full_en' AND m.surface='note' ''',(q,n))}
        self.assertEqual(len(refs[19]),1);self.assertEqual(refs[19],refs[20]);self.assertTrue(refs[19].isdisjoint(refs[21]))
        self.assertEqual(len(refs[21]),1)
        self.assertTrue(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='torn_and_final_note_identity_unresolved'",(q,)).fetchone())
        feather=[tuple(r) for r in self.db.execute('''SELECT i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND s.step_order=20 AND e.original_name='bird feather' ''',(q,))]
        self.assertEqual(feather,[('show',None)])

    def test_footprint_rental_per_attempt_cost_and_retained_items(self):
        q='irowiki-classic:Footprint_Quest'
        costs={r[0]:r[1] for r in self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('zeny_rental','item_per_attempt')",(q,))}
        self.assertEqual(costs,{'zeny_rental':'50','item_per_attempt':'1'})
        self.assertFalse(self.db.execute("SELECT 1 FROM requirements r JOIN items x ON x.entity_id=r.entity_id WHERE r.quest_id=? AND r.kind='item_consumed_total' AND x.game_id=528",(q,)).fetchone())
        kept=[tuple(r) for r in self.db.execute("SELECT x.game_id,r.amount FROM requirements r JOIN items x ON x.entity_id=r.entity_id WHERE r.quest_id=? AND r.kind='item_not_consumed' ORDER BY x.game_id",(q,))]
        self.assertEqual(kept,[(7285,None),(7287,None)])
        items=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id WHERE s.quest_id=? AND x.game_id IN (504,528,7288) ORDER BY s.step_order''',(q,))]
        self.assertEqual(items,[(2,528,'consume',1),(3,7288,'collect',None),(7,504,'deliver',1)])

    def test_footprint_nine_triggers_exact_rescue_tile_and_completion_gate(self):
        q='irowiki-classic:Footprint_Quest'
        points=[tuple(r) for r in self.db.execute('''SELECT m.map_code,l.x,l.y,sl.location_order FROM step_locations sl JOIN locations l USING(location_id)
          JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=6 ORDER BY m.map_code,l.x,l.y''',(q,))]
        expected=[('ayo_dun01',x,y,None) for x,y in [(18,258),(73,142),(243,257),(256,61)]]+[('ayo_dun02',x,y,None) for x,y in [(73,213),(87,254),(89,262),(136,169),(146,117)]]
        self.assertEqual(points,expected)
        rescue=[tuple(r) for r in self.db.execute('''SELECT m.map_code,l.x,l.y FROM step_locations sl JOIN locations l USING(location_id)
          JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=7''',(q,))]
        self.assertEqual(rescue,[('ayo_dun02',91,262)])
        gate=self.db.execute("SELECT e.step_id,e.snippet FROM quest_relations r JOIN evidence e USING(evidence_id) WHERE r.quest_id=? AND r.kind='completion_required_at_step'",(q,)).fetchone()
        self.assertIn('including retrieval of the Old Blue Box',gate[1])
        self.assertEqual(self.db.execute('SELECT step_order FROM quest_steps WHERE step_id=?',(gate[0],)).fetchone()[0],4)
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards r JOIN entities e ON e.entity_id=r.item_id WHERE r.quest_id=? AND e.original_name='Old Blue Box'",(q,)).fetchone())

    def test_footprint_potions_are_intermediate_and_final_exp_selects_one_band(self):
        q='irowiki-classic:Footprint_Quest'
        potions=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=r.item_id WHERE r.quest_id=? AND e.original_name='Random Potions' ''',(q,))]
        self.assertEqual(potions,[(6,'10')])
        bands=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,b.choice_group,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY b.branch_key''',(q,))]
        expected=[('level_'+key,'final_level_reward',str(amount)) for key,amount in [('1_55',9000),('56_60',10500),('61_65',18684),('66_70',27411),('71_75',70757),('76_80',130246),('81_85',150340),('86_90',182052),('91_99',406786)]]
        self.assertEqual(bands,expected)
        self.assertFalse(self.db.execute("SELECT 1 FROM rewards WHERE quest_id=? AND kind='base_exp' AND branch_id IS NULL",(q,)).fetchone())

    def test_new_world_staff_identity_and_source_availability(self):
        q='irowiki-classic:Onward_to_the_New_World_Quest'
        staff=[r[0] for r in self.db.execute('''SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=2 AND m.field='full_en' AND m.surface='Promotional Staff' ORDER BY m.start_offset''',(q,))]
        self.assertEqual(len(set(staff)),2)
        self.assertEqual(staff[1],self.db.execute('''SELECT npc_id FROM step_npcs JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND step_order=3''',(q,)).fetchone()[0])
        managers=[r[0] for r in self.db.execute('''SELECT npc_id FROM step_npcs JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND step_order IN (10,12)''',(q,))]
        self.assertEqual(len(set(managers)),2)
        self.assertEqual(self.db.execute('SELECT availability FROM quests WHERE quest_id=?',(q,)).fetchone()[0],'unknown')
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='source_classic_availability_warning'",(q,)).fetchone())

    def test_new_world_deliveries_combat_and_reward_installments(self):
        q='irowiki-classic:Onward_to_the_New_World_Quest'
        delivered=[r[0] for r in self.db.execute('''SELECT e.original_name FROM step_actions a JOIN entities e USING(entity_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=3 AND a.operation='deliver' ORDER BY a.action_order''',(q,))]
        self.assertEqual(delivered,['Emerald','Ruby'])
        self.assertEqual([tuple(r) for r in self.db.execute('''SELECT s.step_order,r.kind,r.amount FROM rewards r
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s ON s.step_id=sr.step_id
          WHERE r.quest_id=? AND r.kind IN ('base_exp','job_exp') ORDER BY s.step_order,r.kind''',(q,))],
          [(5,'base_exp','660000'),(5,'job_exp','210000'),(17,'base_exp','660000'),(17,'job_exp','210000')])
        monster=self.db.execute('''SELECT x.game_id,m.quantity,m.required FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND m.operation='kill' ''',(q,)).fetchone()
        self.assertEqual(tuple(monster),(1313,50,1))

    def test_surroundings_shared_repair_materials_and_action_reuse(self):
        q='irowiki-classic:New_Surroundings'
        rows=self.db.execute('''SELECT s.step_order,e.original_name,i.operation,i.quantity FROM step_items i
          JOIN entities e ON e.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (6,9,10) ORDER BY s.step_order,e.original_name''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(n,item,op,qty) for n,op,qty in [(6,'bring',20),(9,'use',None),(10,'use',None)] for item in ['Ordinary Branch','Strong Vine']])
        options=[r[0] for r in self.db.execute('''SELECT o.exact_text FROM dialogue_options o JOIN step_dialogues sd USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=9 ORDER BY o.option_order''',(q,))]
        self.assertEqual(options,['Attach the Tree Bar','Tie it with vines','Knot it'])
        edge=self.db.execute('''SELECT a.step_order,b.step_order FROM step_edges e JOIN quest_steps a ON a.step_id=e.from_step
          JOIN quest_steps b ON b.step_id=e.to_step WHERE a.quest_id=? AND e.kind='repeat_actions_from' ''',(q,)).fetchone()
        self.assertEqual(tuple(edge),(10,9))
        self.assertEqual(self.db.execute('''SELECT m.operation,m.required,m.quantity FROM step_monsters m
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchone()[:],('loot_source',0,None))

    def test_surroundings_pies_boxes_and_letters_are_not_double_counted(self):
        q='irowiki-classic:New_Surroundings'
        pies=self.db.execute('''SELECT count(*),sum(CAST(r.amount AS INTEGER)) FROM rewards r JOIN items x ON x.entity_id=r.item_id
          WHERE r.quest_id=? AND x.game_id=12322''',(q,)).fetchone()
        self.assertEqual(tuple(pies),(12,20))
        boxes=self.db.execute('''SELECT sum(i.quantity) FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND x.game_id=6045 AND i.operation='deliver' ''',(q,)).fetchone()[0]
        self.assertEqual(boxes,3)
        letters=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,i.quantity FROM step_items i
          JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND x.game_id IN (6043,6044) AND i.operation='deliver' ORDER BY s.step_order''',(q,))]
        self.assertEqual(letters,[(21,6044,1),(22,6043,1)])
        self.assertEqual(self.db.execute('''SELECT alias FROM entity_aliases a JOIN items x USING(entity_id)
          WHERE x.game_id=6043 AND alias=?''',("Otto's Letter",)).fetchone()[0],"Otto's Letter")

    def test_attitude_item_hunt_and_extra_reward_scope(self):
        q='irowiki-classic:Attitude_to_the_New_World'
        kills=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,m.quantity FROM step_monsters m
          JOIN monsters x ON x.entity_id=m.monster_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND m.operation='kill' ORDER BY s.step_order''',(q,))]
        self.assertEqual(kills,[(2,1988,1),(5,1995,30),(6,1992,10),(14,1986,10)])
        horn=self.db.execute('''SELECT x.game_id,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=13''',(q,)).fetchone()
        self.assertEqual(tuple(horn),(6032,20,'deliver'))
        self.assertEqual([tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY s.step_order''',(q,))],[(15,'1500000'),(16,'1000000')])
        route=self.db.execute('''SELECT r.kind,s.step_order,r.condition_text FROM quest_routes r JOIN step_routes m USING(route_id)
          JOIN quest_steps s USING(step_id) WHERE r.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(route[:2]),('optional',16));self.assertIn('required',route[2]);self.assertIn('Report',route[2])

    def test_new_world_relations_keep_literal_step_evidence_and_shared_identity(self):
        rows=[tuple(r) for r in self.db.execute('''SELECT r.quest_id,r.related_id,r.kind,s.step_order FROM quest_relations r
          JOIN evidence e USING(evidence_id) LEFT JOIN quest_steps s ON s.step_id=e.step_id
          WHERE e.method IN ('curated_quest_relation','curated_single_prerequisite') AND r.quest_id IN ('irowiki-classic:Attitude_to_the_New_World','irowiki-classic:New_Surroundings','irowiki-classic:Pursuing_Rayan_Moore',"irowiki-classic:Tripatriate_Union's_Feud") ORDER BY r.quest_id,r.kind''')]
        prefix='irowiki-classic:'
        self.assertEqual(rows,[(prefix+'Attitude_to_the_New_World',prefix+'Report_from_the_New_World','extra_stage_required_for_prerequisite',16),
          (prefix+'Attitude_to_the_New_World',prefix+'Onward_to_the_New_World_Quest','mandatory_prerequisite',None),
          (prefix+'Attitude_to_the_New_World',prefix+'New_Surroundings','source_concurrency_constraint',2),
          (prefix+'New_Surroundings',prefix+'Onward_to_the_New_World_Quest','mandatory_prerequisite',None),
          (prefix+'Pursuing_Rayan_Moore',prefix+'Onward_to_the_New_World_Quest','mandatory_prerequisite',None),
          (prefix+"Tripatriate_Union's_Feud",prefix+'Onward_to_the_New_World_Quest','mandatory_prerequisite',None)])
        ids={r[0] for r in self.db.execute('''SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id IN (?,?) AND m.surface='Diego' ''',(prefix+'New_Surroundings',prefix+'Attitude_to_the_New_World'))}
        self.assertEqual(len(ids),1)
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_npcs n JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=n.npc_id WHERE s.quest_id=? AND s.step_order=2 AND e.original_name='Diego' ''',(prefix+'Attitude_to_the_New_World',)).fetchone()[0],0)

    def test_action_reuse_and_curated_relation_corruption_fail_closed(self):
        cases=[
          ("UPDATE step_edges SET to_step=(SELECT step_id FROM quest_steps WHERE quest_id='irowiki-classic:New_Surroundings' AND step_order=8) WHERE kind='repeat_actions_from' AND from_step=(SELECT step_id FROM quest_steps WHERE quest_id='irowiki-classic:New_Surroundings' AND step_order=10)",'invalid_action_reuse_target'),
          ("UPDATE quest_relations SET related_id='irowiki-classic:Juice_Quest' WHERE kind='extra_stage_required_for_prerequisite'",'invalid_curated_quest_relation'),
          ("UPDATE evidence SET method='source_literal' WHERE method='curated_quest_relation'",'invalid_curated_quest_relation'),
          ("UPDATE quest_relations SET related_id='irowiki-classic:Juice_Quest' WHERE kind='mandatory_prerequisite'",'invalid_mandatory_prerequisite'),
        ]
        for sql,error in cases:
            with self.subTest(error=error),contextlib.closing(sqlite3.connect(':memory:')) as db:
                self.db.backup(db);db.execute(sql)
                with self.assertRaisesRegex(ValueError,error):validate(db)
        for name,mutate,error in [
          ('reuse',lambda d:d['steps']['10']['action_reuse'].update(source_step=10),'Action reuse requires an earlier step'),
          ('prerequisite',lambda d:d['relations'][0].update(related_quest_id='irowiki-classic:Juice_Quest'),'Header does not establish a single mandatory prerequisite'),
        ]:
            with self.subTest(name=name):
                folder=self.out/('invalid-'+name);shutil.copytree(ROOT/'curation',folder)
                path=folder/'New_Surroundings.json';data=json.loads(path.read_text());mutate(data);path.write_text(json.dumps(data))
                before=(self.out/'quest_semantic.sqlite').read_bytes()
                with self.assertRaisesRegex(ValueError,error):build(self.source,self.out,curation_dir=folder)
                self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_report_requires_two_of_four_with_attitude_extra_stage(self):
        q='irowiki-classic:Report_from_the_New_World'
        group=self.db.execute('SELECT group_id,minimum_completed FROM quest_prerequisite_groups WHERE quest_id=?',(q,)).fetchone()
        self.assertEqual(group[1],2)
        members=[r[0] for r in self.db.execute('SELECT related_id FROM quest_prerequisite_members WHERE group_id=? ORDER BY member_order',(group[0],))]
        self.assertEqual(members,['irowiki-classic:'+name for name in ['Attitude_to_the_New_World','New_Surroundings',"Tripatriate_Union's_Feud",'Pursuing_Rayan_Moore']])
        self.assertEqual(self.db.execute("SELECT count(*) FROM quest_relations WHERE quest_id=? AND kind='mandatory_prerequisite'",(q,)).fetchone()[0],0)
        prerequisite_step=self.db.execute('''SELECT s.step_order FROM quest_relations r JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=e.step_id WHERE r.quest_id=? AND r.related_id=? AND r.kind='extra_stage_required_for_prerequisite' ''',(members[0],q)).fetchone()[0]
        self.assertEqual(prerequisite_step,16)
        self.assertEqual(self.report['prerequisite_groups'],1)

    def test_report_scouts_are_alternatives_and_timers_losses_are_variant_scoped(self):
        q='irowiki-classic:Report_from_the_New_World'
        facts=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,r.kind,r.amount FROM requirements r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? ORDER BY b.branch_key''',(q,))]
        self.assertEqual(facts,[('default_timer','search_interval_minutes','30'),('iro_timer','search_interval_minutes','15')])
        losses=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,i.quantity FROM step_items i JOIN step_branches b USING(branch_id)
          JOIN quest_steps s ON s.step_id=i.step_id WHERE s.quest_id=? AND i.operation='consume' ORDER BY b.branch_key''',(q,))]
        self.assertEqual(losses,[('default_failure',10),('iro_failure',5)])
        scouts=[r[0] for r in self.db.execute('''SELECT npc_id FROM step_npcs JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND step_order=7 AND role='alternative_contact' ''',(q,))]
        self.assertEqual(len(set(scouts)),4)
        collective=self.db.execute('''SELECT entity_id FROM description_mentions JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND step_order=7 AND field='full_en' AND surface='Expedition Scout' ''',(q,)).fetchone()[0]
        self.assertNotIn(collective,scouts)
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_locations JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND step_order=7 AND location_order IS NULL AND role='alternative_scout' ''',(q,)).fetchone()[0],4)
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_monsters JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND operation='kill' ''',(q,)).fetchone()[0],0)

    def test_report_retries_do_not_invent_copy_ids_or_fixed_page_totals(self):
        q='irowiki-classic:Report_from_the_New_World'
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='distinct_report_volumes'",(q,)).fetchone()[0],'4')
        copies=self.db.execute('''SELECT i.quantity,x.game_id FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order IN (10,11,12)''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in copies],[(None,None)]*3)
        self.assertEqual([r[0] for r in self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,))],['2500000'])
        self.assertIsNone(self.db.execute('''SELECT 1 FROM step_items i JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND i.quantity=40''',(q,)).fetchone())

    def test_rayan_main_rewards_and_optional_wrap_up_are_separate(self):
        q='irowiki-classic:Pursuing_Rayan_Moore'
        grants=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.kind,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? AND r.kind IN ('base_exp','job_exp') ORDER BY s.step_order,r.kind''',(q,))]
        self.assertEqual(grants,[(7,'base_exp','200000'),(7,'job_exp','10000'),(25,'base_exp','1200000'),(25,'job_exp','200000')])
        self.assertEqual([tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=15 ORDER BY x.game_id''',(q,))],[(523,30),(7511,30),(7563,30)])
        route=self.db.execute('''SELECT r.kind,s.step_order FROM quest_routes r JOIN step_routes m USING(route_id)
          JOIN quest_steps s USING(step_id) WHERE r.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(route),('optional',26))
        self.assertEqual([tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=26 ORDER BY x.game_id''',(q,))],[(516,20),(7201,20)])
        battle=self.db.execute('''SELECT m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(battle),(None,None))
        self.assertIsNone(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='battle_time_limit'",(q,)).fetchone()[0])

    def test_tripatriate_completion_is_separate_from_repeatable_food_recipes(self):
        q="irowiki-classic:Tripatriate_Union's_Feud"
        main=[tuple(r) for r in self.db.execute('''SELECT x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          WHERE r.quest_id=? AND r.branch_id IS NULL AND x.game_id IN (12110,12319,12320,12321) ORDER BY x.game_id''',(q,))]
        self.assertEqual(main,[(12110,'1'),(12319,'1'),(12320,'1'),(12321,'1')])
        recipes=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,x.game_id,i.quantity,i.required FROM step_branches b
          JOIN step_items i USING(branch_id) JOIN items x ON x.entity_id=i.item_id JOIN quest_steps s ON s.step_id=b.step_id
          WHERE s.quest_id=? ORDER BY b.branch_key''',(q,))]
        self.assertEqual(recipes,[('hansenne_exchange',6021,5,0),('hue_exchange',6020,5,0),('ryosen_exchange',6023,5,0)])
        full,original=self.db.execute('''SELECT d.full_en,s.original_instruction FROM step_descriptions d JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=21''',(q,)).fetchone()
        self.assertNotIn('Mystic Horn',full);self.assertIn('Mystic Horn',original)
        count=self.db.execute('''SELECT m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND m.operation='kill' ''',(q,)).fetchone()
        self.assertEqual(tuple(count),(None,1051))

    def test_prerequisite_group_corruption_and_invalid_curation_fail_closed(self):
        for sql,error in [
          ("UPDATE quest_prerequisite_groups SET minimum_completed=4",'invalid_prerequisite_group'),
          ("DELETE FROM quest_prerequisite_members WHERE member_order=4",'invalid_prerequisite_group'),
          ("UPDATE quest_prerequisite_members SET related_id='irowiki-classic:Juice_Quest' WHERE member_order=4",'invalid_prerequisite_group'),
          ("UPDATE quest_prerequisite_members SET member_order=member_order+10",'invalid_prerequisite_group_evidence'),
          ("UPDATE evidence SET method='candidate' WHERE method='curated_prerequisite_group'",'invalid_prerequisite_group_evidence'),
        ]:
            with self.subTest(sql=sql),contextlib.closing(sqlite3.connect(':memory:')) as db:
                self.db.backup(db);db.execute(sql)
                with self.assertRaisesRegex(ValueError,error):validate(db)
        folder=self.out/'invalid-prerequisite-group';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Report_from_the_New_World.json';data=json.loads(path.read_text())
        data['prerequisite_groups'][0]['minimum_completed']=4;path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Prerequisite group contradicts source header'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_human_prerequisite_group_cannot_be_silently_replaced(self):
        folder=self.out/'human-prerequisite-group';folder.mkdir();target=folder/'quest_semantic.sqlite'
        shutil.copyfile(self.out/'quest_semantic.sqlite',target)
        with contextlib.closing(sqlite3.connect(target)) as db:
            db.execute("UPDATE quest_prerequisite_groups SET review_state='human_reviewed'");db.commit()
        before=target.read_bytes()
        with self.assertRaisesRegex(ValueError,'Human-reviewed prerequisite groups'):build(self.source,folder)
        self.assertEqual(before,target.read_bytes())

    def test_fairy_installments_and_ring_header_reward_provenance(self):
        q='irowiki-classic:Finding_a_Fairy'
        grants=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY s.step_order''',(q,))]
        self.assertEqual(grants,[(2,'700000'),(4,'700000')])
        q='irowiki-classic:Ring_of_the_Wise_King'
        exact=[tuple(r) for r in self.db.execute('''SELECT r.kind,r.amount,e.method FROM rewards r JOIN evidence e USING(evidence_id)
          WHERE r.quest_id=? AND r.kind IN ('base_exp','job_exp') ORDER BY r.kind''',(q,))]
        self.assertEqual(exact,[('base_exp','1000000','curated_rewards'),('job_exp','300000','curated_rewards')])
        ring=self.db.execute('''SELECT x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN entities e ON e.entity_id=r.item_id WHERE r.quest_id=? AND e.original_name='Ring of the Wise Ancient King' ''',(q,)).fetchone()
        self.assertEqual(tuple(ring),(2782,'1'))

    def test_started_and_finished_prerequisites_have_distinct_typed_relations(self):
        rows=[tuple(r) for r in self.db.execute('''SELECT r.quest_id,r.related_id,r.kind,e.snippet FROM quest_relations r
          JOIN evidence e USING(evidence_id) WHERE e.method='curated_prerequisite_clause' AND r.quest_id IN ('irowiki-classic:Guardian_of_Yggdrasil','irowiki-classic:Ring_of_the_Wise_King') ORDER BY r.quest_id,r.kind''')]
        p='irowiki-classic:'
        self.assertEqual(rows,[(p+'Guardian_of_Yggdrasil',p+'Report_from_the_New_World','required_completed_quest','Finished Report from the New World'),
          (p+'Guardian_of_Yggdrasil',p+'Two_Tribes','required_started_quest','Started Two Tribes (To the point where you enter the Dungeon)'),
          (p+'Ring_of_the_Wise_King',p+'Finding_a_Fairy','required_quest','Finding a Fairy'),
          (p+'Ring_of_the_Wise_King',p+'Report_from_the_New_World','required_started_quest','Started Report from the New World')])
        stage=self.db.execute('''SELECT s.step_order FROM quest_relations r JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=e.step_id WHERE r.quest_id=? AND r.related_id=? AND r.kind='stage_required_to_start' ''',(p+'Two_Tribes',p+'Guardian_of_Yggdrasil')).fetchone()[0]
        self.assertEqual(stage,7);self.assertEqual(self.report['phase_prerequisites'],12)

    def test_ring_literal_language_inputs_and_separate_gem_objects(self):
        q='irowiki-classic:Ring_of_the_Wise_King'
        options=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,o.exact_text,o.operation FROM dialogue_options o
          JOIN dialogues d USING(dialogue_id) JOIN step_branches b ON b.branch_id=d.branch_id
          JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id=? ORDER BY b.branch_key,o.option_order''',(q,))]
        self.assertEqual(options,[('iro_language','RLGHLRXLA TKANTLFDMS WJACK TNAHRDNJSDMFH WLSGHKWND !!','enter_text'),
          ('iro_language','TJDTMFJDNS CJFDI TKADLFDMF QKATOS EKDTLSDML DLFMADMS..','enter_text'),
          ('non_iro_language','TKANTLFDMS TNAHRDNJSDMFH WLSGHKWND','enter_text'),
          ('non_iro_language','CJFDI QKATOS DLFMADMS..','enter_text')])
        gems=[r[0] for r in self.db.execute('''SELECT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (5,6)''',(q,))]
        self.assertEqual(len(set(gems)),2)
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='wait_hours'",(q,)).fetchone()[0],'1')
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_monsters m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND m.quantity IS NOT NULL''',(q,)).fetchone()[0],0)

    def test_two_tribes_berry_preparation_and_all_held_loss_are_distinct(self):
        q='irowiki-classic:Two_Tribes'
        granted=self.db.execute('''SELECT r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE s.quest_id=? AND s.step_order=15''',(q,)).fetchone()[0]
        collected=[r[0] for r in self.db.execute('''SELECT i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (16,17,18) ORDER BY s.step_order''',(q,))]
        self.assertEqual((granted,collected),('3',[1,1,1]))
        delivery=self.db.execute('''SELECT i.quantity,i.condition_text FROM step_items i JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=19''',(q,)).fetchone()
        self.assertIsNone(delivery[0]);self.assertIn('ALL',delivery[1])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='berry_overcollection_loss_threshold'",(q,)).fetchone()[0],'6')
        ring=self.db.execute('''SELECT x.game_id FROM requirements r JOIN items x ON x.entity_id=r.entity_id
          WHERE r.quest_id=? AND r.kind='equipped_item' ''',(q,)).fetchone()[0]
        self.assertIsNone(ring)

    def test_guardian_choices_do_not_double_rewards_or_invent_password(self):
        q='irowiki-classic:Guardian_of_Yggdrasil'
        groups=[tuple(r) for r in self.db.execute('''SELECT s.step_order,count(DISTINCT b.choice_group),count(*) FROM step_branches b
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? GROUP BY s.step_order ORDER BY s.step_order''',(q,))]
        self.assertEqual(groups,[(5,1,2),(8,1,2),(10,1,2)])
        rewards=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,r.kind,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? AND b.branch_key LIKE 'reward_%' ORDER BY b.branch_key,r.kind''',(q,))]
        self.assertEqual(rewards,[(branch,kind,amount) for branch in ['reward_manuk','reward_splendide'] for kind,amount in [('base_exp','1500000'),('item','10'),('job_exp','35000')]])
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND branch_id IS NULL",(q,)).fetchone()[0],0)
        self.assertEqual(self.db.execute('''SELECT count(*) FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchone()[0],0)
        self.assertEqual(self.db.execute('SELECT availability FROM quests WHERE quest_id=?',(q,)).fetchone()[0],'unknown')
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='source_iro_completion_warning'",(q,)).fetchone())

    def test_guardian_navigation_keeps_source_typo_and_two_tribes_map_witnesses(self):
        q='irowiki-classic:Guardian_of_Yggdrasil'
        points=[tuple(r) for r in self.db.execute('''SELECT m.map_code,l.x,l.y,sl.location_order FROM step_locations sl
          JOIN locations l USING(location_id) JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=1 ORDER BY sl.location_order''',(q,))]
        self.assertEqual(points,[('spl_fld01',368,112,1),('nyd_dun01',252,144,2),('nyd_dun02',137,274,3),('nyd_dun02',99,199,4)])
        self.assertEqual(self.db.execute("SELECT known_in_reference FROM maps WHERE map_code='spl_fld01'").fetchone()[0],0)
        inherited=[tuple(r) for r in self.db.execute('''SELECT s.step_order,m.map_code FROM step_locations sl JOIN locations l USING(location_id)
          JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order IN (7,9) ORDER BY s.step_order''',('irowiki-classic:Two_Tribes',))]
        self.assertEqual(inherited,[(7,'spl_fild01'),(9,'nyd_dun01')])
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_locations sl JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=5 AND (sl.condition_text IS NULL OR sl.role='waypoint')''',(q,)).fetchone()[0],0)

    def test_phase_prerequisite_corruption_and_unsupported_curations_fail_closed(self):
        for sql,error in [
          ("UPDATE quest_relations SET kind='required_completed_quest' WHERE kind='required_started_quest'",'invalid_phase_prerequisite'),
          ("DELETE FROM quest_relations WHERE kind IN ('required_quest','required_started_quest','required_completed_quest')",'incomplete_phase_prerequisite_list'),
          ("UPDATE evidence SET method='candidate' WHERE method='curated_prerequisite_clause'",'invalid_phase_prerequisite_evidence'),
        ]:
            with self.subTest(sql=sql),contextlib.closing(sqlite3.connect(':memory:')) as db:
                self.db.backup(db);db.execute(sql)
                with self.assertRaisesRegex(ValueError,error):validate(db)
        folder=self.out/'invalid-phase-prerequisite';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Ring_of_the_Wise_King.json';data=json.loads(path.read_text());data['prerequisite_clauses'][1]['kind']='required_completed_quest';path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Phase prerequisite contradicts source clause'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_einbroch_password_pairs_and_repeat_apples(self):
        q='irowiki-classic:Einbroch_Murder_Quest'
        answers=[tuple(r) for r in self.db.execute('''SELECT d.original_text,o.exact_text,o.operation FROM dialogues d
          JOIN dialogue_options o USING(dialogue_id) JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=4 ORDER BY sd.dialogue_order''',(q,))]
        self.assertEqual(len(answers),7);self.assertTrue(all(r[2]=='enter_text' for r in answers))
        self.assertNotEqual(answers[4][0],answers[5][0]);self.assertEqual(answers[4][1],answers[5][1])
        self.assertEqual(answers[-1][1],"opeN,Open!op3n.openOpen0p3nOpEn0pen`open'0Pen open?open!111OPENSESAME")
        apples=[tuple(r) for r in self.db.execute('''SELECT i.operation,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=6 ORDER BY i.operation''',(q,))]
        self.assertEqual(apples,[('buy',100),('deliver',None)])

    def test_einbroch_level_bands_and_scientist_identities(self):
        q='irowiki-classic:Einbroch_Murder_Quest'
        grants=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,r.kind,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? ORDER BY b.branch_key,r.kind''',(q,))]
        self.assertEqual(grants,[(key,kind,amount) for key,base,job in [('level_01_69','100000','80000'),('level_70_79','300000','100000'),('level_80_89','500000','300000'),('level_90_99','700000','500000')] for kind,amount in [('base_exp',base),('job_exp',job)]])
        ids=[r[0] for r in self.db.execute('''SELECT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (5,6) AND m.field='full_en' AND m.surface='scientist' ''',(q,))]
        self.assertEqual(len(set(ids)),2)
        self.assertEqual(self.db.execute('SELECT count(*) FROM rewards WHERE quest_id=? AND branch_id IS NULL',(q,)).fetchone()[0],0)

    def test_president_appointment_and_completion_gate_scope(self):
        q='irowiki-classic:President_Quest'
        slots=[r[0] for r in self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='appointment_window' ORDER BY amount",(q,))]
        self.assertEqual(slots,['11 AM to 2 PM','8 PM to 11 PM'])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_timezone'",(q,)).fetchone()[0],'Pacific Time')
        gate=self.db.execute('''SELECT s.step_order,r.related_id FROM quest_relations r JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=e.step_id WHERE r.quest_id=? AND r.kind='completion_required_at_step' ''',(q,)).fetchone()
        self.assertEqual(tuple(gate),(22,'irowiki-classic:Einbroch_Murder_Quest'))
        self.assertEqual(self.db.execute("SELECT count(*) FROM quest_relations WHERE quest_id=? AND kind='required_completed_quest'",(q,)).fetchone()[0],0)

    def test_president_pin_search_case_and_retained_disguise(self):
        q='irowiki-classic:President_Quest'
        options=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,o.exact_text FROM dialogue_options o JOIN dialogues d USING(dialogue_id)
          JOIN step_branches b ON b.branch_id=d.branch_id JOIN quest_steps s ON s.step_id=b.step_id WHERE s.quest_id=? ORDER BY b.branch_key''',(q,))]
        self.assertEqual(options,[('iro_search','shinokas'),('source_search','Shinokas')])
        pin=self.db.execute('''SELECT o.exact_text FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=28''',(q,)).fetchone()[0]
        self.assertEqual(pin,'738495')
        disguise=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.operation,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=25 ORDER BY x.game_id''',(q,))]
        self.assertEqual(disguise,[(2241,'possess',1),(2243,'possess',1)])
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],'1800000')

    def test_rebellion_random_materials_and_class_prices(self):
        q='irowiki-classic:Rebellion_Quest'
        sets=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,i.quantity FROM step_items i JOIN step_branches b USING(branch_id)
          JOIN quest_steps s ON s.step_id=i.step_id WHERE s.quest_id=? AND s.step_order=1 ORDER BY b.branch_key''',(q,))]
        self.assertEqual(sets,[('clams',5),('mushrooms',5),('shells',5)])
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_items i JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=1 AND i.branch_id IS NULL''',(q,)).fetchone()[0],0)
        prices=[tuple(r) for r in self.db.execute('''SELECT b.branch_key,r.amount FROM requirements r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? AND r.kind='zeny_per_drink' ORDER BY b.branch_key''',(q,))]
        self.assertEqual(prices,[('assassin_price','800'),('normal_price','1000')])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_zeny_summary'",(q,)).fetchone()[0],'3000')

    def test_rebellion_bottle_combat_and_early_repeatable_unlock(self):
        q='irowiki-classic:Rebellion_Quest'
        kill=self.db.execute('''SELECT m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND m.operation='kill' ''',(q,)).fetchone()
        self.assertEqual(tuple(kill),(2,None))
        bottle=self.db.execute('''SELECT x.game_id,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=14''',(q,)).fetchone()
        self.assertEqual(tuple(bottle),(7138,1,'consume'))
        optional=self.db.execute('''SELECT s.step_order,i.quantity,i.required FROM step_items i JOIN step_branches b USING(branch_id)
          JOIN quest_steps s ON s.step_id=i.step_id WHERE s.quest_id=? AND b.branch_key='food_exchange' ''',(q,)).fetchone()
        self.assertEqual(tuple(optional),(15,100,0))
        self.assertEqual(self.db.execute("SELECT count(*) FROM quest_relations WHERE quest_id=? AND kind='required_quest'",(q,)).fetchone()[0],4)
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='all_header_prerequisites_completed'",(q,)).fetchone()[0],'4')
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],'2000000')

    def test_president_gate_and_search_corruption_fail_closed(self):
        with contextlib.closing(sqlite3.connect(':memory:')) as db:
            self.db.backup(db);db.execute("UPDATE quest_relations SET related_id='irowiki-classic:Factory_Quest' WHERE kind='completion_required_at_step'")
            with self.assertRaisesRegex(ValueError,'invalid_curated_quest_relation'):validate(db)
        folder=self.out/'invalid-president-search';shutil.copytree(ROOT/'curation',folder)
        path=folder/'President_Quest.json';data=json.loads(path.read_text())
        data['steps']['29']['branches'][1]['dialogues'][0]['options'][0]['text']='SHINOKAS';path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'exact dialogue answer'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_airship_attack_is_optional_and_four_minutes_is_not_spawn_rate(self):
        q='irowiki-classic:How_the_Airship_Works_Quest'
        route=self.db.execute('''SELECT r.kind,s.step_order FROM quest_routes r JOIN step_routes m USING(route_id)
          JOIN quest_steps s USING(step_id) WHERE r.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(route),('optional',4))
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='attack_cleanup_minutes'",(q,)).fetchone()[0],'4')
        self.assertIsNone(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='attack_spawn_interval'",(q,)).fetchone()[0])
        materials=[tuple(r) for r in self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=5 ORDER BY x.game_id''',(q,))]
        self.assertEqual(materials,[(576,2),(7340,2)])
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],'200000')

    def test_heart_gate_potion_installments_and_shared_ferlock(self):
        q='irowiki-classic:Heart_Fragment_Quest'
        gate=self.db.execute('''SELECT s.step_order FROM quest_relations r JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=e.step_id WHERE r.quest_id=? AND r.kind='completion_required_at_step' ''',(q,)).fetchone()[0]
        self.assertEqual(gate,2)
        potions=[tuple(r) for r in self.db.execute('''SELECT s.step_order,x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(potions,[(2,501,'1'),(4,501,'1')])
        ids={r[0] for r in self.db.execute('''SELECT n.npc_id FROM step_npcs n JOIN entities e ON e.entity_id=n.npc_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id IN (?,?) AND e.original_name='Ferlock' ''',(q,'irowiki-classic:How_the_Airship_Works_Quest'))}
        self.assertEqual(len(ids),1)

    def test_heart_partial_passwords_and_random_code_are_not_fixed_answers(self):
        q='irowiki-classic:Heart_Fragment_Quest'
        choices=[tuple(r) for r in self.db.execute('''SELECT o.exact_text,o.correct FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=6 ORDER BY o.option_order''',(q,))]
        self.assertEqual(len(choices),12);self.assertTrue(all(x[1] is None for x in choices))
        self.assertIn(('hfjdkeldjsieldjshfjdjeiskdlefvbd',None),choices)
        self.assertIn(('fReeDoM eCstAcy JoUrnaLiSm ArMpIt DisCoverY hEaDaChE MoonbeAmS jUsTiCE',None),choices)
        rules=dict(self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind LIKE 'code_%'",(q,)))
        self.assertEqual(rules,{'code_attempt_limit':'5','code_digit_count':'3','code_digit_range':'1-9','code_digits_distinct':'true'})
        self.assertEqual(self.db.execute('''SELECT count(*) FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=7''',(q,)).fetchone()[0],0)

    def test_ticket_package_returns_to_sender_and_repair_is_a_service(self):
        q='irowiki-classic:Airship_Ticket_Quest'
        absent=self.db.execute('''SELECT n.role FROM step_npcs n JOIN entities e ON e.entity_id=n.npc_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=3 AND e.original_name='Defra' ''',(q,)).fetchone()[0]
        self.assertEqual(absent,'absent_target')
        recipient=self.db.execute('''SELECT e.original_name FROM step_npcs n JOIN entities e ON e.entity_id=n.npc_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=5''',(q,)).fetchone()[0]
        self.assertEqual(recipient,'Kain Himere')
        reward=self.db.execute('''SELECT x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND x.game_id=7311''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in reward],[(7311,'4')])
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='service_unlock'",(q,)).fetchone()[0],1)

    def test_unknown_journals_are_not_merged_by_label_across_quests(self):
        rows=self.db.execute('''SELECT DISTINCT s.quest_id,i.item_id,x.game_id FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id
          WHERE s.quest_id IN ('irowiki-classic:Airship_Ticket_Quest','irowiki-classic:Pursuing_Rayan_Moore') AND e.original_name='Journal'
          ORDER BY s.quest_id''').fetchall()
        self.assertEqual(len(rows),2);self.assertNotEqual(rows[0][1],rows[1][1]);self.assertTrue(all(r[2] is None for r in rows))

    def test_medicine_reuses_two_steps_without_multiplying_potions(self):
        q='irowiki-classic:Medicine_Quest'
        repeats=[tuple(r) for r in self.db.execute('''SELECT a.step_order,b.step_order FROM step_edges e JOIN quest_steps a ON a.step_id=e.from_step
          JOIN quest_steps b ON b.step_id=e.to_step WHERE a.quest_id=? AND e.kind='repeat_actions_from' ORDER BY a.step_order,b.step_order''',(q,))]
        self.assertEqual(repeats,[(8,6),(8,7),(9,8),(10,9)])
        potions=[tuple(r) for r in self.db.execute('''SELECT s.step_order,i.operation,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND x.game_id=504''',(q,))]
        self.assertEqual(potions,[(3,'deliver',1)])
        actions=[r[0] for r in self.db.execute('''SELECT a.operation FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=6''',(q,))]
        self.assertEqual(actions,['approach'])
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],1)

    def test_friendship_exact_message_is_linked_to_delivery_step(self):
        q='irowiki-classic:Friendship_Quest'
        message=[r[0] for r in self.db.execute('''SELECT o.exact_text FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=5 ORDER BY d.dialogue_order,o.option_order''',(q,))]
        self.assertEqual(message,['Hopeless bastard!',"You're still a stubborn jerk!",'You owe me at least 3 lunches!','Not to mention an apology!','But who cares what you think?!',"I'm so goddamn happy without you!"])
        rows=self.db.execute('SELECT step_order,original_instruction,full_en FROM localized_steps WHERE quest_id=? AND step_order IN (4,5) ORDER BY step_order',(q,)).fetchall()
        self.assertIn(message[0],rows[0][1]);self.assertNotIn(message[0],rows[0][2]);self.assertIn(message[0],rows[1][2])
        self.assertEqual(self.db.execute('''SELECT count(*) FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=6''',(q,)).fetchone()[0],0)
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='maku_return_reply_unknown'",(q,)).fetchone())

    def test_friendship_overlap_and_two_reward_installments_remain_explicit(self):
        q='irowiki-classic:Friendship_Quest'
        grants=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          JOIN quest_steps s ON s.step_id=b.step_id WHERE r.quest_id=? ORDER BY s.step_order,b.branch_key''',(q,))]
        self.assertEqual(grants,[(n,v) for n in [12,14] for v in ['200000','400000','700000']])
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='base_exp' AND branch_id IS NULL",(q,)).fetchone()[0],0)
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='source_level_75_overlap'",(q,)).fetchone())
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='scripted_hp_loss_percent'",(q,)).fetchone()[0],'50')

    def test_multiple_reuse_targets_reject_duplicates_and_wrong_witness(self):
        q='irowiki-classic:Medicine_Quest'
        with contextlib.closing(sqlite3.connect(':memory:')) as db:
            self.db.backup(db);db.execute('''UPDATE step_edges SET to_step=(SELECT step_id FROM quest_steps WHERE quest_id=? AND step_order=5)
              WHERE kind='repeat_actions_from' AND from_step=(SELECT step_id FROM quest_steps WHERE quest_id=? AND step_order=8)
              AND to_step=(SELECT step_id FROM quest_steps WHERE quest_id=? AND step_order=7)''',(q,q,q))
            with self.assertRaisesRegex(ValueError,'invalid_action_reuse_target'):validate(db)
        folder=self.out/'invalid-multiple-reuse';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Medicine_Quest.json';data=json.loads(path.read_text());data['steps']['8']['action_reuse'].append(dict(data['steps']['8']['action_reuse'][0]));path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Duplicate action reuse target'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_regional_guides_have_source_pinned_mandatory_prerequisites(self):
        p='irowiki-classic:'
        for owner,target in [('Biological_Weapon_Quest','Medicine_Quest'),('Bruspetti_Quest','Friendship_Quest'),('Peace_for_Arunafeltz_Quest','Thor_Volcano_Base_Quest')]:
            with self.subTest(owner=owner):
                rows=self.db.execute('''SELECT r.related_id,e.method,e.step_id FROM quest_relations r JOIN evidence e USING(evidence_id)
                  WHERE r.quest_id=? AND r.kind='mandatory_prerequisite' ''',(p+owner,)).fetchall()
                self.assertEqual([tuple(r) for r in rows],[(p+target,'curated_single_prerequisite',None)])

    def test_biological_material_request_and_delivery_are_one_set(self):
        q='irowiki-classic:Biological_Weapon_Quest'
        expected={1003:5,7054:5,7053:5,619:1,971:3,935:20,507:1,510:1,511:1,509:1,508:1}
        for stage,operation in [(3,'collect'),(4,'deliver')]:
            facts=self.db.execute('''SELECT x.game_id,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
              JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=?''',(q,stage)).fetchall()
            self.assertEqual(len(facts),11);self.assertEqual({r[0]:r[1] for r in facts},expected)
            self.assertTrue(all(r[2]==operation for r in facts))
        totals=self.db.execute("SELECT count(*) FROM requirements WHERE quest_id=? AND kind='item_consumed_total' AND step_id IS NULL",(q,)).fetchone()[0]
        self.assertEqual(totals,11)
        witnesses=self.db.execute('''SELECT DISTINCT t.step_order FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN evidence_context c ON c.evidence_id=i.evidence_id JOIN evidence e ON e.evidence_id=c.supporting_id
          JOIN quest_steps t ON t.step_id=e.step_id WHERE s.quest_id=? AND s.step_order=4''',(q,)).fetchall()
        self.assertEqual([r[0] for r in witnesses],[3])

    def test_biological_medicine_split_unknown_and_exp_installments_exact(self):
        q='irowiki-classic:Biological_Weapon_Quest'
        grants=self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in grants],[(8,'500000'),(9,'700000')])
        medicine=self.db.execute('''SELECT s.step_order,i.item_id,x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id
          WHERE s.quest_id=? AND e.original_name='medicine' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in medicine],[7,8]);self.assertEqual(len({r[1] for r in medicine}),1)
        self.assertTrue(all(r[2] is None and r[3] is None for r in medicine))
        powder=self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN entities e ON e.entity_id=i.item_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND e.original_name='Red Plant Stem Powder' ''',(q,)).fetchone()
        self.assertEqual(tuple(powder),(None,None))

    def test_bruspetti_exact_topics_preserve_order_and_input_mode(self):
        q='irowiki-classic:Bruspetti_Quest'
        expected={11:[('Bruspetti','enter_text'),('Lighthalzen','ask_topic'),("Freya's Spring",'ask_topic')],
          12:[("Freya's Spring",'enter_text')],16:[('Lighthalzen','ask_topic'),('Recent Break-up','ask_topic'),("Freya's Spring",'ask_topic')]}
        for stage,values in expected.items():
            rows=self.db.execute('''SELECT o.exact_text,o.operation FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
              JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=? ORDER BY d.dialogue_order,o.option_order''',(q,stage)).fetchall()
            self.assertEqual([tuple(r) for r in rows],values)

    def test_bruspetti_knife_slots_and_diary_do_not_become_extra_items(self):
        q='irowiki-classic:Bruspetti_Quest'
        rows=self.db.execute('''SELECT s.step_order,e.original_name,i.quantity,i.operation,x.game_id FROM step_items i
          JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=i.item_id JOIN items x ON x.entity_id=i.item_id
          WHERE s.quest_id=? ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(15,"Bruspetti's Diary",None,'collect',None),(17,'Knife [3]',1,'consume',None)])
        book=self.db.execute('''SELECT e.entity_type FROM step_npcs n JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=n.npc_id WHERE s.quest_id=? AND s.step_order=10 AND e.original_name='Book' ''',(q,)).fetchone()
        self.assertEqual(book[0],'npc')
        hp=self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='scripted_hp_result_approx_percent'",(q,)).fetchone()
        self.assertEqual(hp[0],'50')
        self.assertEqual(self.db.execute("SELECT count(*) FROM requirements WHERE quest_id=? AND kind='minimum_hp'",(q,)).fetchone()[0],0)

    def test_bruspetti_level_75_conflict_is_retained_not_repaired(self):
        q='irowiki-classic:Bruspetti_Quest'
        grants=self.db.execute('''SELECT b.branch_key,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? ORDER BY b.branch_key''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in grants],[('level_50_75','450000'),('level_75_90','850000'),('level_91_99','1300000')])
        self.assertIn('Level 76-90',self.db.execute('SELECT rewards_original FROM quests WHERE quest_id=?',(q,)).fetchone()[0])
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='source_level_75_header_conflict'",(q,)).fetchone())
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND branch_id IS NULL",(q,)).fetchone()[0],0)

    def test_peace_merchants_distinct_and_zhed_shared_by_source_identity(self):
        q='irowiki-classic:Peace_for_Arunafeltz_Quest'
        ids=[]
        for stage in [1,3,4]:
            rows=self.db.execute('''SELECT npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
              JOIN entities e ON e.entity_id=n.npc_id WHERE s.quest_id=? AND s.step_order=? AND e.original_name='Foreign Merchant' ''',(q,stage)).fetchall()
            self.assertEqual(len(rows),1);ids.append(rows[0][0])
        self.assertEqual(ids[0],ids[2]);self.assertNotEqual(ids[0],ids[1])
        for stage,expected in [(1,ids[0]),(3,ids[1]),(4,ids[0])]:
            mentions=self.db.execute('''SELECT entity_id FROM description_mentions JOIN quest_steps USING(step_id)
              WHERE quest_id=? AND step_order=? AND surface='Foreign Merchant' ''',(q,stage)).fetchall()
            self.assertTrue(mentions);self.assertTrue(all(r[0]==expected for r in mentions))
        shared=self.db.execute('''SELECT DISTINCT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=n.npc_id WHERE s.quest_id IN (?,?) AND e.original_name='High Priest Zhed' ''',(q,'irowiki-classic:Rachel_Sanctuary_Quest')).fetchall()
        self.assertEqual(len(shared),1)

    def test_peace_notes_and_original_forged_files_have_distinct_identities(self):
        q='irowiki-classic:Peace_for_Arunafeltz_Quest'
        facts=self.db.execute('''SELECT s.step_order,i.item_id,x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id
          WHERE s.quest_id=? AND s.step_order IN (9,10,13,14,15,16) AND e.original_name IN ('note','File') ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in facts],[9,10,13,14,15,16])
        ids=[r[1] for r in facts];self.assertEqual(ids[0],ids[1]);self.assertEqual(ids[2],ids[3]);self.assertEqual(ids[4],ids[5]);self.assertEqual(len(set(ids)),3)
        self.assertTrue(all(r[2] is None and r[3] is None for r in facts))
        for stage,index,surface in [(9,0,'note'),(10,1,'note'),(13,2,'note'),(14,3,'File'),(15,4,'File'),(16,5,'File')]:
            mentions=self.db.execute('''SELECT entity_id FROM description_mentions JOIN quest_steps USING(step_id)
              WHERE quest_id=? AND step_order=? AND field='full_en' AND surface=?''',(q,stage,surface)).fetchall()
            self.assertTrue(mentions);self.assertTrue(all(r[0]==ids[index] for r in mentions))
        older={r[0] for r in self.db.execute("SELECT entity_id FROM entities WHERE original_name='File Folder'")}
        self.assertFalse(set(ids)&older)

    def test_peace_unknown_exp_split_gem_spelling_and_unqualified_map(self):
        q='irowiki-classic:Peace_for_Arunafeltz_Quest'
        grants=self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in grants],[(20,None),(21,None)])
        total=self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp_total'",(q,)).fetchone()[0]
        self.assertEqual(total,'2500000')
        gem=self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=15 AND i.operation='deliver' ''',(q,)).fetchone()
        self.assertEqual(tuple(gem),(725,1))
        typo=self.db.execute('''SELECT x.game_id FROM entities e JOIN items x USING(entity_id) JOIN description_mentions m USING(entity_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=15 AND e.original_name='Sardonx' LIMIT 1''',(q,)).fetchone()
        self.assertIsNone(typo[0])
        point=self.db.execute('''SELECT l.map_id,l.x,l.y FROM step_locations a JOIN locations l USING(location_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=14''',(q,)).fetchone()
        self.assertEqual(tuple(point),(None,139,355))

    def test_shadow_guard_paths_are_alternatives_and_sure_is_wrong(self):
        q='irowiki-classic:Shadow_Quest_(Rekenber_Job_Quest)'
        rows=self.db.execute('''SELECT b.branch_key,o.exact_text,o.correct FROM dialogue_options o JOIN dialogues d USING(dialogue_id)
          JOIN step_branches b ON b.branch_id=d.branch_id JOIN quest_steps s ON s.step_id=b.step_id
          WHERE s.quest_id=? ORDER BY b.branch_key,o.option_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[('business_then_leave','I have some business',1),('business_then_leave',"I'll be back later.",1),('leave_immediately',"Whoa, I'm leaving!",1)])
        groups=self.db.execute('''SELECT DISTINCT b.choice_group FROM step_branches b JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchall()
        self.assertEqual([r[0] for r in groups],['lab_guard_response'])
        wrong=self.db.execute('''SELECT e.original_name,o.exact_text,o.correct FROM dialogue_options o JOIN dialogues d USING(dialogue_id)
          JOIN entities e ON e.entity_id=d.npc_id JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND d.branch_id IS NULL''',(q,)).fetchall()
        self.assertIn(('Kazien','Sure',0),[tuple(r) for r in wrong])

    def test_shadow_work_gate_and_retry_are_not_quest_log_or_spawn_rules(self):
        q='irowiki-classic:Shadow_Quest_(Rekenber_Job_Quest)'
        requirements=self.db.execute('''SELECT s.step_order,r.kind,r.amount FROM requirements r JOIN quest_steps s USING(step_id)
          WHERE r.quest_id=? ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in requirements],[(1,'minimum_base_level_for_jobs','70'),(5,'missing_npc_wait_minutes','5-10')])
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='retry_and_missing_npc_timers_distinct'",(q,)).fetchone())
        trigger=self.db.execute('''SELECT a.operation FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=17''',(q,)).fetchone()[0]
        self.assertEqual(trigger,'walk_to_flashback')
        labels=self.db.execute('''SELECT DISTINCT e.entity_id FROM entities e JOIN step_npcs n ON n.npc_id=e.entity_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND e.original_name IN ('Kazien','Kaizen')''',(q,)).fetchall()
        self.assertEqual(len(labels),2)

    def test_shadow_two_attacks_three_exp_installments_and_single_potion(self):
        q='irowiki-classic:Shadow_Quest_(Rekenber_Job_Quest)'
        attacks=self.db.execute('''SELECT s.step_order,m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in attacks],[(9,2,1592),(13,3,1592)])
        grants=self.db.execute('''SELECT s.step_order,r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? AND r.kind='base_exp' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in grants],[(8,'400000'),(12,'450000'),(16,'550000')])
        potions=self.db.execute('''SELECT s.step_order,x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=?''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in potions],[(13,504,'1')])
        operations=[r[0] for r in self.db.execute('''SELECT a.operation FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=13 ORDER BY a.action_order''',(q,))]
        self.assertEqual(operations,['talk','kill','inspect','receive'])

    def test_lovers_two_independent_six_band_exp_installments(self):
        q='irowiki-classic:Lovers_Quest'
        bands=[('level_1_40','3712'),('level_41_60','7425'),('level_61_70','14850'),('level_71_80','29700'),('level_81_90','59400'),('level_91_99','118800')]
        for stage in [4,9]:
            grants=self.db.execute('''SELECT b.branch_key,r.amount,b.choice_group FROM rewards r JOIN step_branches b USING(branch_id)
              JOIN quest_steps s ON s.step_id=b.step_id WHERE r.quest_id=? AND s.step_order=? ORDER BY b.branch_key''',(q,stage)).fetchall()
            self.assertEqual([tuple(r) for r in grants],[(key,amount,'level_reward_'+str(stage)) for key,amount in bands])
        totals=self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp_total'",(q,)).fetchall()
        self.assertEqual({int(r[0]) for r in totals},{int(amount)*2 for _,amount in bands})
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='base_exp' AND branch_id IS NULL",(q,)).fetchone()[0],0)

    def test_lovers_coal_batches_and_purchase_delivery_share_one_cost(self):
        q='irowiki-classic:Lovers_Quest'
        coal=self.db.execute('''SELECT s.step_order,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND x.game_id=1003''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in coal],[(7,40,'deliver')])
        batches=self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('delivery_batch_count','items_per_delivery_batch') ORDER BY kind",(q,)).fetchall()
        self.assertEqual([tuple(r) for r in batches],[('delivery_batch_count','4'),('items_per_delivery_batch','10')])
        suits=self.db.execute('''SELECT x.game_id,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND x.game_id=7170 ORDER BY i.operation''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in suits],[(7170,1,'buy'),(7170,1,'deliver')])
        flower_rewards=self.db.execute('''SELECT count(*) FROM rewards r JOIN items x ON x.entity_id=r.item_id WHERE r.quest_id=? AND x.game_id=712''',(q,)).fetchone()[0]
        self.assertEqual(flower_rewards,0)
        ore=self.db.execute("SELECT amount,item_id FROM rewards WHERE quest_id=? AND kind='random_material_category'",(q,)).fetchone()
        self.assertIsNone(ore[0]);self.assertIsNone(self.db.execute('SELECT game_id FROM items WHERE entity_id=?',(ore[1],)).fetchone()[0])

    def test_lovers_character_name_is_dynamic_but_sender_answer_literal(self):
        q='irowiki-classic:Lovers_Quest'
        options=self.db.execute('''SELECT s.step_order,o.exact_text,o.operation FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order,o.option_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in options],[(7,'Klitzer','enter_text')])
        names=self.db.execute('''SELECT s.step_order FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND a.operation='enter_character_name' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in names],[5,6])
        sequence=self.db.execute('''SELECT e.original_name FROM step_actions a JOIN entities e ON e.entity_id=a.entity_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=6 ORDER BY a.action_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in sequence],['Klitzer','Kaijeta','Klitzer','Calla'])

    def test_juno_bingo_five_total_without_victory_and_shared_captain(self):
        q='irowiki-classic:Juno_Remedy_Quest'
        sizes=self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('total_players_for_bingo','additional_players_for_bingo') ORDER BY kind",(q,)).fetchall()
        self.assertEqual([tuple(r) for r in sizes],[('additional_players_for_bingo','4'),('total_players_for_bingo','5')])
        rule=self.db.execute('''SELECT a.condition_text FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND a.operation='play_bingo' ''',(q,)).fetchone()[0]
        self.assertIn('victory is explicitly unnecessary',rule)
        captains=self.db.execute('''SELECT DISTINCT npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE n.role='talk' AND ((s.quest_id=? AND s.step_order=4) OR (s.quest_id='irowiki-classic:Heart_Fragment_Quest' AND s.step_order=9))''',(q,)).fetchall()
        self.assertEqual(len(captains),1)
        label=self.db.execute('''SELECT entity_id FROM description_mentions JOIN quest_steps USING(step_id)
          WHERE quest_id=? AND step_order=4 AND field='full_en' AND surface='Airship Captain' ''',(q,)).fetchone()[0]
        self.assertEqual(label,captains[0][0])

    def test_juno_scene_context_rune_total_unknown_remedy_and_header_rewards(self):
        q='irowiki-classic:Juno_Remedy_Quest'
        points=self.db.execute('''SELECT l.x,l.y,a.location_order,e.status FROM step_locations a JOIN locations l USING(location_id)
          JOIN evidence e ON e.evidence_id=a.evidence_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=8 ORDER BY a.location_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in points],[(135,125,1,'source_literal'),(114,147,2,'context_inferred')])
        runes=self.db.execute('''SELECT s.step_order,i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND x.game_id=7511''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in runes],[(11,5,'deliver')])
        remedy=self.db.execute('''SELECT s.step_order,i.item_id,x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND e.original_name='remedy' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in remedy],[11,12]);self.assertEqual(len({r[1] for r in remedy}),1)
        self.assertTrue(all(r[2] is None and r[3] is None for r in remedy))
        rewards=self.db.execute('''SELECT r.kind,r.amount,s.step_order,e.method FROM rewards r JOIN evidence e USING(evidence_id)
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? ORDER BY r.kind''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rewards],[('base_exp','900000',12,'curated_rewards'),('job_exp','600000',12,'curated_rewards')])

    def test_continental_material_set_accessory_choice_and_skin_eligibility(self):
        q='irowiki-classic:Continental_Guard_Quest_(Resurrection_of_Satan_Morroc)'
        rows=self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=4 ORDER BY x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(7098,30),(7561,50)])
        rows=self.db.execute('''SELECT x.game_id,r.amount,b.choice_group FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_branches b USING(branch_id) WHERE r.quest_id=? ORDER BY x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(2730,'1','accessory_choice'),(2731,'1','accessory_choice'),(2732,'1','accessory_choice')])
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],'2000000')
        self.assertEqual(self.db.execute('''SELECT m.required FROM step_monsters m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=7 AND m.operation='mention' ''',(q,)).fetchone()[0],0)
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM requirements WHERE quest_id=? AND kind='monster_defeat_event'",(q,)).fetchone())

    def test_continental_combo_tables_and_classic_notice_have_pinned_additions(self):
        q='irowiki-classic:Continental_Guard_Quest_(Resurrection_of_Satan_Morroc)'
        rows=self.db.execute('''SELECT s.step_order,count(*) FROM step_source_additions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? GROUP BY s.step_order ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(1,1),(8,7)])
        row=self.db.execute('SELECT original_instruction,full_en FROM localized_steps WHERE quest_id=? AND step_order=8',(q,)).fetchone()
        self.assertNotIn('Recharge Time: 20 seconds',row[0]);self.assertIn('Recharge Time: 20 seconds',row[1]);self.assertIn('Dragon Fear 10',row[1])
        self.assertEqual({int(r[0]) for r in self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='boss_combo_hp_threshold_percent'",(q,))},{80,60,50,40,30,10,5})
        notice=self.db.execute('SELECT full_en FROM localized_steps WHERE quest_id=? AND step_order=1',(q,)).fetchone()[0]
        self.assertIn("Dandelion's_Request",notice);self.assertIn('currently not implemented',notice)

    def test_continental_organization_is_not_a_false_npc_span(self):
        q='irowiki-classic:Continental_Guard_Quest_(Resurrection_of_Satan_Morroc)'
        count=self.db.execute('''SELECT count(*) FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (1,3) AND m.surface='Continental Guard' ''',(q,)).fetchone()[0]
        self.assertEqual(count,0)
        count=self.db.execute('''SELECT count(*) FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=5 AND m.surface='Continental Guard' AND m.entity_type='npc' ''',(q,)).fetchone()[0]
        self.assertEqual(count,4)
        rows=self.db.execute('''SELECT DISTINCT entity_type FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=4 AND m.surface='Continental Guard Paper' ''',(q,)).fetchall()
        self.assertEqual([r[0] for r in rows],['item'])

    def test_nidhoggur_source_entry_and_collective_guardians_are_variant_scoped(self):
        q="irowiki-classic:Nidhoggur's_Nest"
        self.assertIn('Quest Prerequisite(s): None',self.db.execute('SELECT requirements_original FROM quests WHERE quest_id=?',(q,)).fetchone()[0])
        self.assertEqual(self.db.execute("SELECT count(*) FROM quest_relations WHERE quest_id=? AND kind='mandatory_prerequisite'",(q,)).fetchone()[0],0)
        name=self.db.execute('''SELECT e.original_name FROM step_npcs n JOIN entities e ON e.entity_id=n.npc_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=1''',(q,)).fetchone()[0]
        self.assertEqual(name,'Matt Roy Shka')
        rows=self.db.execute('''SELECT m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=4 AND m.operation='kill' ''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(12,None)])

    def test_nidhoggur_side_rooms_do_not_assert_uncertain_hp_or_fixed_totals(self):
        q="irowiki-classic:Nidhoggur's_Nest"
        keys=['instance_duration_hours','side_room_interval_minutes_approx','side_room_effect_duration_seconds','instance_reentry_cooldown_days']
        facts=dict(self.db.execute('SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN (?,?,?,?)',(q,*keys)).fetchall())
        self.assertEqual(facts,dict(zip(keys,['2.5','3','30','3'])))
        rows=self.db.execute('''SELECT b.branch_key,b.choice_group FROM step_branches b JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? ORDER BY b.branch_key''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(x,'current_side_room') for x in ['northeast','northwest','southeast','southwest']])
        self.assertIsNone(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_uncertain_hp_loss'",(q,)).fetchone()[0])
        self.assertEqual(self.db.execute("SELECT count(*) FROM requirements WHERE quest_id=? AND kind='hp_loss_percent' AND amount='25'",(q,)).fetchone()[0],0)
        rows=self.db.execute('''SELECT e.original_name,m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN entities e ON e.entity_id=m.monster_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=8 AND m.operation='kill' ORDER BY e.original_name''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[('Phylla',2,None),('Ryncho',3,None)])

    def test_shrine_population_and_leader_roles_are_not_per_player_costs(self):
        q='irowiki-classic:Sealed_Shrine_Quest'
        rows=self.db.execute('''SELECT m.operation,m.required,sum(m.quantity),count(*) FROM step_monsters m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=3 GROUP BY m.operation,m.required''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[('mention',None,290,10)])
        self.assertEqual(self.db.execute("SELECT count(*) FROM requirements WHERE quest_id=? AND kind='monster_population'",(q,)).fetchone()[0],10)
        row=self.db.execute('''SELECT i.quantity,i.operation FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=6 AND x.game_id=6001''',(q,)).fetchone()
        self.assertEqual(tuple(row),(10,'collect'))
        self.assertIn('not every first-time entrant',self.db.execute("SELECT condition_text FROM requirements WHERE quest_id=? AND kind='token_required_roles'",(q,)).fetchone()[0])
        facts=dict(self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('initial_entry_window_minutes','return_after_exit_window_minutes','instance_duration_hours','instance_creation_cooldown_hours','leader_collection_time_limit_hours','outer_area_trigger_reset_minutes')",(q,)).fetchall())
        self.assertEqual(facts,{'initial_entry_window_minutes':'5','return_after_exit_window_minutes':'5','instance_duration_hours':'2','instance_creation_cooldown_hours':'12','leader_collection_time_limit_hours':'1','outer_area_trigger_reset_minutes':'5'})

    def test_shrine_recipe_is_optional_and_output_id_not_guessed(self):
        q='irowiki-classic:Sealed_Shrine_Quest'
        route=self.db.execute('SELECT route_id,kind,choice_group FROM quest_routes WHERE quest_id=?',(q,)).fetchone()
        self.assertEqual(tuple(route[1:]),('optional',None))
        rows=self.db.execute('SELECT s.step_order FROM step_routes r JOIN quest_steps s USING(step_id) WHERE r.route_id=? ORDER BY r.route_order',(route[0],)).fetchall()
        self.assertEqual([r[0] for r in rows],[11,12])
        rows=self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=12 ORDER BY x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(2256,1),(6004,1),(7798,50),(7799,30)])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='crafting_zeny'",(q,)).fetchone()[0],'990000')
        row=self.db.execute('''SELECT e.original_name,x.game_id FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN entities e ON e.entity_id=r.item_id WHERE r.quest_id=? AND r.kind='crafted_item' ''',(q,)).fetchone()
        self.assertEqual(tuple(row),('Giant Majestic Goat',None))

    def test_orc_instance_map_order_and_context_evidence(self):
        q='irowiki-classic:Orc_Memory_Dungeon'
        rows=self.db.execute('''SELECT DISTINCT m.map_code,m.known_in_reference FROM step_locations a JOIN quest_steps s USING(step_id)
          JOIN locations l USING(location_id) JOIN maps m ON m.entity_id=l.map_id WHERE s.quest_id=? AND m.map_code LIKE '%@%' ORDER BY m.map_code''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[('1@orcs',0),('2@orcs',0)])
        for stage,first in [(10,(27,164)),(11,(35,92)),(12,(143,144))]:
            rows=self.db.execute('''SELECT l.x,l.y,a.location_order,a.role,e.status FROM step_locations a JOIN quest_steps s USING(step_id)
              JOIN locations l USING(location_id) JOIN evidence e ON e.evidence_id=a.evidence_id WHERE s.quest_id=? AND s.step_order=? ORDER BY a.location_order''',(q,stage)).fetchall()
            self.assertEqual(len(rows),6);self.assertEqual(tuple(rows[0][:2]),first)
            self.assertEqual([r[2] for r in rows],[1,2,3,4,5,6]);self.assertTrue(all(r[4]=='context_inferred' for r in rows))
            self.assertEqual([r[3] for r in rows],['brazier_sequence']*4+['guard_spawn','opened_portal'])

    def test_orc_targets_lookalikes_cleanup_and_repeat_cooldown_are_distinct(self):
        q='irowiki-classic:Orc_Memory_Dungeon'
        rows=self.db.execute('''SELECT s.step_order,m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order IN (5,6,7,8) AND m.required=1 ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(i,1,None) for i in [5,6,7,8]])
        row=self.db.execute('''SELECT m.operation,m.required FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=4 AND x.game_id=1023''',(q,)).fetchone()
        self.assertEqual(tuple(row),('mention',0))
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM requirements WHERE quest_id=? AND kind='monster_appearance_reference'",(q,)).fetchone())
        facts=dict(self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('instance_duration_hours','instance_reentry_cooldown_hours','base_level_range')",(q,)).fetchall())
        self.assertEqual(facts,{'instance_duration_hours':'2','instance_reentry_cooldown_hours':'2','base_level_range':'30-80'})
        self.assertEqual(self.db.execute('SELECT count(*) FROM rewards WHERE quest_id=?',(q,)).fetchone()[0],0)

    def test_unsupported_curated_operations_fail_without_replacing_valid_export(self):
        q='irowiki-classic:Shadow_Quest_(Rekenber_Job_Quest)'
        cargo=self.db.execute('''SELECT i.operation FROM step_items i JOIN entities e ON e.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=13 AND e.original_name='goods' ''',(q,)).fetchone()
        self.assertEqual(cargo[0],'mention')
        folder=self.out/'unsupported-operation';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Shadow_Quest_(Rekenber_Job_Quest).json';data=json.loads(path.read_text())
        data['steps']['13']['items'][0]['operation']='unsupported-inspect'
        path.write_text(json.dumps(data));before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Unsupported curated item operation'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_muff_requested_gems_and_actual_material_consumption_are_distinct(self):
        q="irowiki-classic:Muff's_Loan_Quest"
        rows=self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=7 ORDER BY x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(716,5),(723,2),(999,5),(1002,10),(7317,20)])
        self.assertEqual(self.db.execute("SELECT amount FROM requirements WHERE quest_id=? AND kind='source_requested_item_count'",(q,)).fetchone()[0],'10')
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM requirements WHERE quest_id=? AND kind='incomplete_then_complete_material_check'",(q,)).fetchone())
        actions=self.db.execute('''SELECT a.operation FROM step_actions a JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=7 ORDER BY a.action_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in actions],['talk_with_incomplete_set','talk_with_complete_set'])

    def test_muff_switch_table_is_conditioned_mapping_restored_from_html(self):
        q="irowiki-classic:Muff's_Loan_Quest"
        rows=self.db.execute('''SELECT d.original_text,o.exact_text,o.condition_text FROM dialogues d JOIN dialogue_options o USING(dialogue_id)
          JOIN step_dialogues sd USING(dialogue_id) JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=10 ORDER BY sd.dialogue_order''',(q,)).fetchall()
        expected=[('Vibrating Weakly','Red'),('Vibrating a Bit More Strongly','Blue'),('Vibrations are a Bit More Stable','Yellow'),('Vibrating Fairly Strongly','Green')]
        self.assertEqual([(r[0],r[1]) for r in rows],expected)
        self.assertTrue(all(state in r[2] for (state,_),r in zip(expected,rows)))
        original,full=self.db.execute('SELECT original_instruction,full_en FROM localized_steps WHERE quest_id=? AND step_order=10',(q,)).fetchone()
        self.assertNotIn('Vibrating Weakly',original);self.assertIn('Vibrating Weakly Red',full)
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_source_additions a JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchone()[0],1)

    def test_muff_exact_theory_answer_wallet_timing_and_final_rewards(self):
        q="irowiki-classic:Muff's_Loan_Quest"
        answer=self.db.execute('''SELECT o.exact_text FROM dialogue_options o JOIN step_dialogues sd USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=5''',(q,)).fetchone()[0]
        self.assertEqual(answer,'3 Centrifuge Wavelength Theory')
        wallet=self.db.execute('''SELECT s.step_order,x.game_id,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND e.original_name='wallet' AND i.operation='collect' ''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in wallet],[(3,None,None)])
        rewards=self.db.execute('''SELECT s.step_order,x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? ORDER BY x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rewards],[(14,603,'1'),(14,608,'4')])

    def test_broken_diamond_prerequisite_shared_muff_and_distinct_investigators(self):
        q='irowiki-classic:Broken_Diamond_Quest';p="irowiki-classic:Muff's_Loan_Quest"
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM quest_relations WHERE quest_id=? AND related_id=? AND kind='mandatory_prerequisite'",(q,p)).fetchone())
        shared=self.db.execute('''SELECT DISTINCT n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          JOIN entities e ON e.entity_id=n.npc_id WHERE s.quest_id IN (?,?) AND e.original_name='Muff' ''',(q,p)).fetchall()
        self.assertEqual(len(shared),1)
        actors=self.db.execute('''SELECT s.step_order,n.npc_id FROM step_npcs n JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order IN (8,9) AND n.role='talk' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in actors],[8,9]);self.assertNotEqual(actors[0][1],actors[1][1])

    def test_broken_diamond_one_material_set_and_one_information_fee(self):
        q='irowiki-classic:Broken_Diamond_Quest'
        materials=self.db.execute('''SELECT x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=6 AND i.operation='deliver' ORDER BY x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in materials],[(713,5),(7043,20),(7054,10),(7194,10)])
        fees=self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('zeny_payment','source_zeny_summary') ORDER BY kind",(q,)).fetchall()
        self.assertEqual([tuple(r) for r in fees],[('source_zeny_summary','10000'),('zeny_payment','10000')])
        switches=self.db.execute('''SELECT l.x,l.y,a.location_order FROM step_locations a JOIN locations l USING(location_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=11 ORDER BY a.location_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in switches],[(69,196,1),(80,189,2)])

    def test_broken_diamond_pearl_is_intermediate_and_carat_label_not_quantity(self):
        q='irowiki-classic:Broken_Diamond_Quest'
        rewards=self.db.execute('''SELECT s.step_order,x.game_id,r.amount FROM rewards r JOIN items x ON x.entity_id=r.item_id
          JOIN step_rewards sr USING(reward_id) JOIN quest_steps s ON s.step_id=sr.step_id WHERE r.quest_id=? ORDER BY s.step_order,x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rewards],[(3,722,'1'),(13,603,'1'),(13,732,'2')])
        gems=self.db.execute('''SELECT s.step_order,i.item_id,x.game_id FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND e.original_name='Diamond' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in gems],[12,13]);self.assertEqual(gems[0][1],gems[1][1]);self.assertTrue(all(r[2] is None for r in gems))

    def test_zgang_exact_passwords_belong_to_input_stages(self):
        q='irowiki-classic:Z-Gang_Quest'
        rows=self.db.execute('''SELECT s.step_order,o.exact_text,o.operation FROM dialogue_options o JOIN step_dialogues sd USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order,sd.dialogue_order,o.option_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[(4,'The dawn is yet to come.','enter_text'),(14,'Weii arr prowd Z G gna','enter_text'),(14,'Aynoen hwo sspotp uys','enter_text'),(14,'wlil eb kckide on htier ssa!','enter_text')])
        raw,full=self.db.execute('SELECT original_instruction,full_en FROM localized_steps WHERE quest_id=? AND step_order=14',(q,)).fetchone()
        self.assertNotIn('Weii arr',raw);self.assertIn('Weii arr',full)

    def test_zgang_notes_and_ahn_characters_are_distinct_without_invented_inventory(self):
        q='irowiki-classic:Z-Gang_Quest'
        notes=self.db.execute('''SELECT s.step_order,i.item_id,x.game_id,i.operation FROM step_items i JOIN quest_steps s USING(step_id)
          JOIN items x ON x.entity_id=i.item_id JOIN entities e ON e.entity_id=i.item_id WHERE s.quest_id=? AND e.original_name='note' ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([r[0] for r in notes],[8,10,11]);self.assertNotEqual(notes[0][1],notes[1][1]);self.assertEqual(notes[1][1],notes[2][1]);self.assertTrue(all(r[2] is None and r[3]=='mention' for r in notes))
        ahns=self.db.execute('''SELECT DISTINCT e.entity_id FROM entities e JOIN step_npcs n ON n.npc_id=e.entity_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND e.original_name IN ('Moonho Ahn','Gooho Ahn')''',(q,)).fetchall()
        self.assertEqual(len(ahns),2)
        self.assertEqual(dict(self.db.execute("SELECT kind,amount FROM requirements WHERE quest_id=? AND kind IN ('rounds_to_win','rounds_in_match')",(q,))),{'rounds_to_win':'2','rounds_in_match':'3'})

    def test_zgang_material_quantities_first_wave_and_single_exp_reward(self):
        q='irowiki-classic:Z-Gang_Quest'
        materials=self.db.execute('''SELECT s.step_order,x.game_id,i.quantity FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND i.operation='deliver' ORDER BY s.step_order,x.game_id''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in materials],[(3,12049,5),(11,970,2),(11,7035,1),(11,7097,10),(11,7098,20)])
        monsters=self.db.execute('''SELECT s.step_order,m.quantity,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order,m.quantity''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in monsters],[(15,None,None),(15,1,None)])
        self.assertEqual([tuple(r) for r in self.db.execute("SELECT kind,amount FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,))],[('base_exp','1000000')])
        self.assertIsNotNone(self.db.execute("SELECT 1 FROM review_queue WHERE quest_id=? AND reason='wave_plan_not_spawn_or_loot'",(q,)).fetchone())

    def test_reference_index_is_excluded_with_pinned_evidence(self):
        key='irowiki-classic:Access_Quests'
        self.assertIsNone(self.db.execute('SELECT 1 FROM quests WHERE quest_id=?',(key,)).fetchone())
        with contextlib.closing(sqlite3.connect(self.source)) as source:
            original=source.execute("SELECT p.id,count(*) FROM wiki_pages p JOIN wiki_steps s ON s.page_id=p.id WHERE p.title='Access Quests' GROUP BY p.id").fetchone()
            self.assertEqual(original[1],3)
            data=snapshots();result=select_pages(source,data)[original[0]]
            self.assertEqual(result['state'],'excluded_reference')
            self.assertIn('following table lists',result['evidence_text'])
            data['Access_Quests']=dict(data['Access_Quests'],sha256='stale')
            with self.assertRaisesRegex(ValueError,'Selection review source changed'):select_pages(source,data)

    def test_odin_alternative_routes_do_not_double_deliveries_or_boxes(self):
        q="irowiki-classic:Odin's_Temple_Excavation_Quest"
        routes=self.db.execute('SELECT route_id,route_key,kind,choice_group FROM quest_routes WHERE quest_id=? ORDER BY route_key',(q,)).fetchall()
        self.assertEqual([(r[1],r[2],r[3]) for r in routes],[('alex','alternative','initial_expedition'),('laura','alternative','initial_expedition')])
        for route,expected in zip(routes,[[4,5,6,7],[8,9,10,11,12]]):
            steps=[r[0] for r in self.db.execute('SELECT s.step_order FROM step_routes m JOIN quest_steps s USING(step_id) WHERE m.route_id=? ORDER BY m.route_order',(route[0],))]
            self.assertEqual(steps,expected)
            delivered=self.db.execute("SELECT sum(i.quantity) FROM step_items i JOIN step_routes m USING(step_id) WHERE m.route_id=? AND i.operation='deliver'",(route[0],)).fetchone()[0]
            self.assertEqual(delivered,5)
            boxes=self.db.execute('''SELECT r.amount FROM rewards r JOIN step_rewards sr USING(reward_id)
              JOIN step_routes m ON m.step_id=sr.step_id JOIN entities e ON e.entity_id=r.item_id
              WHERE m.route_id=? AND e.original_name='Old Blue Box' ''',(route[0],)).fetchall()
            self.assertEqual([r[0] for r in boxes],['1'])
        self.assertEqual(self.db.execute('''SELECT count(*) FROM step_routes m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order>=13''',(q,)).fetchone()[0],0)

    def test_ice_optional_summon_sale_and_weapon_slots(self):
        q='irowiki-classic:Ice_Necklace_Quest'
        optional=[tuple(r) for r in self.db.execute('''SELECT s.step_order,r.kind,r.choice_group FROM quest_routes r
          JOIN step_routes m USING(route_id) JOIN quest_steps s USING(step_id) WHERE r.quest_id=? ORDER BY s.step_order''',(q,))]
        self.assertEqual(optional,[(6,'optional',None),(7,'optional',None)])
        hammer=self.db.execute('''SELECT i.quantity,e.original_name FROM step_items i JOIN items x ON x.entity_id=i.item_id
          JOIN entities e ON e.entity_id=x.entity_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND x.game_id=1354''',(q,)).fetchone()
        self.assertEqual(tuple(hammer),(1,'Hammer'))
        boss=self.db.execute('''SELECT m.operation,m.required,x.game_id FROM step_monsters m JOIN monsters x ON x.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=?''',(q,)).fetchone()
        self.assertEqual(tuple(boss),('mention',0,1779))
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],1)
        self.assertEqual(self.db.execute("SELECT amount FROM rewards WHERE quest_id=? AND kind='zeny_per_item'",(q,)).fetchone()[0],'375')

    def test_cursed_rewards_are_four_level_bands(self):
        q='irowiki-classic:Cursed_Spirit_Quest'
        bands=self.db.execute('''SELECT b.branch_key,r.kind,r.amount FROM rewards r JOIN step_branches b USING(branch_id)
          WHERE r.quest_id=? ORDER BY b.branch_key,r.kind''',(q,)).fetchall()
        expected=[]
        for key,base,job in [('level_60_69','800000','300000'),('level_70_79','1000000','500000'),('level_80_89','1500000','800000'),('level_90_99','2000000','1000000')]:
            expected.extend([(key,'base_exp',base),(key,'job_exp',job)])
        self.assertEqual([tuple(r) for r in bands],expected)
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind IN ('base_exp','job_exp') AND branch_id IS NULL",(q,)).fetchone()[0],0)

    def test_ordered_triggers_alternative_points_and_disguise(self):
        q='irowiki-classic:Cursed_Spirit_Quest'
        route=self.db.execute('''SELECT m.map_code,l.x,l.y,sl.role FROM step_locations sl JOIN locations l USING(location_id)
          JOIN maps m ON m.entity_id=l.map_id JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=3 ORDER BY sl.location_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in route],[('lighthalzen',344,278,'trigger'),('lighthalzen',344,229,'entrance'),('lhz_in03',178,22,'trigger'),('lighthalzen',295,227,'trigger'),('lighthalzen',364,315,'trigger')])
        points=self.db.execute('''SELECT sl.location_order,sl.condition_text FROM step_locations sl JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=16''',("irowiki-classic:Odin's_Temple_Excavation_Quest",)).fetchall()
        self.assertEqual(len(points),5);self.assertTrue(all(r[0] is None and 'Alternative trigger' in r[1] for r in points))
        disguise=self.db.execute('''SELECT e.original_name,i.operation,i.quantity,i.condition_text FROM step_items i
          JOIN entities e ON e.entity_id=i.item_id JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=10 ORDER BY e.original_name''',(q,)).fetchall()
        self.assertEqual([(r[0],r[1],r[2]) for r in disguise],[('Geek Glasses','use',1),('Grandpa Beard','use',1)])
        self.assertTrue(all('not consumed' in r[3] for r in disguise))
        reps=self.db.execute('''SELECT DISTINCT m.entity_id FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND m.surface='Representative' AND m.field='full_en' ''',(q,)).fetchall()
        self.assertEqual(len(reps),2)

    def test_route_corruption_and_wrong_source_heading_are_rejected(self):
        cases=[
          ("UPDATE step_routes SET step_id=(SELECT step_id FROM quest_steps WHERE quest_id='irowiki-classic:Biolabs_Entrance_Quest' AND step_order=22) WHERE (step_id,route_id)=(SELECT step_id,route_id FROM step_routes LIMIT 1)",'route_membership_wrong_quest_or_step'),
          ("UPDATE evidence SET step_id=NULL WHERE method='source_route_membership'",'route_membership_wrong_quest_or_step'),
          ("UPDATE step_routes SET route_order=route_order+20",'route_order_gap'),
        ]
        for sql,error in cases:
            with self.subTest(error=error),contextlib.closing(sqlite3.connect(':memory:')) as db:
                self.db.backup(db);db.execute(sql)
                with self.assertRaisesRegex(ValueError,error):validate(db)
        folder=self.out/'invalid-route';shutil.copytree(ROOT/'curation',folder)
        path=folder/"Odin's_Temple_Excavation_Quest.json";data=json.loads(path.read_text())
        data['routes'][0]['steps'].append(13);path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Route membership contradicts source heading'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_human_reviewed_routes_cannot_be_silently_replaced(self):
        folder=self.out/'human-route';folder.mkdir()
        target=folder/'quest_semantic.sqlite';shutil.copyfile(self.out/'quest_semantic.sqlite',target)
        with contextlib.closing(sqlite3.connect(target)) as db:
            db.execute("UPDATE quest_routes SET review_state='human_reviewed'");db.commit()
        before=target.read_bytes()
        with self.assertRaisesRegex(ValueError,'Human-reviewed quest routes'):build(self.source,folder)
        self.assertEqual(before,target.read_bytes())

    def test_biolabs_alternatives_preserve_original_but_not_exit_instruction(self):
        q='irowiki-classic:Biolabs_Entrance_Quest'
        rows=self.db.execute('''SELECT b.*,e.step_id AS evidence_step,d.full_en FROM step_branches b
          JOIN quest_steps s USING(step_id) JOIN branch_descriptions d USING(branch_id)
          JOIN evidence e USING(evidence_id) WHERE s.quest_id=? AND s.step_order=16 ORDER BY b.branch_key''',(q,)).fetchall()
        self.assertEqual(len(rows),2)
        self.assertEqual({r['choice_group'] for r in rows},{'wall_code'})
        exit_step=self.db.execute('''SELECT s.step_id,s.original_instruction,d.full_en FROM quest_steps s
          JOIN step_descriptions d USING(step_id) WHERE s.quest_id=? AND s.step_order=22''',(q,)).fetchone()
        self.assertIn('Examine the Barrel again.',exit_step['original_instruction'])
        self.assertNotIn('Examine the Barrel again.',exit_step['full_en'])
        self.assertIn('Sewer Pipe',exit_step['full_en'])
        self.assertEqual(rows[1]['evidence_step'],exit_step['step_id'])
        parts=self.db.execute('SELECT original_text,target_step_id FROM source_fragments WHERE partition_key=? ORDER BY start_offset',(exit_step['step_id'],)).fetchall()
        self.assertEqual(''.join(r[0] for r in parts),exit_step['original_instruction'])
        self.assertEqual(len(parts),2)
        self.assertEqual(parts[1][1],rows[1]['step_id'])
        self.assertEqual(self.db.execute('''SELECT count(*) FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=16''',(q,)).fetchone()[0],0)

    def test_thor_rewards_are_five_alternatives_not_five_simultaneous_grants(self):
        q='irowiki-classic:Thor_Volcano_Base_Quest'
        rows=self.db.execute('''SELECT b.choice_group,e.original_name,r.amount FROM rewards r
          JOIN step_branches b USING(branch_id) JOIN entities e ON e.entity_id=r.item_id
          WHERE r.quest_id=? ORDER BY e.original_name''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in rows],[('office_item_reward',n,v) for n,v in [
          ('Bloody Branch','1'),('Giggling Boxes','5'),('Random Quiver',None),('Soft Apron','1'),('Steel Arrow Quiver',None)]])
        self.assertEqual(self.db.execute('''SELECT count(*) FROM rewards r JOIN step_rewards sr USING(reward_id)
          JOIN quest_steps s USING(step_id) WHERE r.quest_id=? AND s.step_order=21 AND r.kind='item' AND r.branch_id IS NULL''',(q,)).fetchone()[0],0)
        self.assertEqual(self.db.execute("SELECT sum(CAST(amount AS INTEGER)) FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],1000000)

    def test_veins_random_materials_and_unconsumed_steel(self):
        q='irowiki-classic:Veins_Siblings_Quest'
        steel=self.db.execute('''SELECT i.operation,i.quantity FROM step_items i JOIN entities e ON e.entity_id=i.item_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND e.original_name='Steel' ''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in steel],[('possess',1)])
        minimum=self.db.execute("SELECT count(*) FROM requirements WHERE quest_id=? AND kind='item_consumed_minimum'",(q,)).fetchone()[0]
        self.assertEqual(minimum,3)
        self.assertIsNone(self.db.execute('''SELECT 1 FROM step_locations sl JOIN locations l USING(location_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=10''',(q,)).fetchone())

    def test_nameless_combat_reward_and_death_penalty_scope(self):
        q='irowiki-classic:Nameless_Island_Entrance_Quest'
        row=self.db.execute('''SELECT d.full_ru,d.full_en FROM step_descriptions d JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=14''',(q,)).fetchone()
        self.assertIn('снаружи',row[0]);self.assertIn('You will lose experience',row[1])
        kills=self.db.execute('''SELECT e.original_name,m.game_id,i.quantity FROM step_monsters i
          JOIN monsters m ON m.entity_id=i.monster_id JOIN entities e ON e.entity_id=i.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? ORDER BY s.step_order''',(q,)).fetchall()
        self.assertEqual([tuple(r) for r in kills],[('Zombie Slaughter',None,1),("King's Corpse",1875,1)])
        self.assertEqual(self.db.execute("SELECT sum(CAST(amount AS INTEGER)) FROM rewards WHERE quest_id=? AND kind='base_exp'",(q,)).fetchone()[0],1500000)

    def test_thor_shortcut_scope_and_repeatability_are_explicit(self):
        q='irowiki-classic:Thor_Volcano_Level_2_Access_Quest'
        reasons={r[0] for r in self.db.execute('SELECT reason FROM review_queue WHERE quest_id=?',(q,))}
        self.assertTrue({'iRO_premium_source_scope','repeatability_scope','literal_map_spelling'}<=reasons)
        self.assertEqual(self.db.execute('SELECT availability FROM quests WHERE quest_id=?',(q,)).fetchone()[0],'unknown')
        self.assertIn('not repeatable',self.db.execute("SELECT condition_text FROM rewards WHERE quest_id=? AND kind='access'",(q,)).fetchone()[0])

    def test_branch_and_fragment_corruption_fails_validation(self):
        cases=[
          ("UPDATE source_fragments SET original_text='wrong' WHERE role='primary'",'source_fragment_mismatch'),
          ("UPDATE source_fragments SET partition_key=NULL WHERE role='primary'",'source_partition_gap_or_overlap'),
          ("UPDATE branch_mentions SET surface='wrong'",'invalid_branch_mention_span'),
          ("UPDATE branch_descriptions SET source_instruction_sha256='wrong'",'branch_source_hash'),
          ("UPDATE step_actions SET action_order=10000,step_id=(SELECT step_id FROM quest_steps WHERE quest_id='irowiki-classic:Biolabs_Entrance_Quest' AND step_order=22) WHERE action_id=(SELECT min(action_id) FROM step_actions WHERE branch_id IS NOT NULL)",'branch_fact_wrong_step'),
          ("UPDATE step_descriptions SET full_en='Wrong',full_ru='Неверно' WHERE step_id=(SELECT step_id FROM quest_steps WHERE quest_id='irowiki-classic:Biolabs_Entrance_Quest' AND step_order=22)",'edited_description_without_partition'),
        ]
        for sql,error in cases:
            with self.subTest(error=error),contextlib.closing(sqlite3.connect(':memory:')) as db:
                self.db.backup(db);db.execute(sql)
                with self.assertRaisesRegex(ValueError,error):validate(db)

    def test_human_reviewed_branches_cannot_be_silently_replaced(self):
        folder=self.out/'human-branch';folder.mkdir()
        target=folder/'quest_semantic.sqlite';shutil.copyfile(self.out/'quest_semantic.sqlite',target)
        with contextlib.closing(sqlite3.connect(target)) as db:
            db.execute("UPDATE branch_descriptions SET review_state='human_reviewed'");db.commit()
        before=target.read_bytes()
        with self.assertRaisesRegex(ValueError,'Human-reviewed branch'):build(self.source,folder)
        self.assertEqual(before,target.read_bytes())

    def test_kiel_sword_addition_is_source_backed_and_original_is_unchanged(self):
        q='irowiki-classic:Kiel_Hyre_Quest'
        row=self.db.execute('''SELECT s.step_id,s.original_instruction,d.full_ru,d.full_en,a.text_ru,a.text_en,
          src.kind,e.snippet FROM quest_steps s JOIN step_descriptions d USING(step_id)
          JOIN step_source_additions a USING(step_id) JOIN evidence e ON e.evidence_id=a.evidence_id
          JOIN sources src ON src.source_id=e.source_id WHERE s.quest_id=? AND s.step_order=30''',(q,)).fetchone()
        self.assertNotIn('Sword of Love',row['original_instruction'])
        self.assertEqual(row['full_en'],row['original_instruction']+' '+row['text_en'])
        self.assertTrue(row['full_ru'].endswith(row['text_ru']))
        self.assertEqual(row['kind'],'html_snapshot');self.assertEqual(row['snippet'],row['text_en'])
        for name in ['Sword of Love','Sword of Despair','Sword of Rage','Sword of Hope']:
            self.assertIn(name,row['full_ru']);self.assertIn(name,row['full_en'])
        actions=self.db.execute('''SELECT e.original_name,a.condition_text FROM step_actions a
          JOIN entities e USING(entity_id) WHERE a.step_id=? AND a.operation='insert' ORDER BY a.action_order''',(row['step_id'],)).fetchall()
        self.assertEqual([r[0] for r in actions],['Sword of Love','Sword of Despair','Sword of Rage','Sword of Hope'])
        self.assertTrue(all(target in r[1] for target,r in zip(['2nd Snake','4th Snake','1st Snake','3rd Snake'],actions)))

    def test_gaebolg_routes_keep_reward_order_and_source_partition(self):
        q='irowiki-classic:Curse_of_Gaebolg_(Founding_of_the_Nation_Myth_Quest)'
        row=self.db.execute('SELECT step_id,original_instruction FROM quest_steps WHERE quest_id=? AND step_order=14',(q,)).fetchone()
        parts=self.db.execute('SELECT original_text FROM source_fragments WHERE partition_key=? ORDER BY start_offset',(row['step_id'],)).fetchall()
        self.assertEqual(''.join(r[0] for r in parts),row['original_instruction'])
        rewards=self.db.execute('''SELECT b.branch_key,r.amount,r.condition_text FROM rewards r
          JOIN step_branches b USING(branch_id) WHERE r.quest_id=? ORDER BY b.branch_key''',(q,)).fetchall()
        self.assertEqual([(r[0],r[1]) for r in rewards],[('father_first','1600000'),('historian_first_disclose',None),('historian_first_withhold','1600000')])
        self.assertIn('before Biscuss',rewards[1][2])
        self.assertEqual(self.db.execute("SELECT count(*) FROM rewards WHERE quest_id=? AND kind='base_exp' AND branch_id IS NULL",(q,)).fetchone()[0],0)
        opts=[r[0] for r in self.db.execute('''SELECT o.exact_text FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id)
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND s.step_order=4 ORDER BY o.option_order''',(q,))]
        self.assertEqual(opts,['The great serpent swallowed the sea.','The eagle of the rainbow swallowed the serpent.','Then the eagle built its nest.','A nest upon the swallowed sea.'])

    def test_kiel_initial_retry_and_per_attempt_materials_are_distinct(self):
        q='irowiki-classic:Kiel_Hyre_Quest'
        cards=self.db.execute('''SELECT s.step_order,i.operation,i.quantity,i.condition_text FROM step_items i
          JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=i.item_id
          WHERE s.quest_id=? AND e.original_name='Black Keycard' ORDER BY s.step_order,i.quantity''',(q,)).fetchall()
        self.assertEqual([(r[0],r[1],r[2]) for r in cards],[(60,'collect',1),(60,'collect',3),(65,'use',1),(66,'use',2),(71,'bring',3)])
        self.assertIn('party wipe',cards[-1][3])
        mobs=self.db.execute('''SELECT e.original_name,m.quantity,m.condition_text FROM step_monsters m
          JOIN quest_steps s USING(step_id) JOIN entities e ON e.entity_id=m.monster_id
          WHERE s.quest_id=? AND s.step_order=68 ORDER BY e.original_name''',(q,)).fetchall()
        self.assertEqual([(r[0],r[1]) for r in mobs],[('Alicel',2),('Aliot',2)])
        self.assertTrue(all('Per attempt' in r[2] for r in mobs))
        iron=self.db.execute('''SELECT i.required,i.quantity FROM step_items i JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=69''',(q,)).fetchone()
        self.assertEqual(tuple(iron),(0,4))

    def test_kiel_reward_precision_and_npc_monster_homonyms(self):
        q='irowiki-classic:Kiel_Hyre_Quest'
        self.assertEqual([tuple(r) for r in self.db.execute("SELECT kind,amount FROM rewards WHERE quest_id=? AND kind LIKE 'base_exp%' ORDER BY kind,amount",(q,))],
                         [('base_exp',None),('base_exp','700000'),('base_exp_total','1700000')])
        spans=self.db.execute('''SELECT m.entity_type FROM description_mentions m JOIN quest_steps s USING(step_id)
          WHERE s.quest_id=? AND s.step_order=73 AND m.field='full_ru' AND m.surface='Kiehl' ORDER BY m.start_offset''',(q,)).fetchall()
        self.assertEqual([r[0] for r in spans],['npc','monster','monster'])
        optional=self.db.execute('''SELECT m.required FROM step_monsters m JOIN entities e ON e.entity_id=m.monster_id
          JOIN quest_steps s USING(step_id) WHERE s.quest_id=? AND e.original_name='Kiel-D-01' ''',(q,)).fetchone()
        self.assertEqual(optional[0],0)

    def test_source_addition_corruption_and_nonliteral_curation_fail(self):
        with contextlib.closing(sqlite3.connect(':memory:')) as db:
            self.db.backup(db);db.execute("UPDATE step_source_additions SET text_en='wrong'")
            with self.assertRaisesRegex(ValueError,'source_addition_evidence'):validate(db)
        folder=self.out/'invalid-addition';shutil.copytree(ROOT/'curation',folder)
        path=folder/'Kiel_Hyre_Quest.json';data=json.loads(path.read_text())
        data['steps']['30']['source_additions'][0]['text_en']='Unsupported source addition'
        path.write_text(json.dumps(data))
        before=(self.out/'quest_semantic.sqlite').read_bytes()
        with self.assertRaisesRegex(ValueError,'Nonliteral source addition'):build(self.source,self.out,curation_dir=folder)
        self.assertEqual(before,(self.out/'quest_semantic.sqlite').read_bytes())

    def test_human_reviewed_source_addition_cannot_be_silently_replaced(self):
        folder=self.out/'human-addition';folder.mkdir()
        target=folder/'quest_semantic.sqlite';shutil.copyfile(self.out/'quest_semantic.sqlite',target)
        with contextlib.closing(sqlite3.connect(target)) as db:
            db.execute("UPDATE step_source_additions SET review_state='human_reviewed'");db.commit()
        before=target.read_bytes()
        with self.assertRaisesRegex(ValueError,'Human-reviewed source additions'):build(self.source,folder)
        self.assertEqual(before,target.read_bytes())

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
        self.assertIn('"cache_hits": 216',stream.getvalue())
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
        keep='irowiki-classic:Juice_Quest';remove='irowiki-classic:2013_Easter_Event';reference='irowiki-classic:Access_Quests'
        out.executemany('INSERT INTO quests VALUES(?)',[(keep,),(remove,),(reference,)])
        out.executemany('INSERT INTO steps VALUES(?,1)',[(keep,),(remove,),(reference,)])
        out.execute('INSERT INTO deps VALUES(?,?)',(keep,remove))
        out.execute('INSERT INTO deps VALUES(?,?)',(keep,reference))
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
