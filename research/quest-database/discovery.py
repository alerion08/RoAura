"""High-precision *review proposals* for wiki guides without linked NPC scripts.

Distinctive NPC names must be explicitly introduced by guide interaction verbs
in TWO DIFFERENT guide steps and must co-occur in the SAME indexed emulator
script. Matching script sources are never automatically linked to guides.
A review proposal is NOT a walkthrough, a verified NPC or a server guarantee.
"""
from collections import Counter,defaultdict
import re

from name_links import INTERACT,distinctive,normalized_name

SCHEMA = """
CREATE TABLE IF NOT EXISTS wiki_missing_script_reviews (
 page_id INTEGER PRIMARY KEY REFERENCES wiki_pages(id),
 guide_steps INTEGER NOT NULL,
 explicit_interactions INTEGER NOT NULL,
 matched_npc_mentions INTEGER NOT NULL,
 proposal_count INTEGER NOT NULL,
 review_reason TEXT NOT NULL,
 assurance TEXT NOT NULL DEFAULT 'research_only_no_script_verification'
);
CREATE TABLE IF NOT EXISTS wiki_missing_script_proposals (
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 distinct_names INTEGER NOT NULL CHECK(distinct_names>=2),
 supporting_steps INTEGER NOT NULL CHECK(supporting_steps>=2),
 review_state TEXT NOT NULL DEFAULT 'unverified_needs_manual_review',
 assurance TEXT NOT NULL DEFAULT 'npc_name_pair_candidate',
 PRIMARY KEY(page_id,source_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_missing_script_evidence (
 page_id INTEGER NOT NULL,
 source_id INTEGER NOT NULL,
 step_no INTEGER NOT NULL,
 npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 npc_display_name TEXT NOT NULL,
 npc_line INTEGER,
 PRIMARY KEY(page_id,source_id,step_no,npc_id),
 FOREIGN KEY(page_id,source_id) REFERENCES wiki_missing_script_proposals(page_id,source_id),
 FOREIGN KEY(page_id,step_no) REFERENCES wiki_steps(page_id,step_no)
) WITHOUT ROWID;
"""

def interaction_names(text,by_first):
    """Return exact names following NPC speaking cues; do not tokenize arbitrary prose."""
    identified=set()
    cue_count=0
    for cue in INTERACT.finditer(text or ""):
        cue_count+=1
        tail=(text or "")[cue.end():].casefold().lstrip()
        tail=re.sub(r"^(?:the |a |an |npc )","",tail)
        first=re.match(r"[\w']+",tail)
        if not first:continue
        for name in by_first.get(first.group(),()):
            if re.match(re.escape(name)+r"(?![\w'-])",tail):
                identified.add(name)
    return cue_count,identified

