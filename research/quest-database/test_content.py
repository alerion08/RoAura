"""Wiki content tags are evidence-backed curation clues, not gameplay claims."""
import sqlite3
import unittest
from build import SCHEMA as RESEARCH_SCHEMA
from content import extract_content,content_decision
from enrich import ensure_enrichment_schema

class ContentTests(unittest.TestCase):
    def test_distinctive_source_language_only(self):
        result=content_decision("Unknown Quest","",[(1,"collect_items"),(2,"deliver_items")])
        self.assertEqual(result[0],"item_collection_candidate")
        self.assertEqual(result[1],"corroborated_guide_steps")
        self.assertEqual(content_decision("Unknown Quest","",[(1,"talk_to_npc")])[0],
                         "unsorted_quest_candidate")
        self.assertEqual(content_decision("Unknown Quest","",[(1,"explicit_dungeon_access")])[0],
                         "dungeon_or_access_candidate")
        self.assertEqual(content_decision("Unknown Quest","",[(1,"explicit_job_change")])[0],
                         "profession_quest_candidate")
        self.assertEqual(content_decision("Unknown Quest","Repeatable Quest",[])[0],
                         "repeatable_quest_candidate")
        self.assertEqual(content_decision("2020/01/10 ~ 2020/01/30 Quest","",[])[0],
                         "historical_or_seasonal")
        self.assertEqual(content_decision("President Quest","",[(1,"talk_to_npc")])[0],
                         "storyline_candidate")

    def test_source_evidence_and_repeatable_rebuild(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(RESEARCH_SCHEMA)
        ensure_enrichment_schema(db)
        pages=[
            (1,"Unknown Quest","",["Talk to Alice.","Collect 5 Feathers from the forest.",
                                    "Give 5 Feathers to Alice."]),
            (2,"Other Quest","",["The player gains access to Niflheim Dungeon."]),
            (3,"Guide","",["Talk to Charlie.", "Defeat 12 Poring."]),
            (4,"2015/01/01 ~ 2015/02/01 Quest","",["Hello."]),
        ]
        for pid,title,req,steps in pages:
            db.execute("INSERT INTO wiki_pages(id,title,url,requirements) VALUES(?,?,?,?)",
                (pid,title,"https://irowiki.org/classic/"+title.replace(" ","_"),req))
            for no,txt in enumerate(steps,1):
                db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",(pid,no,"",txt))
        result=extract_content(db)
        self.assertEqual(result["pages"],4)
        self.assertEqual(db.execute("SELECT category FROM wiki_content_categories WHERE page_id=1").fetchone()[0],
                         "item_collection_candidate")
        self.assertEqual(db.execute("SELECT category FROM wiki_content_categories WHERE page_id=2").fetchone()[0],
                         "dungeon_or_access_candidate")
        self.assertEqual(db.execute("SELECT category FROM wiki_content_categories WHERE page_id=3").fetchone()[0],
                         "unsorted_quest_candidate")
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_content_categories").fetchone()[0],4)
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])
        extract_content(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_content_categories").fetchone()[0],4)

if __name__=="__main__":
    unittest.main()
