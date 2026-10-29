"""Export contract smoke test: stable keys, relations, no raw-source dependency."""
import sqlite3
import tempfile
import unittest
from pathlib import Path

from build import SCHEMA as RESEARCH_SCHEMA, save_source
from enrich import ensure_enrichment_schema, link_database
from graph import create_flow
from reconcile import create_reviews
from state import create_state_facts
from runtime import make_runtime, key_page, key_script

SCRIPT=b"""prt_in,49,172,3\tscript\tMarianne#juice\t53,{
 if (checkquest(1001) == 0 && countitem(517) > 0) {
   setquest 1001;
   mes "[Housewife Marianne]";
   switch(select("Talk to me:Leave")) {
     case 1:
       delitem 517,1;
       getitem 512,1;
       completequest 1001;
       close;
     case 2:
       close;
   }
 }
}
"""

class ExportTests(unittest.TestCase):
    def test_key_determinism(self):
        self.assertEqual(key_page("https://irowiki.org/classic/Juice_Quest"),"irowiki-classic:Juice_Quest")
        self.assertEqual(key_script("rathena","npc/quests/juice_maker.txt"),
                         "rathena:npc/quests/juice_maker.txt")
        with self.assertRaises(ValueError):
            key_page("https://wrong.example/other/path")

    def test_full_projection_with_source_link_and_no_raw_script(self):
        source=sqlite3.connect(":memory:")
        source.execute("PRAGMA foreign_keys=ON")
        source.executescript(RESEARCH_SCHEMA)
        ensure_enrichment_schema(source)
        save_source(source,"rathena","npc/quests/juice_maker.txt","123sha",
                    "https://github.com/rathena/rathena/blob/master/npc/quests/juice_maker.txt",SCRIPT)
        source.execute("""INSERT INTO quest_registry VALUES('rathena','pre-re',1001,'Test Quest',NULL,NULL)""")
        source.execute("""INSERT INTO wiki_pages (id,title,url,status) VALUES
          (1,'Juice Quest','https://irowiki.org/classic/Juice_Quest','walkthrough_parsed')""")
        source.execute("""INSERT INTO wiki_steps VALUES
          (1,1,'','Talk to Marianne in Prontera'),(1,2,'','Say "Talk to me"')""")
        source.execute("INSERT INTO wiki_step_sources VALUES (1,1,1,'numbered'),(1,2,2,'numbered')")
        source.execute("INSERT INTO wiki_details VALUES (1,2,'guide_answer','Talk to me',0)")
        source.execute("""INSERT INTO wiki_script_links VALUES(1,1,95,'exact_title_filename','candidate')""")
        # Retain the explicitly supplied 95-point source link for this fixture.
        create_flow(source)
        create_state_facts(source)
        create_reviews(source)
        with tempfile.TemporaryDirectory() as tmp:
            dest=Path(tmp)/"questie_catalog.sqlite"
            summary=make_runtime(source,dest)
            self.assertEqual(summary["schema_version"],2)
            self.assertEqual(summary["sqlite_integrity"],"ok")
            self.assertEqual(summary["foreign_key_violations"],0)
            self.assertGreater(summary["tables"]["dialogue_options"],0)
            self.assertGreater(summary["tables"]["quest_state_facts"],0)
            with sqlite3.connect(dest) as runtime:
                self.assertEqual(runtime.execute("PRAGMA user_version").fetchone()[0],2)
                self.assertEqual(runtime.execute("SELECT count(*) FROM quests").fetchone()[0],1)
                self.assertEqual(runtime.execute("SELECT count(*) FROM scripts").fetchone()[0],1)
                self.assertEqual(runtime.execute("SELECT count(*) FROM quest_quality").fetchone()[0],1)
                self.assertEqual(runtime.execute("SELECT variant_scope FROM scripts").fetchone()[0],"common")
                self.assertEqual(runtime.execute("SELECT refresh_state FROM quest_quality").fetchone()[0],"never_fetched")
                self.assertEqual(runtime.execute("PRAGMA foreign_key_check").fetchall(),[])
                self.assertEqual(runtime.execute("SELECT object_id FROM quest_state_facts WHERE role='quest_start'").fetchone()[0],1001)
                self.assertEqual(runtime.execute("SELECT count(*) FROM candidate_answers").fetchone()[0],1)
                self.assertGreater(runtime.execute("SELECT count(*) FROM quest_progress_evidence").fetchone()[0],0)
                self.assertGreater(runtime.execute("SELECT count(*) FROM quest_dialogue_candidates").fetchone()[0],0)
                self.assertEqual(runtime.execute("SELECT count(*) FROM quest_navigation_candidates").fetchone()[0],0)
                self.assertNotIn("provenance", {row[0] for row in runtime.execute(
                    "SELECT name FROM sqlite_master WHERE type='table'")})
            # Keys/rows stay stable across independent builds from identical data.
            second=Path(tmp)/"again.sqlite"
            make_runtime(source,second)
            with sqlite3.connect(dest) as a,sqlite3.connect(second) as b:
                self.assertEqual(a.execute("SELECT quest_key FROM quests").fetchall(),
                                 b.execute("SELECT quest_key FROM quests").fetchall())
                self.assertEqual(a.execute("SELECT npc_key FROM npcs").fetchall(),
                                 b.execute("SELECT npc_key FROM npcs").fetchall())

if __name__=="__main__":
    unittest.main()