def review_unlinked_scripts(db,max_sources_per_name=8,max_proposals_per_page=12):
    """Source-cited queue for human verification; never updates wiki_script_links."""
    db.executescript(SCHEMA)
    # A display name repeated inside one source can identify several NPCs.
    # Exclude those names rather than pretending we know which NPC is intended.
    rows=db.execute("""SELECT id,source_id,name,line_start FROM script_npcs
        WHERE map IS NOT NULL AND x IS NOT NULL AND y IS NOT NULL
        ORDER BY source_id,id""").fetchall()
    local=Counter((sid,normalized_name(name)) for _,sid,name,_ in rows
                  if distinctive(name))
    global_sources=defaultdict(set)
    for (_,sid,name,_) in rows:
        if distinctive(name):
            normalized=normalized_name(name)
            if local[sid,normalized]==1:global_sources[normalized].add(sid)
    # Repeated generic NPC names across many scripts are not convincing.
    eligible={n for n,sources in global_sources.items()
              if 1<=len(sources)<=max_sources_per_name}
    by_first=defaultdict(set)
    name_sources=defaultdict(list)
    for nid,sid,name,line in rows:
        normalized=normalized_name(name)
        if normalized not in eligible or local[sid,normalized]!=1:continue
        by_first[normalized.split()[0]].add(normalized)
        name_sources[normalized].append((sid,nid,name,line))

    pages=db.execute("""SELECT p.id,p.title,p.url,
       (SELECT count(*) FROM wiki_steps w WHERE w.page_id=p.id)
       FROM wiki_pages p
       WHERE NOT EXISTS(SELECT 1 FROM wiki_script_links l WHERE l.page_id=p.id)
       ORDER BY p.id""").fetchall()
    metrics=Counter()
    exported=[]
    with db:
        db.execute("DELETE FROM wiki_missing_script_evidence")
        db.execute("DELETE FROM wiki_missing_script_proposals")
        db.execute("DELETE FROM wiki_missing_script_reviews")
        for pid,title,url,total_steps in pages:
            source_hits=defaultdict(lambda:defaultdict(dict))
            interactions=0
            mentions=0
            steps=db.execute("SELECT step_no,instruction FROM wiki_steps WHERE page_id=? ORDER BY step_no",
                             (pid,)).fetchall()
            for step,text in steps:
                cues,names=interaction_names(text,by_first)
                interactions+=cues
                mentions+=len(names)
                for name in sorted(names):
                    for sid,nid,display,line in name_sources[name]:
                        source_hits[sid][name][step]=(nid,display,line)
            proposals=[]
            for sid,named in source_hits.items():
                distinct=len(named)
                supported={step for values in named.values() for step in values}
                # Two names only from the SAME step is not independent evidence.
                if distinct<2 or len(supported)<2:continue
                proposals.append((sid,distinct,len(supported),named))
            proposals.sort(key=lambda row:(-row[1],-row[2],row[0]))
            proposals=proposals[:max_proposals_per_page]
            if not total_steps:reason="no_extracted_guide_steps"
            elif proposals:reason="multiple_distinct_npc_names_same_script_manual_review"
            elif not interactions:reason="no_explicit_npc_interaction_phrases"
            elif not mentions:reason="no_distinctive_npc_names_in_source_corpus"
            else:reason="insufficient_independent_npc_name_evidence"
            db.execute("""INSERT INTO wiki_missing_script_reviews
             (page_id,guide_steps,explicit_interactions,matched_npc_mentions,proposal_count,review_reason)
             VALUES(?,?,?,?,?,?)""",
             (pid,total_steps,interactions,mentions,len(proposals),reason))
            metrics[reason]+=1
            for sid,ncount,scount,named in proposals:
                db.execute("""INSERT INTO wiki_missing_script_proposals
                     (page_id,source_id,distinct_names,supporting_steps)
                     VALUES(?,?,?,?)""",(pid,sid,ncount,scount))
                evidence=[]
                for name,positions in sorted(named.items()):
                    for step,(nid,display,line) in sorted(positions.items()):
                        db.execute("""INSERT INTO wiki_missing_script_evidence
                              (page_id,source_id,step_no,npc_id,npc_display_name,npc_line)
                              VALUES(?,?,?,?,?,?)""",
                              (pid,sid,step,nid,display,line))
                        evidence.append({"step_no":step,"npc_id":nid,
                                         "npc_name":display,"script_line":line})
                script=db.execute("SELECT project,path,source_url,blob_sha FROM provenance WHERE id=?",
                                  (sid,)).fetchone()
                exported.append({"guide_url":url,"guide_title":title,"source_id":sid,
                    "project":script[0],"script_path":script[1],
                    "script_url":script[2],"blob_sha":script[3],
                    "distinct_npc_names":ncount,"independent_guide_steps":scount,
                    "evidence":evidence,"review_status":"needs_manual_review",
                    "assurance":"unverified_name_pair_not_quest_script_link"})
                metrics["proposals"]+=1
            if proposals:metrics["pages_with_proposals"]+=1
    metrics["unlinked_pages"]=len(pages)
    metrics["evidence_rows"]=db.execute("SELECT count(*) FROM wiki_missing_script_evidence").fetchone()[0]
    return {"version":1,
            "purpose":"missing script discovery for human audit; never joined to game catalog",
            "metrics":dict(metrics),"proposals":exported}
