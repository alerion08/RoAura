"""Source review decisions must be pinned to real NPC steps and state witnesses."""
import sqlite3,unittest
from build import SCHEMA as CORE
from enrich import ensure_enrichment_schema
from discovery import SCHEMA as DISCOVERY
from state import SCHEMA as STATE
from graph import SCHEMA as FLOW
from adjudicate import adjudicate

class AdjudicateTests(unittest.TestCase):
 def sample(self):
  db=sqlite3.connect(":memory:");db.execute("PRAGMA foreign_keys=ON")
  db.executescript(CORE);ensure_enrichment_schema(db);db.executescript(DISCOVERY)
  db.executescript(STATE);db.executescript(FLOW)
  db.execute("""INSERT INTO provenance(id,project,path,blob_sha,source_url,stored_at,raw_size,content_zlib)
                VALUES(1,'rathena','npc/quests/test.txt','sha1','https://example.invalid','2026',1,X'78')""")
  db.execute("INSERT INTO wiki_pages(id,title,url) VALUES(1,'Test Quest','https://irowiki.org/classic/Test_Quest')")
  for n,name in ((1,"Marianne#one"),(2,"Morrison#two")):
   db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",(1,n,"","Talk to "+name))
   db.execute("INSERT INTO script_npcs(id,source_id,ordinal,name,map,x,y,line_start,line_end) VALUES(?,1,?,?,'prt_in',20,30,?,?)",
     (n,n,name,n*10,n*10+8))
   db.execute("INSERT INTO wiki_missing_script_evidence VALUES(1,1,?,?,?,?,?)",
              (n,n,name,n*10))
   db.execute("""INSERT INTO npc_state_facts
      (source_id,npc_ordinal,line_no,occurrence,role,subject,value_expr,argument)
      VALUES(1,?,?,1,'variable_write','questflag',?,?)""",
      (n,n*10+1,str(n),str(n)))
  db.execute("INSERT INTO wiki_missing_script_proposals VALUES(1,1,2,2,'unverified_needs_manual_review','npc_name_pair_candidate')")
  return db
 def manifest(self,sha="sha1"):
  return {"decisions":[{"guide_title":"Test Quest","guide_url":"https://irowiki.org/classic/Test_Quest",
    "source_project":"rathena","source_path":"npc/quests/test.txt",
    "source_blob_sha":sha,"decision":"source_supported",
    "reason":"Both distinct NPCs write a shared state variable and are referenced in separate guide steps.",
    "source_witnesses":["Marianne","Morrison"],"shared_variable":"questflag",
    "support_basis":["source state variable witness"]}]}
 def test_validates_source_sequence_without_promoting_gameplay(self):
  db=self.sample();result=adjudicate(db,self.manifest())
  self.assertEqual(result["summary"]["source_supported"],1)
  x=result["decisions"][0]
  self.assertEqual([z["step_no"] for z in x["wiki_npc_visit_sequence"]],[1,2])
  self.assertEqual(x["shared_npc_variable_write_facts"]["questflag"][1][0]["value_expression"],"1")
  self.assertFalse(x["source_code_execution_order_verified"])
  self.assertFalse(x["approved_for_automatic_gameplay"])
  self.assertEqual(db.execute("SELECT count(*) FROM wiki_script_links").fetchone()[0],0)
  self.assertFalse(db.execute("PRAGMA foreign_key_check").fetchall())
  adjudicate(db,self.manifest())
  self.assertEqual(db.execute("SELECT count(*) FROM wiki_source_decisions").fetchone()[0],1)
 def test_rejects_stale_source_or_missing_review(self):
  db=self.sample()
  with self.assertRaisesRegex(ValueError,"changed"):adjudicate(db,self.manifest("wrong"))
  with self.assertRaisesRegex(ValueError,"Missing or extra"):adjudicate(db,{"decisions":[]})
if __name__=="__main__":unittest.main()
