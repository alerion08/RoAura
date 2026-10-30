"""ROQuestie source-code transition audit for individually supported quest implementations.

A matched NPC block is not an executed script. This audit correlates the wiki's
visit order with possible variable writes, literal guard checks, menu branches
and explicit journal-completion commands. It does NOT execute an if/switch,
follow a conditional branch, or invent start/completion signals.
"""
from collections import Counter,defaultdict
import re
from adjudicate import SCHEMA as REVIEW_SCHEMA

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_transition_audits(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 wiki_visits INTEGER NOT NULL,
 distinct_npcs INTEGER NOT NULL,
 potential_hops INTEGER NOT NULL,
 literal_guard_correspondences INTEGER NOT NULL,
 unsupported_hops INTEGER NOT NULL,
 unresolved_menu_options INTEGER NOT NULL,
 guarded_state_facts INTEGER NOT NULL,
 explicit_complete_commands INTEGER NOT NULL,
 journal_completion_status TEXT NOT NULL,
 assurance TEXT NOT NULL DEFAULT 'static_code_only_no_branch_execution',
 PRIMARY KEY(page_id,source_id),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_source_decisions(page_id,source_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_transition_witnesses(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 from_step INTEGER NOT NULL,
 to_step INTEGER NOT NULL,
 from_npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 to_npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 symbol TEXT NOT NULL,
 evidence_type TEXT NOT NULL,
 from_write_line INTEGER,
 from_write_value TEXT,
 to_guard_line INTEGER,
 to_guard_expression TEXT,
 PRIMARY KEY(page_id,source_id,from_step,to_step,from_npc_id,to_npc_id,symbol),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_transition_audits(page_id,source_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_transition_completion_facts(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 line_no INTEGER NOT NULL,
 occurrence INTEGER NOT NULL,
 role TEXT NOT NULL,
 quest_id INTEGER,
 source_argument TEXT NOT NULL,
 PRIMARY KEY(page_id,source_id,npc_id,line_no,occurrence),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_transition_audits(page_id,source_id)
) WITHOUT ROWID;
"""

LITERAL = re.compile(r"^\s*(?:=\s*)?(\d{1,9})\s*$")
STOP_NAMES={"Zeny","BaseLevel","JobLevel","Weight","Class","ADVJOB"}
def trackable_symbol(name):
    return bool(name and not name.startswith((".@","@","$@")) and name not in STOP_NAMES)

def literal_write(value):
    m=LITERAL.fullmatch(value or "")
    return m.group(1) if m else None

def guard_tests_symbol(guard,symbol,value):
    # A matching literal comparison is source evidence only, not execution.
    # No condition evaluation, negation elimination, ELSE inference, or jump tracking.
    pattern=r"(?<![\w.@])"+re.escape(symbol)+r"\s*==\s*"+re.escape(value)+r"(?!\d)"
    return bool(re.search(pattern,guard or ""))

def audit_transitions(db):
    db.executescript(REVIEW_SCHEMA)
    db.executescript(SCHEMA)
    candidates=db.execute("""SELECT d.page_id,d.source_id,p.title,s.project,s.path,s.blob_sha
       FROM wiki_source_decisions d JOIN wiki_pages p ON p.id=d.page_id
       JOIN provenance s ON s.id=d.source_id
       WHERE d.decision='source_supported'
       ORDER BY p.title,s.project,s.path""").fetchall()
    reviewed_total=db.execute("SELECT count(*) FROM wiki_source_decisions").fetchone()[0]
    overview=Counter()
    results=[]
    with db:
        db.execute("DELETE FROM wiki_transition_completion_facts")
        db.execute("DELETE FROM wiki_transition_witnesses")
        db.execute("DELETE FROM wiki_transition_audits")
        for pid,sid,title,project,path,sha in candidates:
            # The stage-12 accepted review has already pinned specific NPC
            # and wiki-step witnesses. Do NOT fall back to unreviewed discovery.
            visits=[{"step_no":step,"npc_id":nid,"npc_name":name,
                     "npc_ordinal":ordinal,"script_line":line}
                    for step,nid,name,ordinal,line in db.execute(
                       """SELECT e.step_no,e.npc_id,e.npc_name,n.ordinal,e.script_line
                          FROM wiki_source_sequence_evidence e
                          JOIN script_npcs n ON n.id=e.npc_id
                          WHERE e.page_id=? AND e.source_id=?
                          ORDER BY e.step_no,e.npc_id""",(pid,sid))]
            if len({v["npc_id"] for v in visits})<2 or len({v["step_no"] for v in visits})<2:
                raise ValueError(f"Reviewed NPC/step evidence missing for {(pid,sid)}")
            visited={row["npc_ordinal"]:row["npc_id"] for row in visits}
            records=defaultdict(list)
            guard_map=defaultdict(list)
            menu_unresolved=0
            for ordinal,nid in visited.items():
                rows=db.execute("""SELECT line_no,occurrence,role,subject,object_id,
                           value_expr,argument,context_guard_line
                        FROM npc_state_facts WHERE source_id=? AND npc_ordinal=?
                        ORDER BY line_no,occurrence""",(sid,ordinal)).fetchall()
                for line,occ,role,subject,obj,value,arg,guard in rows:
                    if role=="variable_write" and trackable_symbol(subject):
                        records[ordinal].append({
                            "line":line,"subject":subject,"value":value,
                            "literal":literal_write(value),"guard_line":guard})
                guard_map[ordinal]=db.execute("""SELECT line_no,expression FROM npc_flow_conditions
                     WHERE source_id=? AND npc_ordinal=? ORDER BY line_no""",
                     (sid,ordinal)).fetchall()
                menu_unresolved+=db.execute("""SELECT count(*)
                   FROM npc_flow_options o JOIN npc_flow_points p ON p.id=o.point_id
                   WHERE p.source_id=? AND p.npc_ordinal=?
                    AND (o.target_line IS NULL OR o.resolution IN ('unresolved','dynamic_menu'))""",
                   (sid,ordinal)).fetchone()[0]
            terminal=[]
            for ordinal,nid in visited.items():
                for line,occ,role,obj,arg in db.execute(
                    """SELECT line_no,occurrence,role,object_id,argument
                       FROM npc_state_facts WHERE source_id=? AND npc_ordinal=?
                       AND role IN ('quest_complete','quest_erase')
                       ORDER BY line_no,occurrence""",(sid,ordinal)):
                    terminal.append({"npc_id":nid,"source_line":line,"occurrence":occ,
                                     "role":role,"quest_id":obj,
                                     "source_argument":arg[:260]})
            witnesses=[];matched=0;unsupported=0;hops=0
            for before,after in zip(visits,visits[1:]):
                if after["step_no"]==before["step_no"]:continue
                hops+=1
                a=before["npc_ordinal"];b=after["npc_ordinal"]
                if a==b:
                    kind="same_npc_revisit_no_execution_proof"
                    choices=[("",kind,None,None,None,None)]
                else:
                    before_by=defaultdict(list)
                    after_by=defaultdict(list)
                    for rec in records.get(a,[]):before_by[rec["subject"]].append(rec)
                    for rec in records.get(b,[]):after_by[rec["subject"]].append(rec)
                    both=sorted(set(before_by)&set(after_by))
                    choices=[]
                    for symbol in both:
                        corroboration=None
                        for r in before_by[symbol]:
                            if r["literal"] is None:continue
                            for guardline,expression in guard_map[b]:
                                # Only a condition directly associated with
                                # the destination NPC's write is corroboration.
                                # An unrelated check elsewhere in the same
                                # NPC script is not a quest transition.
                                if (guard_tests_symbol(expression,symbol,r["literal"])
                                    and any(z["guard_line"]==guardline
                                            for z in after_by[symbol])):
                                    corroboration=(r,guardline,expression)
                                    break
                            if corroboration:break
                        if corroboration:
                            r,line,expr=corroboration
                            choices.append((symbol,"literal_write_guard_correspondence_nonexecuted",
                                            r["line"],r["literal"],line,expr[:240]))
                            matched+=1
                        else:
                            choices.append((symbol,"shared_write_symbol_branch_unresolved",
                                            before_by[symbol][0]["line"],
                                            before_by[symbol][0]["literal"],None,None))
                    if not choices:
                        choices=[("","no_shared_written_symbol_between_npcs",None,None,None,None)]
                if not any(c[1]=="literal_write_guard_correspondence_nonexecuted" for c in choices):
                    unsupported+=1
                for symbol,kind,fromline,val,guardline,guardexpr in choices:
                    witnesses.append({
                        "from_step":before["step_no"],"to_step":after["step_no"],
                        "from_npc":before["npc_name"],"to_npc":after["npc_name"],
                        "from_npc_id":before["npc_id"],"to_npc_id":after["npc_id"],
                        "symbol":symbol or None,"evidence_type":kind,
                        "from_write_line":fromline,"from_write_value":val,
                        "to_guard_line":guardline,"to_guard_expression":guardexpr,
                        "assurance":"nonexecuted_source_comparison_only"})
            guarded=db.execute("""SELECT count(*) FROM npc_state_facts
                WHERE source_id=? AND context_guard_line IS NOT NULL
                AND npc_ordinal IN ("""+",".join("?" for _ in visited)+")",
                (sid,*visited.keys())).fetchone()[0] if visited else 0
            explicit_complete=sum(f["role"]=="quest_complete" for f in terminal)
            completion="literal_completequest_present_unattributed" if explicit_complete else (
                "erasequest_present_not_proof_of_completion" if terminal
                else "no_explicit_quest_journal_completion_in_matched_npcs")
            db.execute("""INSERT INTO wiki_transition_audits
             (page_id,source_id,wiki_visits,distinct_npcs,potential_hops,
              literal_guard_correspondences,unsupported_hops,unresolved_menu_options,
              guarded_state_facts,explicit_complete_commands,journal_completion_status)
             VALUES(?,?,?,?,?,?,?,?,?,?,?)""",
             (pid,sid,len(visits),len(visited),hops,matched,unsupported,
              menu_unresolved,guarded,explicit_complete,completion))
            for w in witnesses:
                db.execute("""INSERT OR IGNORE INTO wiki_transition_witnesses
                    (page_id,source_id,from_step,to_step,from_npc_id,to_npc_id,
                     symbol,evidence_type,from_write_line,from_write_value,
                     to_guard_line,to_guard_expression)
                     VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
                  (pid,sid,w["from_step"],w["to_step"],w["from_npc_id"],w["to_npc_id"],
                   w["symbol"] or "",w["evidence_type"],w["from_write_line"],
                   w["from_write_value"],w["to_guard_line"],w["to_guard_expression"]))
            for f in terminal:
                db.execute("""INSERT INTO wiki_transition_completion_facts
                     VALUES(?,?,?,?,?,?,?,?)""",
                     (pid,sid,f["npc_id"],f["source_line"],f["occurrence"],
                      f["role"],f["quest_id"],f["source_argument"]))
            result={"title":title,"source_project":project,"source_path":path,
                    "source_blob_sha":sha,"source_review":"source_supported",
                    "wiki_visits":len(visits),"distinct_matched_npcs":len(visited),
                    "potential_wiki_visit_hops":hops,
                    "literal_write_guard_correspondences":matched,
                    "hops_without_correspondence":unsupported,
                    "unresolved_menu_options":menu_unresolved,
                    "guarded_source_state_facts":guarded,
                    "journal_completion_status":completion,
                    "completion_command_evidence":terminal[:60],
                    "wiki_order_vs_static_source_evidence":witnesses,
                    "branches_executed":False,"complete_route_verified":False,
                    "server_availability_verified":False,
                    "safe_for_automatic_dialogue":False}
            results.append(result)
            overview["reviewed_source_implementations"]+=1
            overview["potential_hops"]+=hops
            overview["literal_guard_correspondences"]+=matched
            overview["hops_without_correspondence"]+=unsupported
            overview["unresolved_menu_options"]+=menu_unresolved
            overview["source_quest_complete_commands"]+=explicit_complete
            overview[completion]+=1
    overview["excluded_non_supported_decisions"]=reviewed_total-len(candidates)
    overview["transition_witnesses"]=db.execute("SELECT count(*) FROM wiki_transition_witnesses").fetchone()[0]
    return {"version":1,"scope":"static source/guide analysis, no branch execution",
            "summary":dict(overview),"quests":results,
            "unverified":["actual branch execution","full progression from start to completion",
                          "dynamic menus","server-specific quest implementation and completion"]}
