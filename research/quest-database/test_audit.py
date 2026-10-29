"""Per-quest review queue must expose missing scripts and freshness correctly."""
import sqlite3
import unittest
from audit import summarize_review_queue
from build import SCHEMA as CORE
from enrich import ensure_enrichment_schema
from reconcile import SCHEMA as REVIEWS
from quality import ensure_quality_schema

class AuditTests(unittest.TestCase):
    def test_prioritizes_missing_quests_and_does_not_fabricate_server_validation(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(CORE)
        ensure_enrichment_schema(db)
        db.executescript(REVIEWS)
        db.execute("""INSERT INTO wiki_pages(id,title,url,status) VALUES
          (1,'Missing Quest','https://irowiki.org/classic/Missing_Quest','walkthrough_parsed'),
          (2,'Empty Article','https://irowiki.org/classic/Empty_Article','article_no_steps')""")
        for step in range(1,8):
            db.execute("INSERT INTO wiki_steps VALUES (?,?,?,?)",(1,step,None,"Step "+str(step)))
        db.execute("""INSERT INTO wiki_script_reviews VALUES
           (1,0,NULL,0,0,'no_candidate','No script'),
           (2,0,NULL,0,0,'no_candidate','No script')""")
        ensure_quality_schema(db)
        result=summarize_review_queue(db)
        self.assertEqual(result["metrics"]["indexed_pages"],2)
        self.assertEqual(result["metrics"]["pages_without_script"],2)
        self.assertEqual(result["metrics"]["pages_not_freshly_verified"],2)
        self.assertEqual(result["quests"][0]["review_priority"],"critical")
        self.assertEqual(result["quests"][0]["steps"],7)
        self.assertIn("no_script_candidate",result["quests"][0]["issues"])
        self.assertEqual(result["quests"][0]["source_scopes"],[])
        self.assertIn("unverified",result["quests"][0]["assurance"])
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])

if __name__=="__main__":
    unittest.main()
