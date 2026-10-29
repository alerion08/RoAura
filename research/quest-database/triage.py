"""Evidence-aware triage of critical wiki pages, without assuming ToC content.

An iRO seasonal event is not silently deleted from the database. Classification
only changes the human verification queue; server coverage stays unverified.
"""
import re

EVENT=re.compile(r"\b(?:event|anniversary|spotlight|festival|thanksgiving|christmas|"
                 r"halloween|valentine(?:'s)?|st\.? patrick(?:'s)?|april fools|fourth of july)\b",re.I)
DATED=re.compile(r"\b(?:19|20)\d{2}\b")
# Labels are conservative; do not infer game mechanics from name alone.
NONQUEST_REFERENCE={"taekwon kid","swordman","mage","thief","merchant","acolyte",
                    "archer","novice","knight","priest","wizard","blacksmith"}

def classify_critical(title):
    name=title.strip()
    if EVENT.search(name):
        return ("historical_event_or_spotlight","verify server event availability; do not assume permanent quest")
    if name.casefold() in NONQUEST_REFERENCE:
        return ("class_or_reference_page","verify whether page is a quest walkthrough or class reference")
    return ("core_or_other_quest_candidate","investigate wiki prerequisites, source script identity, exact NPC map/coordinates and Quest ID")

def triage_review(queue):
    items=[]
    stats={}
    for row in queue["quests"]:
        if row["review_priority"]!="critical":continue
        category,action=classify_critical(row["title"])
        stats[category]=stats.get(category,0)+1
        items.append({
            "quest_key":row["quest_key"],"title":row["title"],
            "source_steps":row["steps"],"source_scopes":row["source_scopes"],
            "classification":category,"verification_action":action,
            "reason":row["issues"],"target_server_status":"unknown",
            "source":row.get("guide_status"),"priority_from_audit":"critical"
        })
    items.sort(key=lambda row:(row["classification"]!="core_or_other_quest_candidate",
                               -row["source_steps"],row["quest_key"]))
    return {
        "version":1,
        "notes":[
            "Classification is a title-based human triage hint, not proof that ToC lacks any quest",
            "No Quest ID, state, NPC interaction or server implementation is verified",
            "The source wiki itself includes historical iRO events and nonquest/reference pages"
        ],
        "counts":{"critical_total":len(items),"categories":stats},
        "queue":items
    }
