"""Audit a fixed decision manifest against exact source snapshots and wiki NPC order.

Decisions concern source file identity ONLY. They never prove that a game
server enables a specific quest, nor that dynamic branches execute in order.
"""
import json
from collections import Counter,defaultdict
from pathlib import Path
from discovery import SCHEMA as DISCOVERY_SCHEMA

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_source_decisions(
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 source_blob_sha TEXT NOT NULL,
 decision TEXT NOT NULL CHECK(decision IN ('source_supported','rejected','variant_conflict','needs_review')),
 reason TEXT NOT NULL,
 reviewer TEXT NOT NULL DEFAULT 'source_review_stage12',
 status_scope TEXT NOT NULL DEFAULT 'source_identity_not_gameplay',
 PRIMARY KEY(page_id,source_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_source_sequence_evidence(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 step_no INTEGER NOT NULL,
 npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 npc_name TEXT NOT NULL,
 script_line INTEGER NOT NULL,
 PRIMARY KEY(page_id,source_id,step_no,npc_id),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_source_decisions(page_id,source_id),
 FOREIGN KEY(page_id,step_no) REFERENCES wiki_steps(page_id,step_no)
) WITHOUT ROWID;
"""

def source_sequence(db,pid,sid):
    visits=db.execute("""SELECT e.step_no,e.npc_id,n.name,n.ordinal,n.line_start
       FROM wiki_missing_script_evidence e JOIN script_npcs n ON n.id=e.npc_id
       WHERE e.page_id=? AND e.source_id=? ORDER BY e.step_no,n.id""",
       (pid,sid)).fetchall()
    ordinals={r[3] for r in visits}
    shared=defaultdict(lambda:defaultdict(list))
    for ordinal,subject,ln,value in db.execute(
        """SELECT npc_ordinal,subject,line_no,value_expr FROM npc_state_facts
           WHERE source_id=? AND role='variable_write'
             AND subject IS NOT NULL ORDER BY line_no""",(sid,)):
        if ordinal not in ordinals:continue
        if subject.startswith((".@","@","$@")) or subject in {"Zeny","BaseLevel","JobLevel","Weight"}:continue
        shared[subject][ordinal].append({"line_no":ln,"value_expression":(value or "")[:120]})
    common={k:v for k,v in shared.items() if len(v)>=2}
    return {"visits":[{"step_no":step,"npc_id":nid,"npc_name":name,
                        "npc_ordinal":ordinal,"script_line":ln}
                       for step,nid,name,ordinal,ln in visits],
            "unique_npcs":len({v[1] for v in visits}),
            "independent_steps":len({v[0] for v in visits}),
            "shared_write_variables":common,
            "execution_order_verified":False}

def adjudicate(db,manifest):
    db.executescript(DISCOVERY_SCHEMA)
    db.executescript(SCHEMA)
    proposal_rows=db.execute("""SELECT p.id,s.id,p.title,p.url,s.project,s.path,s.blob_sha
       FROM wiki_missing_script_proposals d
       JOIN wiki_pages p ON p.id=d.page_id
       JOIN provenance s ON s.id=d.source_id
       ORDER BY p.title,s.project,s.path""").fetchall()
    proposals={(title,project,path):(pid,sid,url,sha)
               for pid,sid,title,url,project,path,sha in proposal_rows}
    supplied={}
    for r in manifest["decisions"]:
        key=(r["guide_title"],r["source_project"],r["source_path"])
        if key in supplied:raise ValueError("Duplicate decision "+str(key))
        supplied[key]=r
    if set(supplied)!=set(proposals):
        raise ValueError("Missing or extra per-proposal decisions: "+str(
                         {"missing":sorted(set(proposals)-set(supplied)),
                          "extra":sorted(set(supplied)-set(proposals))}))
    result=[]
    with db:
        db.execute("DELETE FROM wiki_source_sequence_evidence")
        db.execute("DELETE FROM wiki_source_decisions")
        for key in sorted(proposals):
            pid,sid,url,sha=proposals[key]
            row=supplied[key]
            if row["source_blob_sha"]!=sha or row["guide_url"]!=url:
                raise ValueError("Manifest source snapshot changed: "+str(key))
            decision=row["decision"]
            if decision not in {"source_supported","rejected","variant_conflict","needs_review"}:
                raise ValueError("Invalid decision for "+str(key))
            if len(row.get("reason",""))<35:raise ValueError("Specific reason required for "+str(key))
            seq=source_sequence(db,pid,sid)
            if seq["unique_npcs"]<2 or seq["independent_steps"]<2:
                raise ValueError("Independent NPC evidence lost "+str(key))
            names={r["npc_name"].split("#")[0].casefold() for r in seq["visits"]}
            witnesses=row.get("source_witnesses",[])
            if len(witnesses)<2 or any(x.split("#")[0].casefold() not in names for x in witnesses):
                raise ValueError("Invalid NPC witnesses for "+str(key))
            shared=row.get("shared_variable")
            if shared and shared not in seq["shared_write_variables"]:
                raise ValueError("Shared variable not supported in two NPC blocks: "+str((key,shared)))
            if decision=="source_supported" and not row.get("support_basis"):
                raise ValueError("Supported source needs corroboration basis "+str(key))
            db.execute("""INSERT INTO wiki_source_decisions(
                 page_id,source_id,source_blob_sha,decision,reason)
                 VALUES(?,?,?,?,?)""",
                 (pid,sid,sha,decision,row["reason"]))
            for visit in seq["visits"]:
                db.execute("""INSERT OR IGNORE INTO wiki_source_sequence_evidence
                    VALUES(?,?,?,?,?,?)""",(pid,sid,visit["step_no"],
                      visit["npc_id"],visit["npc_name"],visit["script_line"]))
            result.append({
                "guide_title":key[0],"guide_url":url,
                "source_project":key[1],"source_path":key[2],
                "source_blob_sha":sha,"decision":decision,
                "reason":row["reason"],"support_basis":row.get("support_basis",[]),
                "source_witnesses":witnesses,"shared_variable":shared,
                "wiki_npc_visit_sequence":seq["visits"],
                "shared_npc_variable_write_facts":seq["shared_write_variables"],
                "source_code_execution_order_verified":False,
                "status_scope":"source-file identity only; not a verified full quest route",
                "approved_for_automatic_gameplay":False
            })
    counts=Counter(x["decision"] for x in result)
    counts["total"]=len(result)
    counts["source_sequence_rows"]=db.execute("SELECT count(*) FROM wiki_source_sequence_evidence").fetchone()[0]
    return {"schema_version":1,"summary":dict(counts),
            "assurance":"individually reviewed source identity; dynamic gameplay and all server variants remain unverified",
            "decisions":result}
