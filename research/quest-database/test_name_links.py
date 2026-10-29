"""NPC name matching requires explicit dialogue cue and a strong script lead."""
import sqlite3
import unittest
import zlib
from build import SCHEMA
from enrich import ensure_enrichment_schema
from name_links import link_npc_name_mentions,explicit_mention,distinctive

class NpcNameLinkTests(unittest.TestCase):
    def test_no_generic_or_accidental_names(self):
        self.assertFalse(distinctive("Guard#1"))
        self.assertFalse(distinctive("NPC#1"))
        self.assertTrue(explicit_mention("Talk to Marianne in Prontera.", "Marianne#juice"))
        self.assertTrue(explicit_mention("Return to the Professor Higgins for a reward","Professor Higgins"))
        self.assertFalse(explicit_mention("Marianne is in Prontera.", "Marianne"))
        self.assertFalse(explicit_mention("Talk to MarianneX nearby.", "Marianne"))
        self.assertFalse(explicit_mention("Talk to a guard in Prontera.","Guard"))

    def test_only_strong_source_with_unique_named_npc(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(SCHEMA)
        ensure_enrichment_schema(db)
        for sid,path in ((1,"npc/quests/juice_maker.txt"),(2,"npc/quests/other.txt")):
            raw="prt_in,5,6,3 script Marianne 55,{ close; }\n"
            db.execute("""INSERT INTO provenance(id,project,path,content_zlib,source_url,stored_at,raw_size)
                VALUES(?,?,?,?,?,?,?)""",(sid,"rathena",path,zlib.compress(raw.encode()),
                                             "https://example.invalid","2026",len(raw)))
        db.execute("INSERT INTO wiki_pages(id,title,url) VALUES(1,'Juice Quest','https://irowiki.org/classic/Juice_Quest')")
        for step,text in enumerate(["Talk to Marianne about the fruit.", "Marianne explains the juice recipe."],1):
            db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",(1,step,"",text))
        db.execute("INSERT INTO wiki_script_links VALUES(1,1,95,'exact_title_filename','candidate')")
        db.execute("INSERT INTO wiki_script_links VALUES(1,2,60,'single_distinctive_filename_token','candidate')")
        for nid,sid in ((1,1),(2,2)):
            db.execute("INSERT INTO script_npcs(id,source_id,ordinal,map,x,y,name) VALUES(?,?,?,?,?,?,?)",
                        (nid,sid,1,"prt_in",5,6,"Marianne#one"))
        summary=link_npc_name_mentions(db)
        self.assertEqual(summary["new_name_based_npc_candidates"],1)
        self.assertEqual(db.execute("SELECT npc_id,score,evidence FROM wiki_step_npc_links").fetchone(),
                         (1,55,"explicit_npc_name_in_candidate_script"))
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])
        link_npc_name_mentions(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_step_npc_links").fetchone()[0],1)

if __name__=="__main__":
    unittest.main()
