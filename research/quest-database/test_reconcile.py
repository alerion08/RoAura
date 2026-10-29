"""Ambiguous guide-to-NPC menu reconciliation tests."""
import sqlite3
import unittest
from graph import SCHEMA as FLOW_SCHEMA
from reconcile import create_reviews, norm_answer


class ReviewTests(unittest.TestCase):
    def test_normalize_dialogue_option_without_inventing_text(self):
        self.assertEqual(norm_answer("^FF0000I need more wine...!"),"i need more wine")
        self.assertEqual(norm_answer("Not now."),"not now")

    def test_ambiguous_candidates_and_unique_literal_choice(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript("""CREATE TABLE wiki_pages(id INTEGER PRIMARY KEY,title TEXT);
          CREATE TABLE provenance(id INTEGER PRIMARY KEY,path TEXT);
          CREATE TABLE wiki_script_links(page_id INTEGER REFERENCES wiki_pages(id),
                     source_id INTEGER REFERENCES provenance(id),score INTEGER);
          CREATE TABLE wiki_step_npc_links(page_id INTEGER,score INTEGER);
          CREATE TABLE wiki_details(page_id INTEGER,step_no INTEGER,kind TEXT,value TEXT);""")
        db.executescript(FLOW_SCHEMA)
        db.execute("INSERT INTO wiki_pages VALUES (1,'Example Quest'),(2,'Other Quest'),(3,'Missing Quest')")
        db.execute("INSERT INTO provenance VALUES(1,'npc/quests/a.txt'),(2,'npc/quests/b.txt')")
        db.execute("INSERT INTO wiki_script_links VALUES (1,1,95),(1,2,95),(2,1,95)")
        db.execute("INSERT INTO npc_flow_points(id,source_id,npc_ordinal,line_no,kind,expression) VALUES (1,1,1,5,'select','...')")
        db.execute("INSERT INTO npc_flow_options VALUES (1,1,'Give me the map!',12,'switch_case')")
        db.execute("INSERT INTO wiki_details VALUES (1,1,'guide_answer','Give me the map!'),(2,1,'guide_answer','Give me the map!'),(2,1,'guide_answer','Yes')")
        result=create_reviews(db)
        self.assertEqual(result["pages_ambiguous"],1)
        self.assertEqual(result["pages_no_script"],1)
        matches=db.execute("SELECT page_id,review_state FROM wiki_answer_menu_matches ORDER BY page_id").fetchall()
        self.assertEqual(matches,[(1,'candidate_unique_label'),(2,'candidate_unique_label')])
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])
        # Rebuild must not multiply records.
        create_reviews(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_answer_menu_matches").fetchone()[0],2)


if __name__=="__main__":
    unittest.main()
