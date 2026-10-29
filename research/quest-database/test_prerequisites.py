import sqlite3
import unittest
from build import SCHEMA
from prerequisites import prerequisite_clause,match_known_titles,extract_prerequisites

class PrerequisiteTests(unittest.TestCase):
    def test_strict_labeled_requirement(self):
        text="Base Level: 60 Item(s): Handcuffs Quest Prerequisite(s): Biolabs Entrance Quest, Einbroch Murder Quest"
        self.assertEqual(prerequisite_clause(text),"Biolabs Entrance Quest, Einbroch Murder Quest")
        self.assertIsNone(prerequisite_clause("Talk about the Biolabs Entrance Quest and go to President"))
        self.assertIsNone(prerequisite_clause(None))

    def test_exact_links_do_not_invent_quest_logic(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(SCHEMA)
        db.execute("""INSERT INTO wiki_pages(id,title,url,status,requirements) VALUES
          (1,'President Quest','https://irowiki.org/classic/President_Quest','walkthrough_parsed',
            'Base Level: 60 Quest Prerequisite(s): Biolabs Entrance Quest, Einbroch Murder Quest'),
          (2,'Biolabs Entrance Quest','https://irowiki.org/classic/Biolabs_Entrance_Quest','walkthrough_parsed',NULL),
          (3,'Einbroch Murder Quest','https://irowiki.org/classic/Einbroch_Murder_Quest','walkthrough_parsed',NULL)""")
        report=extract_prerequisites(db)
        self.assertEqual(report["explicit_prerequisite_statements"],1)
        self.assertEqual(report["wiki_page_links"],2)
        self.assertEqual(db.execute("SELECT prerequisite_page_id FROM wiki_quest_prerequisite_links ORDER BY prerequisite_page_id").fetchall(),[(2,),(3,)])
        self.assertEqual(db.execute("SELECT expression_logic FROM wiki_quest_prerequisite_text").fetchone()[0],"unspecified")
        extract_prerequisites(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_quest_prerequisite_links").fetchone()[0],2)
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])

    def test_missing_title_preserves_text_without_fake_link(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(SCHEMA)
        db.execute("""INSERT INTO wiki_pages(id,title,url,requirements) VALUES
         (1,'Some Quest','https://irowiki.org/classic/Some_Quest','Quest Prerequisite(s): Unindexed Quest')""")
        summary=extract_prerequisites(db)
        self.assertEqual(summary["statements_with_no_exact_catalog_match"],1)
        self.assertEqual(summary["wiki_page_links"],0)
        self.assertEqual(db.execute("SELECT raw_text FROM wiki_quest_prerequisite_text").fetchone()[0],"Unindexed Quest")

if __name__=="__main__":
    unittest.main()
