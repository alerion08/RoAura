"""Wiki refresh should preserve known-good guides and expose stale coverage."""
import sqlite3
import unittest
from unittest.mock import patch

import requests

from build import SCHEMA as CORE_SCHEMA,ingest_wiki
from enrich import ensure_enrichment_schema
from quality import (ensure_quality_schema,too_few_steps,update_state,
                     state_counts,dataset_counts,check_nonregression)
from runtime import script_variant_scope

class QualityTests(unittest.TestCase):
    def make_db(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(CORE_SCHEMA)
        ensure_enrichment_schema(db)
        db.execute("""INSERT INTO wiki_pages(id,title,url,status,content_sha)
          VALUES(1,'Juice Quest','https://irowiki.org/classic/Juice_Quest','walkthrough_parsed','goodhash'),
                (2,'Old Quest','https://irowiki.org/classic/Old_Quest','walkthrough_parsed','oldhash')""")
        for pid,num in ((1,7),(2,5)):
            for i in range(1,num+1):
                db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",(pid,i,"","Step "+str(i)))
            db.execute("INSERT INTO wiki_parse_quality VALUES(?,?,?,?)",(pid,"numbered",num,0))
        ensure_quality_schema(db)
        return db

    def test_thresholds(self):
        self.assertTrue(too_few_steps(75,0))
        self.assertTrue(too_few_steps(75,25))
        self.assertFalse(too_few_steps(75,45))
        self.assertFalse(too_few_steps(1,1))
        self.assertFalse(too_few_steps(0,0))

    def test_partial_network_error_retains_all_old_guides(self):
        db=self.make_db()
        class FailFetcher:
            def __init__(self): self.session=self
            def close(self):pass
            def get(self,url):raise requests.ConnectionError("temporary outage")
        with patch("build.WikiFetcher",FailFetcher),patch("build.wiki_discover",return_value=(
            {"Juice Quest":"https://irowiki.org/classic/Juice_Quest"},
            [{"url":"Category:Quests","error":"connection aborted"}])):
            summary=ingest_wiki(db)
        self.assertEqual(summary["retained_from_previous"],1)
        self.assertEqual(summary["cached_errors"],2)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_steps").fetchone()[0],12)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_pages WHERE status='walkthrough_parsed'").fetchone()[0],2)
        self.assertEqual(db.execute("SELECT content_sha FROM wiki_pages WHERE id=1").fetchone()[0],"goodhash")
        self.assertEqual(state_counts(db),{"cached_http_error":2})
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])

    def test_parser_regression_keeps_steps_and_sha(self):
        db=self.make_db()
        class Fetched:
            def __init__(self):self.session=self
            def close(self):pass
            def get(self,url):return '<div class="mw-parser-output"><p>Empty article.</p></div>',url
        with patch("build.WikiFetcher",Fetched),patch("build.wiki_discover",return_value=(
            {"Juice Quest":"https://irowiki.org/classic/Juice_Quest"},[])):
            summary=ingest_wiki(db,max_pages=1)
        self.assertEqual(summary["parse_regressions_prevented"],1)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_steps WHERE page_id=1").fetchone()[0],7)
        self.assertEqual(db.execute("SELECT content_sha FROM wiki_pages WHERE id=1").fetchone()[0],"goodhash")
        self.assertEqual(db.execute("SELECT refresh_state FROM wiki_refresh_state WHERE page_id=1").fetchone()[0],"cached_parse_regression")

    def test_upgrade_restores_legacy_fetched_guide_status(self):
        db=self.make_db()
        db.execute("UPDATE wiki_pages SET status='fetch_error' WHERE id=1")
        ensure_quality_schema(db)
        self.assertEqual(db.execute("SELECT status FROM wiki_pages WHERE id=1").fetchone()[0],"walkthrough_parsed")
        self.assertIsNone(db.execute("SELECT last_success_utc FROM wiki_refresh_state WHERE page_id=1").fetchone()[0])

    def test_major_regression_guard_and_variant_scopes(self):
        previous={"script_sources":939,"wiki_pages":286,"wiki_steps":3420}
        with self.assertRaisesRegex(RuntimeError,"smaller dataset"):
            check_nonregression(previous,{"script_sources":50,"wiki_pages":286,"wiki_steps":3420})
        with self.assertRaisesRegex(RuntimeError,"smaller dataset"):
            check_nonregression(previous,{"script_sources":939,"wiki_pages":286,"wiki_steps":3000})
        self.assertTrue(check_nonregression(previous,previous)["passed"])
        self.assertEqual(script_variant_scope("npc/pre-re/quests/foo.txt"),"pre-re")
        self.assertEqual(script_variant_scope("npc/re/jobs/foo.txt"),"renewal")
        self.assertEqual(script_variant_scope("npc/quests/foo.txt"),"common")

if __name__=="__main__":
    unittest.main()
