"""Evidence augmentation must prefer corroborated NPC coordinates and explicit headers."""
import sqlite3
import unittest
import zlib
from enrich import ensure_enrichment_schema
from augment import augment_evidence, named_npc_in_step, title_in_header
from build import SCHEMA, scripts_from_tree

class AdditionalEvidenceTests(unittest.TestCase):
    def test_variant_source_tree_and_no_custom(self):
        entries=[
          {"type":"blob","path":"npc/quests/a.txt","size":110},
          {"type":"blob","path":"npc/pre-re/quests/b.txt","size":220},
          {"type":"blob","path":"npc/re/quests/c.txt","size":320},
          {"type":"blob","path":"npc/re/jobs/d.txt","size":320},
          {"type":"blob","path":"npc/pre-re/guides/e.txt","size":220},
          {"type":"blob","path":"npc/custom/quests/z.txt","size":40},
          {"type":"blob","path":"npc/re/mobs/monsters.txt","size":40},
          {"type":"blob","path":"npc/re/quests/huge.txt","size":999999},
        ]
        for provider in ("rathena","hercules"):
            chosen=scripts_from_tree(entries,provider)
            self.assertEqual(len(chosen),5)
            self.assertTrue(any(x["path"]=="npc/re/quests/c.txt" for x in chosen))
            self.assertTrue(any(x["path"]=="npc/pre-re/quests/b.txt" for x in chosen))
            self.assertFalse(any("custom" in x["path"] for x in chosen))

    def test_header_exact_title_not_generic_npc_or_mentions_in_dialogue(self):
        raw="// Juice Quest\n//= Learn how to make fruit juice.\nprt_in,10,10,3 script Test 52,{\n mes \"Juice Quest\";\n}"
        self.assertTrue(title_in_header("Juice Quest",raw))
        self.assertFalse(title_in_header("Merchant Quest",raw))
        self.assertFalse(title_in_header("Quest",raw))
        self.assertTrue(named_npc_in_step("Marianne#juice","Talk to Marianne in Prontera"))
        self.assertFalse(named_npc_in_step("Guard#quest","Speak to Guard at the gate"))

    def test_header_and_two_distinct_positions_without_name(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(SCHEMA)
        ensure_enrichment_schema(db)
        script1="// Juice Quest\nprt_in,49,172,3 script Marianne#juice 55,{\n mes \"Hello\";\n}\n"
        script2="// Other Event\nprt_in,47,173,3 script Morrison#juice 55,{\n mes \"Test\";\n}\n"
        for sid,path,raw in ((1,"npc/quests/juice_maker.txt",script1),(2,"npc/pre-re/quests/other.txt",script2)):
            db.execute("""INSERT INTO provenance (id,project,path,source_url,stored_at,raw_size,content_zlib)
                       VALUES(?,?,?,?,?,?,?)""",(sid,"rathena",path,"https://github.com/rathena/rathena","2026",len(raw),zlib.compress(raw.encode())))
        for nid,sid,name,coords in ((1,1,"Marianne#juice",(49,172)),(2,2,"Morrison#juice",(47,173)),(3,2,"Third NPC",(50,171))):
            db.execute("INSERT INTO script_npcs(id,source_id,ordinal,map,x,y,name) VALUES(?,?,?,?,?,?,?)",
                       (nid,sid,nid,"prt_in",coords[0],coords[1],name))
        db.execute("INSERT INTO wiki_pages(id,title,url,status) VALUES(1,'Juice Quest','https://irowiki.org/classic/Juice_Quest','walkthrough_parsed')")
        db.execute("INSERT INTO wiki_pages(id,title,url,status) VALUES(2,'Unrelated Event','https://irowiki.org/classic/Other','walkthrough_parsed')")
        db.execute("INSERT INTO wiki_steps VALUES(1,1,'','Talk to Marianne at prt_in 49,172')")
        db.execute("INSERT INTO wiki_steps VALUES(1,2,'','Speak to a boy at prt_in 47,173')")
        db.execute("INSERT INTO wiki_steps VALUES(1,3,'','Return to prt_in 50,171')")
        db.execute("INSERT INTO wiki_steps VALUES(2,1,'','Talk to a guard at prt_in 49,172')")
        result=augment_evidence(db)
        links=db.execute("SELECT page_id,source_id,score,method FROM wiki_script_links ORDER BY page_id,source_id").fetchall()
        self.assertTrue(any(row[0]==1 and row[1]==1 and row[2]>=82 for row in links))
        self.assertTrue(any(row[0]==1 and row[1]==2 and row[3]=="multiple_exact_npc_positions" for row in links))
        self.assertFalse(any(row[0]==2 for row in links)) # one coord alone is weak
        self.assertGreater(result["new_step_npc_links"],0)
        before=db.execute("SELECT count(*) FROM wiki_script_links").fetchone()[0]
        augment_evidence(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_script_links").fetchone()[0],before)
        self.assertFalse(db.execute("PRAGMA foreign_key_check").fetchall())

if __name__=="__main__":
    unittest.main()
