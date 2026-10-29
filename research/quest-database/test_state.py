"""Quest-state lexical extraction regression tests (not gameplay simulation)."""
import sqlite3
import unittest
import zlib

from graph import SCHEMA as FLOW_SCHEMA
from state import create_state_facts, extract_state, literal_int

SAMPLE = """prt_in,47,173,3 script Morrison#juice 97,{
 // setquest 9999; getitem 100,100;
 mes "getItem 123,2; // inside dialogue";
 if (checkquest(1001, HUNTING) == 1 && countitem(501) >= 2) {
   delitem 501,2;
   setquest 1002;
   set MISC_QUEST,MISC_QUEST | 1;
   getitem 502,1;
 }
 if (countitem(.@fruit) > 0) {
   completequest 1002;
   erasequest 1001;
 }
 if (MISC_QUEST == 1) set Zeny,Zeny-100;
 .@counter += 1;
 getitem .@reward,1;
}
"""

class StateTests(unittest.TestCase):
    def test_literal_ids_and_dynamic_ids(self):
        self.assertEqual(literal_int('  1001 '),1001)
        self.assertIsNone(literal_int('.@quest_id'))
        self.assertIsNone(literal_int('1000 + 1'))

    def test_extract_real_commands_and_checks_not_dialogue_or_comments(self):
        facts=extract_state(SAMPLE,1,len(SAMPLE.splitlines()),[(4,9),(10,13)])
        rows=[(x[2],x[4],x[7],x[8]) for x in facts]
        self.assertEqual(sum(x[0]=="quest_start" for x in rows),1)
        self.assertEqual(sum(x[0]=="quest_complete" for x in rows),1)
        self.assertEqual(sum(x[0]=="quest_erase" for x in rows),1)
        self.assertEqual(sum(x[0]=="item_check" for x in rows),2)
        self.assertEqual(sum(x[0]=="quest_check" for x in rows),1)
        self.assertEqual(sum(x[0]=="item_grant" for x in rows),2)
        self.assertEqual(sum(x[0]=="item_consume" for x in rows),1)
        self.assertEqual(sum(x[0]=="variable_write" for x in rows),3)
        self.assertNotIn(9999,[x[1] for x in rows])
        self.assertTrue(any(x[0]=="item_grant" and x[1] is None for x in rows))
        self.assertEqual([x[3] for x in rows if x[0]=="quest_start"],[4])

    def test_source_lines_and_guards_are_stable(self):
        facts=extract_state(SAMPLE,1,len(SAMPLE.splitlines()),[(4,9),(10,13)])
        start=[x for x in facts if x[2]=="quest_start"][0]
        self.assertEqual(start[0],6)
        self.assertEqual(start[8],4)
        self.assertEqual(start[4],1002)

    def test_stores_registry_links_and_idempotent_on_rerun(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript("""CREATE TABLE provenance (id INTEGER PRIMARY KEY,content_zlib BLOB);
        CREATE TABLE script_npcs(source_id INTEGER,ordinal INTEGER,line_start INTEGER,line_end INTEGER);
        CREATE TABLE quest_registry(project TEXT,variant TEXT,quest_id INTEGER);""")
        db.executescript(FLOW_SCHEMA)
        db.execute("INSERT INTO provenance VALUES(?,?)",(1,zlib.compress(SAMPLE.encode())))
        db.execute("INSERT INTO script_npcs VALUES(1,1,1,?)",(len(SAMPLE.splitlines()),))
        db.execute("INSERT INTO quest_registry VALUES('rathena','pre-re',1002)")
        db.execute("INSERT INTO npc_flow_conditions VALUES(1,1,4,'checkquest(1001)',9)")
        summary=create_state_facts(db)
        self.assertGreater(summary["facts"],0)
        self.assertEqual(summary["registry_matches"],2)
        self.assertEqual(db.execute("SELECT count(*) FROM npc_quest_registry_refs").fetchone()[0],2)
        start=db.execute("SELECT object_id,context_guard_line FROM npc_state_facts WHERE role='quest_start'").fetchone()
        self.assertEqual(start,(1002,4))
        first=db.execute("SELECT count(*) FROM npc_state_facts").fetchone()[0]
        create_state_facts(db)
        self.assertEqual(db.execute("SELECT count(*) FROM npc_state_facts").fetchone()[0],first)
        self.assertEqual(db.execute("PRAGMA integrity_check").fetchone()[0],"ok")
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])

if __name__=="__main__":
    unittest.main()
