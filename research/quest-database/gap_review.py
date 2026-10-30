"""Deep but non-executing investigation of ROQuestie unsupported wiki transitions.

Every row has pinned stage-12 source identity and stage-13 unsupported edge
provenance. Alternative branches, item exchanges, and journal terminal
commands remain *candidate evidence*; no game state, AND/OR, dynamic menu
selection, or completion is inferred.
"""
from collections import Counter,defaultdict
import json
import re
from transitions import SCHEMA as TRANSITION_SCHEMA,trackable_symbol,literal_write

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_gap_reviews(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 from_step INTEGER NOT NULL,
 to_step INTEGER NOT NULL,
 from_npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 to_npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 gap_class TEXT NOT NULL,
 branch_hints INTEGER NOT NULL,
 item_source_facts INTEGER NOT NULL,
 potential_item_handoffs INTEGER NOT NULL,
 terminal_source_facts INTEGER NOT NULL,
 review_state TEXT NOT NULL DEFAULT 'not_proven_static_evidence_only',
 evidence_json TEXT NOT NULL,
 PRIMARY KEY(page_id,source_id,from_step,to_step,from_npc_id,to_npc_id),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_transition_audits(page_id,source_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_terminal_reviews(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 journal_start_facts INTEGER NOT NULL,
 journal_complete_facts INTEGER NOT NULL,
 journal_erase_facts INTEGER NOT NULL,
 matching_literal_quest_ids INTEGER NOT NULL,
 terminal_verdict TEXT NOT NULL,
 evidence_json TEXT NOT NULL,
 PRIMARY KEY(page_id,source_id),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_transition_audits(page_id,source_id)
) WITHOUT ROWID;
"""
GUARD_SYMBOL = r"(?<![\w.@]){symbol}(?![\w])"

def has_symbol(text,symbol):
    return bool(re.search(GUARD_SYMBOL.format(symbol=re.escape(symbol)),text or ""))

def item_role_rows(db,sid,ordinal,npc_id,limit=120):
    return [{"npc_id":npc_id,"line_no":ln,"occurrence":occ,"role":role,
             "item_id":obj,"amount_expr":amount,"argument":arg[:260],
             "guard_line":guard}
            for ln,occ,role,obj,amount,arg,guard in db.execute(
                """SELECT line_no,occurrence,role,object_id,amount_expr,
                          argument,context_guard_line FROM npc_state_facts
                   WHERE source_id=? AND npc_ordinal=?
                     AND role IN ('item_check','item_consume','item_grant')
                   ORDER BY line_no,occurrence LIMIT ?""",(sid,ordinal,limit))]

def npc_conditions(db,sid,ordinal,limit=100):
    return [{"line_no":ln,"expression":expr[:320],"end_line":end}
            for ln,expr,end in db.execute(
                """SELECT line_no,expression,end_line FROM npc_flow_conditions
                   WHERE source_id=? AND npc_ordinal=? ORDER BY line_no LIMIT ?""",
                (sid,ordinal,limit))]

def npc_menus(db,sid,ordinal,limit=100):
    return [{"line_no":line,"kind":kind,"option_no":no,"label":label,
             "target_line":target,"resolution":resolution,
             "is_resolved_target":target is not None}
            for line,kind,no,label,target,resolution in db.execute(
                """SELECT p.line_no,p.kind,o.option_no,o.label,o.target_line,o.resolution
                   FROM npc_flow_points p JOIN npc_flow_options o ON o.point_id=p.id
                   WHERE p.source_id=? AND p.npc_ordinal=?
                   ORDER BY p.line_no,o.option_no LIMIT ?""",
                (sid,ordinal,limit))]

def npc_writes(db,sid,ordinal,limit=250):
    return [{"line_no":ln,"symbol":subject,"value_expr":value,
             "literal_value":literal_write(value),"guard_line":guard}
            for ln,subject,value,guard in db.execute(
                """SELECT line_no,subject,value_expr,context_guard_line
                   FROM npc_state_facts
                   WHERE source_id=? AND npc_ordinal=? AND role='variable_write'
                   ORDER BY line_no LIMIT ?""",(sid,ordinal,limit))
            if trackable_symbol(subject)]

def terminal_audit(db,pid,sid,visited):
    # These commands are from all named NPC blocks, not necessarily the same
    # quest! A matching quest ID is a source correlation, not completion proof.
    rows=[]
    for ordinal,nid in visited.items():
        for ln,occ,role,obj,arg,guard in db.execute(
                """SELECT line_no,occurrence,role,object_id,argument,context_guard_line
                   FROM npc_state_facts WHERE source_id=? AND npc_ordinal=?
                     AND role IN ('quest_start','quest_complete','quest_erase')
                   ORDER BY line_no,occurrence""",(sid,ordinal)):
            rows.append({"npc_id":nid,"line_no":ln,"occurrence":occ,"role":role,
                         "quest_id":obj,"argument":arg[:260],
                         "guard_line":guard})
    starts={r["quest_id"] for r in rows if r["role"]=="quest_start"
            and r["quest_id"] is not None}
    finishes={r["quest_id"] for r in rows if r["role"]=="quest_complete"
              and r["quest_id"] is not None}
    joined=sorted(starts&finishes)
    completed=[r for r in rows if r["role"]=="quest_complete"]
    erased=[r for r in rows if r["role"]=="quest_erase"]
    if joined:status="matching_literal_journal_start_complete_not_executed"
    elif completed:status="completequest_present_without_matching_start_in_named_npcs"
    elif erased:status="erasequest_only_not_a_completion_signal"
    else:status="no_literal_journal_completion_in_named_npcs"
    return {"start_commands":sum(r["role"]=="quest_start" for r in rows),
            "complete_commands":len(completed),"erase_commands":len(erased),
            "matching_literal_ids":joined,"terminal_status":status,
            "source_command_evidence":rows,
            "full_quest_terminal_path_verified":False}

def audit_gaps(db):
    db.executescript(TRANSITION_SCHEMA)
    db.executescript(SCHEMA)
    source_roots=db.execute("""SELECT a.page_id,a.source_id,p.title,s.project,
                                    s.path,s.blob_sha
                             FROM wiki_transition_audits a
                             JOIN wiki_pages p ON p.id=a.page_id
                             JOIN provenance s ON s.id=a.source_id
                             JOIN wiki_source_decisions d ON d.page_id=a.page_id
                                AND d.source_id=a.source_id
                             WHERE d.decision='source_supported'
                             ORDER BY p.title,s.project,s.path""").fetchall()
    source_decisions=db.execute(
        "SELECT count(*) FROM wiki_source_decisions WHERE decision='source_supported'").fetchone()[0]
    if len(source_roots)!=source_decisions:
        raise ValueError("Incomplete source-supported transition audit")
    gap_rows=db.execute("""SELECT w.page_id,w.source_id,w.from_step,w.to_step,
                                  w.from_npc_id,w.to_npc_id,w.evidence_type
                          FROM wiki_transition_witnesses w
                          WHERE w.evidence_type!='literal_write_guard_correspondence_nonexecuted'
                          ORDER BY w.page_id,w.source_id,w.from_step,w.to_step,w.from_npc_id,w.to_npc_id""").fetchall()
    # One gap per ordered wiki NPC pair. Several different common variables
    # are evidence on the same hop; they are NOT separate quest transitions.
    gaps_by_pair=defaultdict(set)
    for pid,sid,step1,step2,nid1,nid2,kind in gap_rows:
        gaps_by_pair[(pid,sid,step1,step2,nid1,nid2)].add(kind)
    names={(pid,sid):(title,project,path,sha) for pid,sid,title,project,path,sha in source_roots}
    if any((pid,sid) not in names for pid,sid,*_ in gaps_by_pair):
        raise ValueError("Unreviewed source proposal entered unsupported-edge report")
    npc_cache={}
    def get_npc(sid,nid):
        key=(sid,nid)
        if key not in npc_cache:
            found=db.execute("""SELECT source_id,ordinal,name,line_start
                FROM script_npcs WHERE id=?""",(nid,)).fetchone()
            if found is None or found[0]!=sid:raise ValueError("NPC outside pinned script")
            _,ordinal,name,line=found
            npc_cache[key]={"name":name,"ordinal":ordinal,"line":line,
                            "writes":npc_writes(db,sid,ordinal),
                            "guards":npc_conditions(db,sid,ordinal),
                            "menus":npc_menus(db,sid,ordinal),
                            "items":item_role_rows(db,sid,ordinal,nid)}
        return npc_cache[key]
    counts=Counter()
    result=[]
    terminal_results=[]
    with db:
        db.execute("DELETE FROM wiki_gap_reviews")
        db.execute("DELETE FROM wiki_terminal_reviews")
        for pid,sid,title,project,path,sha in source_roots:
            visits=db.execute("""SELECT DISTINCT n.ordinal,n.id
                 FROM wiki_source_sequence_evidence e JOIN script_npcs n ON n.id=e.npc_id
                 WHERE e.page_id=? AND e.source_id=?""",(pid,sid)).fetchall()
            terminal=terminal_audit(db,pid,sid,{ordinal:nid for ordinal,nid in visits})
            db.execute("""INSERT INTO wiki_terminal_reviews
                (page_id,source_id,journal_start_facts,journal_complete_facts,
                 journal_erase_facts,matching_literal_quest_ids,terminal_verdict,evidence_json)
                 VALUES(?,?,?,?,?,?,?,?)""",
                 (pid,sid,terminal["start_commands"],terminal["complete_commands"],
                  terminal["erase_commands"],len(terminal["matching_literal_ids"]),
                  terminal["terminal_status"],json.dumps(terminal,ensure_ascii=False)))
            terminal_results.append({"title":title,"project":project,"path":path,
                                     "source_blob_sha":sha,**terminal})
            counts["terminal_"+terminal["terminal_status"]]+=1
            counts["matched_journal_start_complete_literal_ids"]+=len(terminal["matching_literal_ids"])
        for (pid,sid,step1,step2,nid1,nid2),types in sorted(gaps_by_pair.items()):
            title,project,path,sha=names[pid,sid]
            first=get_npc(sid,nid1);second=get_npc(sid,nid2)
            a=first["writes"];b=second["writes"]
            a_symbols={r["symbol"] for r in a}
            b_symbols={r["symbol"] for r in b}
            shared=sorted(a_symbols&b_symbols)
            # Investigate reads even if the next NPC does not write the
            # same variable. This is a *weaker* witness, never an upgrade to
            # the four already strictly guard+write corroborated edges.
            read_only=[]
            for symbol in sorted(a_symbols):
                source_values=[r for r in a if r["symbol"]==symbol and r["literal_value"]]
                if not source_values:continue
                for dest_guard in second["guards"]:
                    for w in source_values:
                        if (has_symbol(dest_guard["expression"],symbol) and
                            re.search(r"(?<![\w.@])"+re.escape(symbol)+r"\s*==\s*"+
                                      re.escape(w["literal_value"])+r"(?!\d)",
                                      dest_guard["expression"])):
                            read_only.append({"symbol":symbol,"write_line":w["line_no"],
                                              "literal_value":w["literal_value"],
                                              "destination_guard_line":dest_guard["line_no"],
                                              "destination_guard":dest_guard["expression"],
                                              "target_variable_write_not_established":True})
                            break
            item_a=first["items"];item_b=second["items"]
            # An item handed to a later NPC is an evidence candidate only
            # if its source operation is an actual getitem and the next NPC
            # explicitly checks/consumes the same literal item ID.
            acquired={r["item_id"] for r in item_a if r["role"]=="item_grant"
                      and r["item_id"] is not None}
            tested={r["item_id"] for r in item_b if r["role"] in ("item_check","item_consume")
                    and r["item_id"] is not None}
            handoffs=sorted(acquired&tested)
            menu_count=len(first["menus"])+len(second["menus"])
            unresolved=sum(x["target_line"] is None
                           for x in first["menus"]+second["menus"])
            kind=("same_npc_revisit" if nid1==nid2 else
                  "shared_write_without_proven_target_guard" if shared else
                  "no_shared_written_variable")
            db.execute("""INSERT INTO wiki_gap_reviews
                (page_id,source_id,from_step,to_step,from_npc_id,to_npc_id,gap_class,
                 branch_hints,item_source_facts,potential_item_handoffs,
                 terminal_source_facts,evidence_json)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
                (pid,sid,step1,step2,nid1,nid2,kind,
                 len(read_only)+menu_count,len(item_a)+len(item_b),len(handoffs),
                 len(terminal_audit(db,pid,sid,{first["ordinal"]:nid1,second["ordinal"]:nid2})["source_command_evidence"]),
                 json.dumps({"first_npc":first,"second_npc":second,
                             "guard_reads_without_proven_target_write":read_only,
                             "item_handoffs":handoffs,"unresolved_menu_options":unresolved},
                            ensure_ascii=False)))
            row={"title":title,"source_project":project,"source_path":path,
                 "source_blob_sha":sha,"from_step":step1,"to_step":step2,
                 "from_npc":first["name"],"to_npc":second["name"],
                 "gap_class":kind,"original_evidence_types":sorted(types),
                 "shared_written_variables":shared,
                 "candidate_literal_guard_reads":read_only[:20],
                 "first_npc_source_writes":a[:40],
                 "second_npc_source_writes":b[:40],
                 "first_npc_guards":first["guards"][:35],
                 "second_npc_guards":second["guards"][:35],
                 "first_npc_menus":first["menus"][:45],
                 "second_npc_menus":second["menus"][:45],
                 "first_npc_item_operations":item_a[:35],
                 "second_npc_item_operations":item_b[:35],
                 "literal_item_handoff_candidates":handoffs,
                 "unresolved_menu_options_in_two_npcs":unresolved,
                 "guards_executed":False,"item_requirements_proven":False,
                 "transition_verified":False,"safe_for_automatic_navigation":False}
            result.append(row)
            counts["gaps"]+=1;counts[kind]+=1
            counts["gaps_with_literal_read_candidates"]+=bool(read_only)
            counts["literal_guard_read_hints"]+=len(read_only)
            counts["gaps_with_item_handoff_candidates"]+=bool(handoffs)
            counts["literal_item_handoffs"]+=len(handoffs)
            counts["gaps_with_any_item_source_facts"]+=bool(item_a or item_b)
            counts["gaps_with_menu_source_facts"]+=bool(menu_count)
            counts["gaps_with_unresolved_menus"]+=bool(unresolved)
    expected=db.execute("SELECT sum(unsupported_hops) FROM wiki_transition_audits").fetchone()[0] or 0
    if len(result)!=expected:
        raise ValueError(f"Gap rows {len(result)} do not match stage13 unsupported hops {expected}")
    counts["terminal_audits"]=len(terminal_results)
    counts["supported_source_variants"]=len(source_roots)
    return {"version":1,"scope":"static nonexecuted branch, item and completion research",
            "summary":dict(counts),"unsupported_transitions":result,
            "terminal_state_reviews":terminal_results,
            "safety":{"source_code_branches_executed":False,
                      "item_requirement_logic_proven":False,
                      "complete_quest_routes_verified":False,
                      "automatic_game_navigation_enabled":False}}
