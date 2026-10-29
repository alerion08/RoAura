"""Conservative secondary quest-to-script evidence on top of filename matching.

Unverified research candidates only: header phrases and corroborated in-guide NPC
map/position evidence. Never auto-promote candidates to server-verified quests.
"""
from collections import defaultdict
import re
import zlib

from enrich import MAP_COORD, GENERIC_NPC, clean, tokens

HEADER_LINES=65
WEAK_TITLE={"quest","event","job","change","guide","the","a","of","for","story"}

def normalize(s):
    return re.sub(r"[^a-z0-9]+"," ",(s or "").casefold()).strip()

def title_in_header(title,raw):
    """Require exact ordered multiword title inside script's own lead comments."""
    needle=normalize(title)
    if not needle or len(needle)<10 or len(needle.split())<2 or not tokens(title):return False
    header=[]
    for line in raw.splitlines()[:HEADER_LINES]:
        stripped=line.strip()
        if stripped.startswith(("//","/*","*","//=")):
            header.append(stripped.lstrip("/*= -"))
    content=" "+normalize(" ".join(header))+" "
    return (" "+needle+" ") in content

def named_npc_in_step(name,step):
    display=clean(name.split("#",1)[0]).casefold()
    if len(display)<5 or display in GENERIC_NPC:return False
    if display in ("kafra employee","guard","warp","warper","shop","npc"):return False
    return bool(re.search(r"(?<![a-z])"+re.escape(display)+r"(?![a-z])",(step or "").casefold()))

def augment_evidence(db,max_new_per_page=8):
    """Expand script leads only where source comments or NPC map proof support it.

    Two distinct map+coordinate hits from steps, or a coordinate+name within a
    single step, are required. Otherwise a single map coordinate is too weak.
    """
    sources=db.execute("SELECT id,project,path,content_zlib FROM provenance").fetchall()
    page_rows=db.execute("SELECT id,title FROM wiki_pages WHERE status IN ('walkthrough_parsed','narrative_parsed')").fetchall()
    all_npcs=defaultdict(list)
    by_pos=defaultdict(list)
    for nid,sid,mapname,x,y,name in db.execute("SELECT id,source_id,map,x,y,name FROM script_npcs WHERE map IS NOT NULL"):
        record=(nid,sid,name)
        all_npcs[sid].append((nid,mapname,x,y,name))
        by_pos[(mapname.casefold(),x,y)].append(record)

    existing={(pid,sid):(score,method) for pid,sid,score,method in db.execute(
        "SELECT page_id,source_id,score,method FROM wiki_script_links")}
    candidates=defaultdict(dict)
    page_steps={pid:db.execute("SELECT step_no,instruction FROM wiki_steps WHERE page_id=? ORDER BY step_no",(pid,)).fetchall()
               for pid,_ in page_rows}
    header_leads=0
    for sid,project,path,packed in sources:
        try:raw=zlib.decompress(packed).decode("utf-8-sig",errors="replace")
        except Exception:continue
        for pid,title in page_rows:
            if title_in_header(title,raw):
                candidates[pid][sid]=(82,"script_header_exact_title")
                header_leads+=1

    strong_coordinate_pages=set()
    coord_matches=defaultdict(lambda:defaultdict(list))
    # Search the exact coordinates in each guide. Keep source evidence and line
    # records; do not apply fuzzy distance or assume that a match proves identity.
    for pid,_ in page_rows:
        for step,text in page_steps[pid]:
            seen=set()
            for m in MAP_COORD.finditer(text):
                pos=(m.group(1).casefold(),int(m.group(2)),int(m.group(3)))
                if pos in seen:continue
                seen.add(pos)
                for nid,sid,name in by_pos.get(pos,()):
                    named=named_npc_in_step(name,text)
                    coord_matches[pid][sid].append((step,pos,nid,named))
    for pid,per_source in coord_matches.items():
        for sid,hits in per_source.items():
            distinct={hit[1] for hit in hits}
            named=[h for h in hits if h[3]]
            if named:
                candidates[pid][sid]=max((candidates[pid].get(sid,(0,"")), (89,"npc_exact_position_and_name")),key=lambda x:x[0])
                strong_coordinate_pages.add(pid)
            elif len(distinct)>=2:
                candidates[pid][sid]=max((candidates[pid].get(sid,(0,"")), (78,"multiple_exact_npc_positions")),key=lambda x:x[0])
                strong_coordinate_pages.add(pid)

    inserted=0;improved=0;new_step_npcs=0
    with db:
        for pid,per_sid in candidates.items():
            options=sorted(per_sid.items(),key=lambda x:(-x[1][0],x[0]))
            used=0
            for sid,(score,reason) in options:
                old=existing.get((pid,sid))
                if old and old[0]>=score:continue
                if not old and used>=max_new_per_page:break
                db.execute("""INSERT INTO wiki_script_links(page_id,source_id,score,method,status)
                    VALUES(?,?,?,?,'candidate')
                    ON CONFLICT(page_id,source_id) DO UPDATE SET
                      score=excluded.score, method=excluded.method""",(pid,sid,score,reason))
                if old:improved+=1
                else:inserted+=1;used+=1
                existing[(pid,sid)]=(score,reason)
        # Add step-NPC evidence only where that source is now a known candidate.
        for pid,per_source in coord_matches.items():
            for sid,hits in per_source.items():
                if (pid,sid) not in existing:continue
                for step,pos,nid,named in hits:
                    reason="map_coord_and_npc_name" if named else "exact_map_coord"
                    score=95 if named else 85
                    before=db.total_changes
                    db.execute("""INSERT OR IGNORE INTO wiki_step_npc_links
                        (page_id,step_no,npc_id,score,evidence,status)
                        VALUES(?,?,?,?,?,'candidate')""",(pid,step,nid,score,reason))
                    if db.total_changes>before:new_step_npcs+=1

    return {
        "header_title_leads":header_leads,
        "pages_with_corroborated_coordinate_evidence":len(strong_coordinate_pages),
        "new_script_candidates":inserted,
        "stronger_existing_candidates":improved,
        "new_step_npc_links":new_step_npcs,
        "linked_pages":db.execute("SELECT count(DISTINCT page_id) FROM wiki_script_links").fetchone()[0],
        "script_candidates":db.execute("SELECT count(*) FROM wiki_script_links").fetchone()[0],
        "step_npc_candidates":db.execute("SELECT count(*) FROM wiki_step_npc_links").fetchone()[0],
    }
