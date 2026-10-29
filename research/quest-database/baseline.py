"""ROQuestie: server-neutral source catalog curation.

Names are a triage signal, not evidence of availability or of quest mechanics.
All wiki quests are preserved. No private-server preset is assumed.
"""
import re
from collections import Counter

HISTORICAL = re.compile(
    r"\b(?:event|anniversary|spotlight|festival|thanksgiving|christmas|"
    r"halloween|valentine(?:'s)?|st\.? patrick(?:'s)?|april fools|"
    r"fourth of july)\b",re.I)
DUNGEON = re.compile(
    r"\b(?:dungeon|entrance|access|sanctuary|tower|nest|seal)\b",re.I)
JOB = re.compile(
    r"\b(?:job change|job quest|advancement|transcendence|rebirth|"
    r"platinum skill|guild investigation)\b",re.I)
MAIN_STORY = re.compile(
    r"\b(?:president|successor|satan morroc|crow of destiny|"
    r"god items|curse of gaebolg|guardian of yggdrasil)\b",re.I)
REFERENCE_TITLES = {
    "taekwon kid","swordman","mage","thief","merchant","acolyte",
    "archer","novice","knight","priest","wizard","blacksmith"
}

def classify_quest_title(title):
    value=title.strip()
    if HISTORICAL.search(value):
        return "historical_or_seasonal", "Explicit event/spotlight term in wiki title"
    if value.casefold() in REFERENCE_TITLES:
        return "profession_reference_candidate", "Title matches a profession; verify page structure"
    if DUNGEON.search(value):
        return "dungeon_or_access_candidate", "Dungeon/access keyword in wiki title"
    if JOB.search(value):
        return "profession_quest_candidate", "Profession advancement keyword in wiki title"
    if MAIN_STORY.search(value):
        return "storyline_candidate", "Storyline keyword in wiki title"
    return "unsorted_quest_candidate", "Source content needs manual category review"

def assemble_core_worklist(review):
    rows=[]
    for r in review["quests"]:
        category,reason=classify_quest_title(r["title"])
        emphasis="archive_for_reference" if category=="historical_or_seasonal" else (
            "verify_reference_type" if category=="profession_reference_candidate" else
            "prioritize_core_research" if category in (
                "dungeon_or_access_candidate","profession_quest_candidate",
                "storyline_candidate") else "manual_classification")
        rows.append({
            "quest_key":r["quest_key"],
            "title":r["title"],
            "source_category_candidate":category,
            "category_evidence":reason,
            "curation_action":emphasis,
            "walkthrough_steps":r["steps"],
            "script_candidates":r["linked_scripts"],
            "linked_step_npcs":r["steps_with_npc"],
            "source_variant_scopes":r["source_scopes"],
            "coverage_gaps":r["issues"],
            "quest_review_priority":r["review_priority"],
            "server_availability":"unknown",
            "universal_availability_claim":False,
        })
    order={"prioritize_core_research":0,"manual_classification":1,
           "verify_reference_type":2,"archive_for_reference":3}
    rows.sort(key=lambda x:(order[x["curation_action"]],
                            x["script_candidates"]==0,
                            -x["walkthrough_steps"],x["quest_key"]))
    categories=Counter(x["source_category_candidate"] for x in rows)
    return {
        "version":1,
        "product":"ROQuestie",
        "coverage_scope":"common Ragnarok Online quest knowledge base; individual server behavior is variable",
        "assurance":"Unverified reference candidates, not a list of quests guaranteed on every server",
        "metrics":{"total_source_pages":len(rows),"categories":dict(categories),
                   "core_research_candidates":sum(x["curation_action"]=="prioritize_core_research" for x in rows),
                   "seasonal_archive_candidates":categories["historical_or_seasonal"],
                   "server_verified":0},
        "quests":rows
    }
