"""Explicit wiki talk-to-name hints joined to already plausible NPC source variants.

Never treat a bare matching name as confirmation of NPC identity. Requires a
strong pre-existing candidate script, distinctive unique NPC name in that
script and an explicit interaction verb before the name in the guide step.
"""
from collections import Counter,defaultdict
import re
from enrich import GENERIC_NPC,clean

INTERACT=re.compile(r"\b(?:talk to|speak (?:to|with)|meet|visit|ask|approach|return to)\s+",
                    re.I)
OTHER_GENERIC={"guard","shopkeeper","villager","citizen","merchant","guide",
               "adventurer","soldier","employee","recruiter","researcher","npc",
               "blacksmith","prontera guard","kafra","warper","assistant"}

def normalized_name(value):
    return clean((value or "").split("#",1)[0]).strip().casefold()

def distinctive(value):
    name=normalized_name(value)
    if len(name)<6 or name in GENERIC_NPC or name in OTHER_GENERIC:return False
    if re.search(r"[.@$%{}<>]",name):return False
    if re.fullmatch(r"(?:[a-z]*\d+|npc\s*\d+)",name):return False
    return bool(re.fullmatch(r"[\w' -]+",name))

def explicit_mention(text,name):
    """The exact NPC display name must immediately follow a speaking cue."""
    display=normalized_name(name)
    if not distinctive(name):return False
    for m in INTERACT.finditer(text or ""):
        tail=(text or "")[m.end():m.end()+len(display)+16]
        found=re.match(r"(?:the |a |an |npc )?"+re.escape(display)+r"(?![\w'-])",
                       tail,re.I)
        if found:return True
    return False

def link_npc_name_mentions(db):
    strong=("exact_title_filename","full_title_in_filename",
            "script_header_exact_title","npc_exact_position_and_name")
    placeholders=",".join("?" for _ in strong)
    links=db.execute(
        f"SELECT page_id,source_id FROM wiki_script_links WHERE score>=82 AND method IN ({placeholders})",
        strong).fetchall()
    by_page=defaultdict(set)
    for pid,sid in links:by_page[pid].add(sid)
    scripts={sid:db.execute(
        "SELECT id,name FROM script_npcs WHERE source_id=? AND map IS NOT NULL AND x IS NOT NULL AND y IS NOT NULL",
        (sid,)).fetchall() for sid in {sid for _,sid in links}}
    unique={}
    for sid,rows in scripts.items():
        counts=Counter(normalized_name(name) for _,name in rows if distinctive(name))
        unique[sid]=[(nid,name) for nid,name in rows
                     if distinctive(name) and counts[normalized_name(name)]==1]
    already=set(db.execute("SELECT page_id,step_no,npc_id FROM wiki_step_npc_links"))
    inserted=0;pages=set();steps=set()
    with db:
        for pid,sids in sorted(by_page.items()):
            step_rows=db.execute(
                "SELECT step_no,instruction FROM wiki_steps WHERE page_id=? ORDER BY step_no",
                (pid,)).fetchall()
            for step,txt in step_rows:
                for sid in sorted(sids):
                    for nid,name in unique[sid]:
                        key=(pid,step,nid)
                        if key in already or not explicit_mention(txt,name):continue
                        db.execute("""INSERT OR IGNORE INTO wiki_step_npc_links
                           (page_id,step_no,npc_id,score,evidence,status)
                           VALUES(?,?,?,55,'explicit_npc_name_in_candidate_script','candidate')""",key)
                        inserted+=1;already.add(key);pages.add(pid);steps.add((pid,step))
    return {"new_name_based_npc_candidates":inserted,
            "pages_with_name_based_links":len(pages),
            "steps_with_name_based_links":len(steps),
            "assurance":"explicit wiki name + candidate script only; NPC identity and map not verified"}
