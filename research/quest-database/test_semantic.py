import contextlib
import io
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest

from selection import quest_key, select_pages, filter_catalog, snapshots
from semantic import build, coordinates, resolve_context, item_operation, validate, sha, ROOT, SCHEMA, BRANCH_SCHEMA, ADDITION_SCHEMA, ROUTE_SCHEMA, migrate_semantic


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
        self.assertEqual(self.report['fully_localized_steps'],584)
        self.assertEqual(self.report['not_fully_localized_steps'],2166)
        self.assertFalse(self.report['complete'])
        self.assertEqual(self.db.execute('SELECT count(*) FROM ready_steps').fetchone()[0],584)
        missing=self.db.execute("SELECT count(*) FROM review_queue WHERE reason='localization_missing'").fetchone()[0]
        self.assertEqual(missing,2166)
        self.assertEqual(self.report['fully_localized_branches'],47)
        self.assertEqual(self.report['curated_quests'],40)
        self.assertEqual(self.report['pending_quests'],176)
        self.assertEqual(self.report['quest_routes'],7)
        self.assertEqual(self.report['action_reuse_links'],5)

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
          WHERE e.method IN ('curated_quest_relation','curated_single_prerequisite') AND r.quest_id NOT IN ('irowiki-classic:Finding_a_Fairy','irowiki-classic:Two_Tribes','irowiki-classic:Einbroch_Murder_Quest','irowiki-classic:President_Quest','irowiki-classic:Heart_Fragment_Quest') ORDER BY r.quest_id,r.kind''')]
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
        self.assertEqual(stage,7);self.assertEqual(self.report['phase_prerequisites'],10)

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
