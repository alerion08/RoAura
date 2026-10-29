"""Conservative, source-cited content classification of wiki quest walkthroughs.

Title-based classes are only human-curation hints. Additional categories require
explicit language in source guide text, not generic quest vocabulary. This
module does not assert a quest is implemented on any server.
"""
from collections import Counter,defaultdict
import re
from baseline import classify_quest_title

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_content_signals(
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 step_no INTEGER NOT NULL,
 signal TEXT NOT NULL,
 matched_text TEXT NOT NULL,
 source_kind TEXT NOT NULL DEFAULT 'wiki_step',
 PRIMARY KEY(page_id,step_no,signal),
 FOREIGN KEY(page_id,step_no) REFERENCES wiki_steps(page_id,step_no)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_content_categories(
 page_id INTEGER PRIMARY KEY REFERENCES wiki_pages(id),
 category TEXT NOT NULL,
 method TEXT NOT NULL,
 explanation TEXT NOT NULL,
 evidence_steps INTEGER NOT NULL DEFAULT 0,
 assurance TEXT NOT NULL DEFAULT 'curation_candidate_unverified'
);
"""
# Deliberately require semantically distinctive phrases. "dungeon" or "quest"
# alone is not a sufficient content classification.
PATTERNS={
    "explicit_dungeon_access":re.compile(
        r"\b(?:gains? (?:access|entry) to|(?:get|obtain) permission to enter|"
        r"unlock (?:the )?(?:entrance|portal)|"
        r"allows? you to enter|enter (?:the |a )?[\w' -]{0,36}(?:dungeon|tower|sanctuary))\b",re.I),
    "explicit_job_change":re.compile(
        r"\b(?:job change(?: quest| guide)?|change your job|change (?:your )?class|"
        r"job advancement|become (?:a |an )?(?:knight|priest|wizard|crusader|"
        r"assassin|rogue|hunter|bard|dancer|sage|alchemist|monk|blacksmith))\b",re.I),
    "explicit_repeatability":re.compile(
        r"\b(?:repeatable quest|quest (?:is |can be )?repeatable|"
        r"repeat this quest|can repeat (?:the |this )?quest|once (?:every|per) day)\b",re.I),
    "collect_items":re.compile(r"\b(?:collect|gather|bring)\s+(?:at least\s+)?\d+\s+[\w'-]+",re.I),
    "deliver_items":re.compile(r"\b(?:hand over|turn in|give (?:the |all |him |her )?\d+\s+[\w'-]+)",re.I),
    "kill_monsters":re.compile(r"\b(?:kill|defeat|hunt)\s+\d+\s+[\w'-]+",re.I),
    "talk_to_npc":re.compile(r"\b(?:talk to|speak (?:with|to)|meet with)\s+[\w'-]+",re.I),
}
REQUIREMENTS_REPEAT=re.compile(r"\brepeatable(?:\s+quest)?\b",re.I)
# This must be a date *range*, not just an incidental mention of a year.
SEASONAL_DATES=re.compile(r"\b(?:19|20)\d{2}[/-]\d{2}[/-]\d{2}\s*(?:~|to|–|-)\s*(?:19|20)\d{2}[/-]\d{2}[/-]\d{2}\b",re.I)

def content_decision(title,requirements,step_signals):
    title_cat,explanation=classify_quest_title(title)
    if title_cat!="unsorted_quest_candidate":
        return (title_cat,"wiki_title",explanation,0)
    if SEASONAL_DATES.search(title):
        return ("historical_or_seasonal","dated_title",
                "An explicit bounded historical date range appears in the guide title",0)
    if requirements and REQUIREMENTS_REPEAT.search(requirements):
        return ("repeatable_quest_candidate","wiki_requirements",
                "The wiki requirements explicitly mention repeatability",0)
    by_kind=defaultdict(set)
    for step,kind in step_signals:by_kind[kind].add(step)
    # Single explicit access language is enough as a curation *candidate*.
    if by_kind["explicit_dungeon_access"]:
        return ("dungeon_or_access_candidate","explicit_guide_step",
                "A source step explicitly refers to unlocking or entering an area",
                len(by_kind["explicit_dungeon_access"]))
    if by_kind["explicit_job_change"]:
        return ("profession_quest_candidate","explicit_guide_step",
                "A source step explicitly mentions job/class advancement",
                len(by_kind["explicit_job_change"]))
    if by_kind["explicit_repeatability"]:
        return ("repeatable_quest_candidate","explicit_guide_step",
                "A source step explicitly describes repeatability",
                len(by_kind["explicit_repeatability"]))
    if by_kind["collect_items"] and by_kind["deliver_items"] and (
            by_kind["collect_items"]!=by_kind["deliver_items"] or
            len(by_kind["collect_items"])>=2):
        return ("item_collection_candidate","corroborated_guide_steps",
                "Distinct source instructions mention item collection and delivery",
                len(by_kind["collect_items"]|by_kind["deliver_items"]))
    return ("unsorted_quest_candidate","needs_content_review",
            "No distinctive evidence supports a more specific source category",0)

def extract_content(db):
    db.executescript(SCHEMA)
    totals=Counter()
    changed_from_title=0
    with db:
        db.execute("DELETE FROM wiki_content_signals")
        db.execute("DELETE FROM wiki_content_categories")
        pages=db.execute("SELECT id,title,requirements FROM wiki_pages ORDER BY id").fetchall()
        for pid,title,requirements in pages:
            source_hits=[]
            steps=db.execute("SELECT step_no,instruction FROM wiki_steps WHERE page_id=? ORDER BY step_no",(pid,)).fetchall()
            for step,text in steps:
                for kind,pattern in PATTERNS.items():
                    match=pattern.search(text or "")
                    if not match:continue
                    source_hits.append((step,kind))
                    db.execute("INSERT INTO wiki_content_signals VALUES(?,?,?,?,?)",
                               (pid,step,kind,match.group(0)[:120],"wiki_step"))
                    totals["signal_"+kind]+=1
            category,method,reason,evidence_count=content_decision(title,requirements,source_hits)
            db.execute("INSERT INTO wiki_content_categories VALUES(?,?,?,?,?,?)",
                       (pid,category,method,reason,evidence_count,"curation_candidate_unverified"))
            totals[category]+=1
            if method!="wiki_title" and category!="unsorted_quest_candidate":
                changed_from_title+=1
    totals["pages"]=len(pages)
    totals["categorized_beyond_title"]=changed_from_title
    totals["content_signals"]=db.execute("SELECT count(*) FROM wiki_content_signals").fetchone()[0]
    totals["still_unsorted"]=totals["unsorted_quest_candidate"]
    return dict(totals)
