"""Discovery findings are a review queue, never silently accepted script links."""
import sqlite3
import unittest
import zlib

from build import SCHEMA
from enrich import ensure_enrichment_schema
from discovery import interaction_names,review_unlinked_scripts,review_unclassified_content
from content import extract_content

class DiscoveryTests(unittest.TestCase):
    def test_interaction_cues_require_exact_name(self):
        index={"marianne":{"marianne","marianne smith"},"morrison":{"morrison"}}
        cues,names=interaction_names("Talk to Marianne Smith. Return to Morrison. Marianne appears.",index)
        self.assertEqual(cues,2)
        self.assertIn("marianne smith",names)
        self.assertIn("morrison",names)
        self.assertNotIn("marianne",names) # Longer name is required in the source match.
        self.assertEqual(interaction_names("Marianne and Morrison were nearby",index)[1],set())
        self.assertEqual(interaction_names("Speak to MarianneX",index)[1],set())

    def test_proposal_requires_two_distinct_npcs_in_separate_steps(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(SCHEMA)
        ensure_enrichment_schema(db)
        for sid,path in [(1,"npc/quests/hidden_quest.txt"),(2,"npc/quests/single_name.txt"),
                         (3,"npc/quests/other_event.txt")]:
            blob=b"// captured public source\n"
            db.execute("""INSERT INTO provenance
              (id,project,path,content_zlib,source_url,stored_at,raw_size)
              VALUES(?,?,?,?,?,?,?)""",
              (sid,"rathena",path,zlib.compress(blob),
               "https://github.com/rathena/rathena","2026",len(blob)))
        for nid,sid,ord_,name in [(1,1,1,"Marianne#quest"),(2,1,2,"Morrison#quest"),
                                   (3,2,1,"Marianne#event"),(4,3,1,"Guard#generic")]:
            db.execute("""INSERT INTO script_npcs
               (id,source_id,ordinal,name,map,x,y,line_start,line_end)
               VALUES(?,?,?,?,?,?,?,?,?)""",
               (nid,sid,ord_,name,"prt_in",5+nid,10+nid,15+nid,30+nid))
        for pid,title in [(1,"Lost Quest"),(2,"Already Linked"),
                          (3,"Other Lost Quest"),(4,"One Step Quest")]:
            db.execute("INSERT INTO wiki_pages(id,title,url) VALUES(?,?,?)",
                       (pid,title,"https://irowiki.org/classic/"+title.replace(" ","_")))
        for pid,no,txt in [(1,1,"Talk to Marianne about the juice"),
                           (1,2,"Speak to Morrison for instructions."),
                           (2,1,"Talk to Marianne"),
                           (2,2,"Speak to Morrison"),
                           (3,1,"Talk to a Guard"),
                           (4,1,"Talk to Marianne. Then talk to Morrison.")]:
            db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",(pid,no,"",txt))
        db.execute("INSERT INTO wiki_script_links VALUES(2,1,95,'exact_title_filename','candidate')")
        result=review_unlinked_scripts(db)
        self.assertEqual(result["metrics"]["unlinked_pages"],3)
        self.assertEqual(result["metrics"]["pages_with_proposals"],1)
        self.assertEqual(result["metrics"]["proposals"],1)
        self.assertEqual(db.execute("SELECT page_id,source_id,distinct_names,supporting_steps FROM wiki_missing_script_proposals").fetchall(),
                         [(1,1,2,2)])
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_missing_script_evidence").fetchone()[0],2)
        # Discovery is explicitly non-mutating for accepted/candidate links.
        self.assertEqual(db.execute("SELECT page_id,source_id FROM wiki_script_links").fetchall(),[(2,1)])
        self.assertEqual(db.execute("SELECT review_reason FROM wiki_missing_script_reviews WHERE page_id=4").fetchone()[0],
                         "insufficient_independent_npc_name_evidence")
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])
        review_unlinked_scripts(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_missing_script_proposals").fetchone()[0],1)
        extract_content(db)
        content_review=review_unclassified_content(db)
        self.assertGreaterEqual(content_review["total"],1)
        self.assertTrue(all(x["server_availability"]=="unknown" for x in content_review["quests"]))
        self.assertTrue(all(x["source_step_excerpts"] or x["steps_total"]==0 for x in content_review["quests"]))

if __name__=="__main__":
    unittest.main()
