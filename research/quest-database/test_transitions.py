"""Static transition witnesses must never be promoted to executable quest routing."""
import sqlite3
import unittest
from build import SCHEMA as BASE
from enrich import ensure_enrichment_schema
from graph import SCHEMA as FLOW
from state import SCHEMA as STATE
from adjudicate import SCHEMA as REVIEW
from transitions import (audit_transitions,guard_tests_symbol,literal_write,
                         trackable_symbol)

class TransitionAuditTests(unittest.TestCase):
    def build_db(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(BASE)
        ensure_enrichment_schema(db)
        db.executescript(FLOW)
        db.executescript(STATE)
        db.executescript(REVIEW)
        db.execute("""INSERT INTO provenance
          (id,project,path,blob_sha,source_url,stored_at,raw_size,content_zlib)
          VALUES(1,'rathena','npc/quests/sample.txt','pinned','https://example.invalid',
          '2026',1,X'78')""")
        db.execute("""INSERT INTO wiki_pages(id,title,url) VALUES
           (1,'Sample Quest','https://irowiki.org/classic/Sample_Quest'),
           (2,'Different Quest','https://irowiki.org/classic/Different_Quest')""")
        for pid in (1,2):
            for step in (1,2,3):
                db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",
                           (pid,step,"",f"Talk to NPC at step {step}"))
        for nid,ord_,name in ((1,1,"El Schatt"),(2,2,"Perfitz")):
            db.execute("""INSERT INTO script_npcs
               (id,source_id,ordinal,name,map,x,y,line_start,line_end)
               VALUES(?,1,?,?,'hugel',100,100,?,?)""",
               (nid,ord_,name,nid*10,nid*10+9))
        db.execute("INSERT INTO wiki_source_decisions(page_id,source_id,source_blob_sha,decision,reason) VALUES(1,1,'pinned','source_supported','sample supported')")
        db.execute("INSERT INTO wiki_source_sequence_evidence VALUES(1,1,1,1,'El Schatt',10)")
        db.execute("INSERT INTO wiki_source_sequence_evidence VALUES(1,1,2,2,'Perfitz',20)")
        db.execute("INSERT INTO wiki_source_sequence_evidence VALUES(1,1,3,1,'El Schatt',10)")
        db.execute("""INSERT INTO wiki_source_decisions(page_id,source_id,source_blob_sha,decision,reason)
                     VALUES(2,1,'pinned','rejected','not same quest')""")
        db.execute("""INSERT INTO npc_state_facts
          (source_id,npc_ordinal,line_no,occurrence,role,subject,value_expr,argument,context_guard_line)
          VALUES(1,1,11,1,'variable_write','hg_memory','3','set hg_memory,3',NULL)""")
        db.execute("""INSERT INTO npc_state_facts
          (source_id,npc_ordinal,line_no,occurrence,role,subject,value_expr,argument,context_guard_line)
          VALUES(1,2,22,1,'variable_write','hg_memory','4','set hg_memory,4',21)""")
        db.execute("""INSERT INTO npc_state_facts
          (source_id,npc_ordinal,line_no,occurrence,role,object_id,argument)
          VALUES(1,2,23,1,'quest_complete',8063,'8063')""")
        db.execute("INSERT INTO npc_flow_conditions VALUES(1,2,21,'hg_memory == 3',29)")
        db.execute("INSERT INTO npc_flow_points VALUES(1,1,2,21,'select','\"Yes:No\"')")
        db.execute("INSERT INTO npc_flow_options VALUES(1,1,'Yes',NULL,'unresolved')")
        return db

    def test_guard_matches_literal_without_executing_and_rebuild_is_stable(self):
        db=self.build_db()
        report=audit_transitions(db)
        self.assertEqual(report["summary"]["reviewed_source_implementations"],1)
        self.assertEqual(report["summary"]["excluded_non_supported_decisions"],1)
        self.assertEqual(report["summary"]["literal_guard_correspondences"],1)
        self.assertEqual(report["summary"]["source_quest_complete_commands"],1)
        self.assertEqual(report["summary"]["potential_hops"],2)
        self.assertEqual(report["summary"]["unresolved_menu_options"],1)
        self.assertEqual(report["summary"]["hops_without_correspondence"],1)
        row=report["quests"][0]
        self.assertFalse(row["complete_route_verified"])
        self.assertFalse(row["safe_for_automatic_dialogue"])
        self.assertEqual(row["journal_completion_status"],"literal_completequest_present_unattributed")
        matched=[x for x in row["wiki_order_vs_static_source_evidence"]
                 if x["evidence_type"]=="literal_write_guard_correspondence_nonexecuted"]
        self.assertEqual(len(matched),1)
        self.assertEqual(matched[0]["to_guard_line"],21)
        self.assertEqual(matched[0]["from_write_value"],"3")
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_transition_audits").fetchone()[0],1)
        self.assertFalse(db.execute("PRAGMA foreign_key_check").fetchall())
        again=audit_transitions(db)
        self.assertEqual(again["summary"],report["summary"])
        self.assertFalse(db.execute("PRAGMA foreign_key_check").fetchall())

    def test_guards_do_not_run_expressions(self):
        self.assertEqual(literal_write(" = 3"),"3")
        self.assertIsNone(literal_write("hg_memory+1"))
        self.assertFalse(trackable_symbol(".@local"))
        self.assertFalse(trackable_symbol("Zeny"))
        self.assertTrue(trackable_symbol("Shinokas_Quest"))
        self.assertTrue(guard_tests_symbol("hg_memory == 3 && countitem(123)>0",
                                           "hg_memory","3"))
        self.assertFalse(guard_tests_symbol("hg_memory != 3","hg_memory","3"))
        self.assertFalse(guard_tests_symbol("hg_memory == 30","hg_memory","3"))
        self.assertFalse(guard_tests_symbol("other_hg_memory == 3","hg_memory","3"))

if __name__=="__main__":
    unittest.main()
