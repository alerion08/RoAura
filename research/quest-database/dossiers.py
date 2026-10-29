"""Conservative code-level dossiers for previously discovered quest/script proposals.

Inspects the NPC blocks actually named by independent wiki guide steps.
Reports dialogue, state commands, branch/menu evidence and literal Quest IDs.
It NEVER links a candidate to the active catalog, promotes an NPC/quest, or
claims that lexical presence proves execution order or server availability.
"""
from collections import Counter,defaultdict
import re
from baseline import HISTORICAL
from enrich import tokens
from discovery import SCHEMA as DISCOVERY_SCHEMA

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_missing_script_dossiers(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 matched_npcs INTEGER NOT NULL,
 npc_dialogue_events INTEGER NOT NULL,
 npc_quest_state_facts INTEGER NOT NULL,
 npc_item_state_facts INTEGER NOT NULL,
 npc_variable_writes INTEGER NOT NULL,
 literal_quest_ids INTEGER NOT NULL,
 npc_menu_options INTEGER NOT NULL,
 npc_guard_conditions INTEGER NOT NULL,
 review_state TEXT NOT NULL,
 review_flags TEXT NOT NULL,
 assurance TEXT NOT NULL DEFAULT 'lexical_code_evidence_not_validated_quest',
 PRIMARY KEY(page_id,source_id),
 FOREIGN KEY(page_id,source_id)
  REFERENCES wiki_missing_script_proposals(page_id,source_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_missing_script_dossier_facts(
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 line_no INTEGER NOT NULL,
 occurrence INTEGER NOT NULL,
 role TEXT NOT NULL,
 object_id INTEGER,
 argument TEXT NOT NULL,
 context_guard_line INTEGER,
 PRIMARY KEY(page_id,source_id,npc_id,line_no,occurrence),
 FOREIGN KEY(page_id,source_id)
  REFERENCES wiki_missing_script_dossiers(page_id,source_id)
) WITHOUT ROWID;
"""

QUEST_ROLES={"quest_start","quest_complete","quest_erase","quest_check"}
ITEM_ROLES={"item_grant","item_consume","item_check"}
def lexical_overlap(guide,dialogue):
    """Non-generative token support: neither similarity nor score is proof."""
    guide_tokens={t for t in tokens(guide or "") if len(t)>=5}
    dialogue_tokens={t for t in tokens(dialogue or "") if len(t)>=5}
    return sorted(guide_tokens & dialogue_tokens)

def review_state(dialogue_count,quest_count,item_count,var_count,flags):
    # Source NPC block facts are never enough to declare a quest verified.
    if "seasonal_source_identity_risk" in flags:
        return "seasonal_source_identity_check"
    if not dialogue_count and not (quest_count or item_count or var_count):
        return "npc_blocks_without_lexical_evidence"
    if dialogue_count and quest_count:
        return "dialogue_and_quest_state_present_review_required"
    if dialogue_count and (item_count or var_count):
        return "dialogue_and_other_state_present_review_required"
    if dialogue_count:
        return "npc_dialogue_only_review_required"
    return "npc_state_only_review_required"

def create_dossiers(db,max_dialogue_examples=4,max_fact_examples=16):
    db.executescript(DISCOVERY_SCHEMA)
    db.executescript(SCHEMA)
    proposals=db.execute("""SELECT d.page_id,d.source_id,p.title,p.url,
       s.project,s.path,s.source_url,s.blob_sha
       FROM wiki_missing_script_proposals d
       JOIN wiki_pages p ON p.id=d.page_id
       JOIN provenance s ON s.id=d.source_id
       ORDER BY p.title,s.project,s.path""").fetchall()
    source_to_pages=defaultdict(set)
    for pid,sid,*_ in proposals:source_to_pages[sid].add(pid)
    counts=Counter()
    dossiers=[]
    with db:
        db.execute("DELETE FROM wiki_missing_script_dossier_facts")
        db.execute("DELETE FROM wiki_missing_script_dossiers")
        for pid,sid,title,url,project,path,source_url,sha in proposals:
            source=db.execute("""SELECT DISTINCT n.id,n.ordinal,n.name,n.map,
               n.x,n.y,n.line_start,n.line_end
               FROM wiki_missing_script_evidence e
               JOIN script_npcs n ON n.id=e.npc_id
               WHERE e.page_id=? AND e.source_id=?
               ORDER BY n.line_start,n.id""",(pid,sid)).fetchall()
            sections=[]
            dialogue_count=quest_count=item_count=variable_count=menu_count=guard_count=0
            quest_ids=set()
            all_guide=[]
            for (step,txt) in db.execute("""SELECT DISTINCT w.step_no,w.instruction
               FROM wiki_missing_script_evidence e JOIN wiki_steps w
               ON w.page_id=e.page_id AND w.step_no=e.step_no
               WHERE e.page_id=? AND e.source_id=?""",(pid,sid)):
                all_guide.append(txt)
            for nid,ordinal,name,mapname,x,y,line_start,line_end in source:
                interactions=db.execute("""SELECT e.step_no,w.instruction
                    FROM wiki_missing_script_evidence e JOIN wiki_steps w
                    ON w.page_id=e.page_id AND w.step_no=e.step_no
                    WHERE e.page_id=? AND e.source_id=? AND e.npc_id=?
                    ORDER BY e.step_no""",(pid,sid,nid)).fetchall()
                messages=db.execute("""SELECT line_no,command,argument FROM script_events
                    WHERE source_id=? AND npc_ordinal=?
                      AND command IN ('mes','mesf','npctalk')
                    ORDER BY line_no""",(sid,ordinal)).fetchall()
                facts=db.execute("""SELECT line_no,occurrence,role,subject,object_id,
                      amount_expr,value_expr,argument,context_guard_line
                    FROM npc_state_facts
                    WHERE source_id=? AND npc_ordinal=?
                    ORDER BY line_no,occurrence""",(sid,ordinal)).fetchall()
                menus=db.execute("""SELECT count(*) FROM npc_flow_options o
                    JOIN npc_flow_points p ON p.id=o.point_id
                    WHERE p.source_id=? AND p.npc_ordinal=?""",(sid,ordinal)).fetchone()[0]
                guards=db.execute("""SELECT count(*) FROM npc_flow_conditions
                    WHERE source_id=? AND npc_ordinal=?""",(sid,ordinal)).fetchone()[0]
                role_counts=Counter(f[2] for f in facts)
                local_qids=sorted({f[4] for f in facts if f[2] in QUEST_ROLES and f[4] is not None})
                quest_ids.update(local_qids)
                matching_guide=" ".join(v for _,v in interactions)
                # Only compare source literal positions within the SAME wiki step.
                # Missing/mismatching coordinates never disprove a quest identity.
                explicit_positions=db.execute("""SELECT w.step_no,d.value
                  FROM wiki_missing_script_evidence e
                  JOIN wiki_details d ON d.page_id=e.page_id AND d.step_no=e.step_no
                  JOIN wiki_steps w ON w.page_id=e.page_id AND w.step_no=e.step_no
                  WHERE e.page_id=? AND e.source_id=? AND e.npc_id=?
                    AND d.kind='map_coord'""",(pid,sid,nid)).fetchall()
                exact_position_steps=sorted({step for step,value in explicit_positions
                    if value==f"{(mapname or '').lower()}:{x}:{y}"})
                overlap=set()
                for ln,cmd,arg in messages:
                    overlap.update(lexical_overlap(matching_guide,arg))
                dialogue_count+=len(messages)
                quest_count+=sum(role_counts[k] for k in QUEST_ROLES)
                item_count+=sum(role_counts[k] for k in ITEM_ROLES)
                variable_count+=role_counts["variable_write"]
                menu_count+=menus
                guard_count+=guards
                for f in facts:
                    db.execute("""INSERT INTO wiki_missing_script_dossier_facts
                        (page_id,source_id,npc_id,line_no,occurrence,role,object_id,argument,context_guard_line)
                        VALUES(?,?,?,?,?,?,?,?,?)""",
                        (pid,sid,nid,f[0],f[1],f[2],f[4],f[7],f[8]))
                sections.append({
                    "npc_id":nid,"npc_ordinal":ordinal,"name":name,
                    "map":mapname,"x":x,"y":y,
                    "script_lines":[line_start,line_end],
                    "wiki_steps":[{"step_no":step,"text_excerpt":txt[:240]}
                                  for step,txt in interactions],
                    "dialogue_events":len(messages),
                    "dialogue_examples":[{"line_no":ln,"command":cmd,"source_argument":arg[:250]}
                                         for ln,cmd,arg in messages[:max_dialogue_examples]],
                    "source_token_overlap":sorted(overlap)[:25],
                    "wiki_map_coordinate_mentions_in_matched_steps":len(explicit_positions),
                    "exact_wiki_to_source_position_steps":exact_position_steps,
                    "state_roles":dict(role_counts),
                    "literal_quest_ids":local_qids,
                    "state_examples":[{"line_no":f[0],"occurrence":f[1],"role":f[2],
                                      "object_id":f[4],"amount_expr":f[5],
                                      "value_expr":f[6],"source_argument":f[7][:240],
                                      "guard_line":f[8]}
                                      for f in facts[:max_fact_examples]],
                    "menu_options":menus,"guard_conditions":guards
                })
            flags=[]
            if HISTORICAL.search(title) and not re.search(
                    r"(?:event|halloween|christmas|valentine|anniversary|festival)",path,re.I):
                flags.append("seasonal_source_identity_risk")
            if len(source_to_pages[sid])>1:
                flags.append("one_source_proposed_for_multiple_guides")
            if len(source)<2:
                flags.append("fewer_than_two_valid_npc_blocks")
            if not quest_count:
                flags.append("no_literal_or_dynamic_quest_state_commands_in_matched_npcs")
            if not guard_count:
                flags.append("no_explicit_condition_guards_in_matched_npcs")
            if not dialogue_count:
                flags.append("no_npc_dialogue_events")
            status=review_state(dialogue_count,quest_count,item_count,variable_count,flags)
            db.execute("""INSERT INTO wiki_missing_script_dossiers
               (page_id,source_id,matched_npcs,npc_dialogue_events,npc_quest_state_facts,
                npc_item_state_facts,npc_variable_writes,literal_quest_ids,
                npc_menu_options,npc_guard_conditions,review_state,review_flags)
               VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
               (pid,sid,len(source),dialogue_count,quest_count,item_count,variable_count,
                len(quest_ids),menu_count,guard_count,status,",".join(flags)))
            counts[status]+=1
            for flag in flags:counts["flag_"+flag]+=1
            dossiers.append({
                "guide_title":title,"guide_url":url,"source_project":project,
                "source_path":path,"source_url":source_url,"source_blob_sha":sha,
                "matched_npcs":len(source),"npc_dialogue_events":dialogue_count,
                "npc_quest_state_facts":quest_count,"npc_item_state_facts":item_count,
                "npc_variable_writes":variable_count,
                "npc_menu_options":menu_count,"npc_guard_conditions":guard_count,
                "literal_quest_ids_found_in_matched_npcs":sorted(quest_ids),
                "review_flags":flags,"review_state":status,
                "npc_evidence":sections,
                "npc_name_and_exact_coordinate_supported_steps":sum(
                    len(n["exact_wiki_to_source_position_steps"]) for n in sections),
                "assurance":"NPC source-code facts only; quest identity/state transition is unverified",
                "can_auto_promote_to_quest_link":False
            })
    counts["proposals_reviewed"]=len(proposals)
    counts["dossier_facts"]=db.execute("SELECT count(*) FROM wiki_missing_script_dossier_facts").fetchone()[0]
    return {"version":1,
            "meaning":"lexical source evidence, NOT confirmed end-to-end quest state or route",
            "metrics":dict(counts),"dossiers":dossiers}
