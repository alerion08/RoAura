"""Auditing 40 unsupported hops is research, not an autonomous quest interpreter."""
import sqlite3
import unittest

from test_transitions import TransitionAuditTests
from transitions import audit_transitions
from gap_review import audit_gaps,terminal_audit,has_symbol

class GapReviewTests(unittest.TestCase):
    def fixture(self):
        db=TransitionAuditTests().build_db()
        db.execute("""INSERT INTO npc_flow_conditions
           VALUES(1,1,17,'hg_memory == 4',19)""")
        db.execute("""INSERT INTO npc_state_facts
           (source_id,npc_ordinal,line_no,occurrence,role,object_id,amount_expr,argument)
           VALUES(1,2,25,1,'item_grant',512,'1','512,1')""")
        db.execute("""INSERT INTO npc_state_facts
           (source_id,npc_ordinal,line_no,occurrence,role,object_id,argument)
           VALUES(1,1,18,1,'item_check',512,'512')""")
        db.execute("""INSERT INTO npc_state_facts
           (source_id,npc_ordinal,line_no,occurrence,role,object_id,argument)
           VALUES(1,1,19,1,'quest_start',8063,'8063')""")
        db.execute("""INSERT INTO npc_state_facts
           (source_id,npc_ordinal,line_no,occurrence,role,object_id,amount_expr,argument)
           VALUES(1,1,26,1,'item_consume',512,'1','512,1')""")
        db.execute("""UPDATE npc_state_facts SET context_guard_line=21
           WHERE source_id=1 AND npc_ordinal=2 AND line_no=23
             AND role='quest_complete'""")
        return db

    def test_source_guard_read_item_handoff_and_journal_pair_are_not_gameplay_proofs(self):
        db=self.fixture()
        stage=audit_transitions(db)
        self.assertEqual(stage["summary"]["hops_without_correspondence"],1)
        result=audit_gaps(db)
        self.assertEqual(result["summary"]["gaps"],1)
        self.assertEqual(result["summary"]["gaps_with_literal_read_candidates"],1)
        self.assertEqual(result["summary"]["gaps_with_item_handoff_candidates"],1)
        self.assertEqual(result["summary"]["literal_item_handoffs"],1)
        self.assertEqual(result["summary"]["gaps_with_same_npc_item_check_consume_pairs"],1)
        self.assertEqual(result["summary"]["same_npc_item_check_consume_pairs"],1)
        self.assertEqual(result["summary"]["matched_journal_start_complete_literal_ids"],1)
        self.assertEqual(result["summary"]["terminal_audits"],1)
        hop=result["unsupported_transitions"][0]
        self.assertEqual((hop["from_step"],hop["to_step"]),(2,3))
        self.assertEqual(hop["literal_item_handoff_candidates"],[512])
        self.assertEqual(hop["same_npc_item_check_consume_pairs"][0]["item_id"],512)
        self.assertFalse(hop["same_npc_item_check_consume_pairs"][0]["mandatory_item_requirement_verified"])
        self.assertEqual(hop["candidate_literal_guard_reads"][0]["literal_value"],"4")
        self.assertFalse(hop["transition_verified"])
        self.assertFalse(hop["item_requirements_proven"])
        self.assertFalse(hop["safe_for_automatic_navigation"])
        self.assertEqual(result["terminal_state_reviews"][0]["matching_literal_ids"],[8063])
        complete=[x for x in result["terminal_state_reviews"][0]["source_command_evidence"] if x["role"]=="quest_complete"][0]
        self.assertEqual(complete["guard_expression"],"hg_memory == 3")
        self.assertFalse(complete["guard_branch_executed"])
        self.assertFalse(result["terminal_state_reviews"][0]["full_quest_terminal_path_verified"])
        self.assertFalse(result["safety"]["automatic_game_navigation_enabled"])
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_gap_reviews").fetchone()[0],1)
        self.assertFalse(db.execute("PRAGMA foreign_key_check").fetchall())
        again=audit_gaps(db)
        self.assertEqual(again["summary"],result["summary"])
        self.assertFalse(db.execute("PRAGMA foreign_key_check").fetchall())

    def test_rejects_unrelated_conditions_and_item_ids(self):
        self.assertFalse(has_symbol("other_hg_memory == 4","hg_memory"))
        self.assertTrue(has_symbol("hg_memory == 4","hg_memory"))
        db=self.fixture()
        db.execute("UPDATE npc_flow_conditions SET expression='hg_memory != 4' WHERE source_id=1 AND npc_ordinal=1")
        db.execute("UPDATE npc_state_facts SET object_id=513 WHERE role IN ('item_check','item_consume')")
        audit_transitions(db)
        result=audit_gaps(db)
        self.assertEqual(result["summary"]["gaps_with_literal_read_candidates"],0)
        self.assertEqual(result["summary"]["gaps_with_item_handoff_candidates"],0)
        self.assertEqual(result["summary"]["gaps"],1)

if __name__=="__main__":
    unittest.main()
