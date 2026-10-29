"""All dossier rows must remain source-cited and never create playable links."""
import sqlite3
import unittest
import zlib

from build import SCHEMA as BASE
from enrich import ensure_enrichment_schema
from graph import SCHEMA as FLOW
from state import SCHEMA as STATE
from discovery import review_unlinked_scripts
from dossiers import create_dossiers,review_state,lexical_overlap

class DossierTests(unittest.TestCase):
    def make_db(self, seasonal=False):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript(BASE)
        ensure_enrichment_schema(db)
        db.executescript(FLOW)
        db.executescript(STATE)
        blob=b"// research test source\n"
        path="npc/quests/quests_amatsu.txt" if seasonal else "npc/quests/hidden_quest.txt"
        db.execute("""INSERT INTO provenance
          (id,project,path,content_zlib,source_url,stored_at,raw_size,blob_sha)
          VALUES(1,'rathena',?,?,?,?,?,'blobtest')""",
          (path,zlib.compress(blob),"https://github.com/rathena/rathena","2026",len(blob)))
        for nid,ordinal,name in [(1,1,"Marianne#q"),(2,2,"Morrison#q")]:
            db.execute("""INSERT INTO script_npcs
             (id,source_id,ordinal,name,map,x,y,line_start,line_end)
             VALUES(?,1,? ,?,'prt_in',20,22,?,?)""",
             (nid,ordinal,name,10*ordinal,10*ordinal+9))
        title="2014 Fourth of July Event" if seasonal else "Lost Quest"
        db.execute("INSERT INTO wiki_pages(id,title,url) VALUES(1,?,?)",
                   (title,"https://irowiki.org/classic/"+title.replace(" ","_")))
        db.execute("INSERT INTO wiki_steps VALUES(1,1,'','Talk to Marianne about treasure')")
        db.execute("INSERT INTO wiki_steps VALUES(1,2,'','Speak to Morrison to finish')")
        db.execute("""INSERT INTO script_events VALUES
        (1,1,11,'mes','"Treasure awaits"'),
        (1,2,21,'mes','"Welcome again"')""")
        db.execute("""INSERT INTO npc_state_facts
            (source_id,npc_ordinal,line_no,occurrence,role,object_id,argument,context_guard_line)
            VALUES(1,1,12,1,'quest_start',1001,'1001',NULL)""")
        db.execute("""INSERT INTO npc_flow_conditions
          (source_id,npc_ordinal,line_no,expression,end_line)
          VALUES(1,1,13,'if(checkquest(1001)==0)',17)""")
        return db

    def test_dossier_uses_matched_npc_blocks_and_never_automatically_links(self):
        db=self.make_db()
        discovery=review_unlinked_scripts(db)
        self.assertEqual(discovery["metrics"]["proposals"],1)
        result=create_dossiers(db)
        self.assertEqual(result["metrics"]["proposals_reviewed"],1)
        dossier=result["dossiers"][0]
        self.assertEqual(dossier["npc_dialogue_events"],2)
        self.assertEqual(dossier["literal_quest_ids_found_in_matched_npcs"],[1001])
        self.assertEqual(dossier["npc_guard_conditions"],1)
        self.assertEqual(dossier["matched_npcs"],2)
        self.assertEqual(dossier["review_state"],"dialogue_and_quest_state_present_review_required")
        self.assertFalse(dossier["can_auto_promote_to_quest_link"])
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_script_links").fetchone()[0],0)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_missing_script_dossier_facts").fetchone()[0],1)
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])
        create_dossiers(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_missing_script_dossiers").fetchone()[0],1)
        review_unlinked_scripts(db)
        create_dossiers(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_missing_script_dossiers").fetchone()[0],1)

    def test_seasonal_name_pair_cannot_be_reported_verified(self):
        db=self.make_db(seasonal=True)
        review_unlinked_scripts(db)
        d=create_dossiers(db)["dossiers"][0]
        self.assertIn("seasonal_source_identity_risk",d["review_flags"])
        self.assertEqual(d["review_state"],"seasonal_source_identity_check")
        self.assertFalse(d["can_auto_promote_to_quest_link"])

    def test_token_overlap_not_identity(self):
        self.assertIn("treasure",lexical_overlap("Return to the treasure stone","The treasure is hidden"))
        self.assertFalse(lexical_overlap("Talk to Marianne","See the maze"))
        self.assertEqual(review_state(0,0,0,0,[]),"npc_blocks_without_lexical_evidence")
        self.assertEqual(review_state(4,0,0,0,[]),"npc_dialogue_only_review_required")

if __name__=="__main__":
    unittest.main()
