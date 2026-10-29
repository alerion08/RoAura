"""Extract explicit wiki prerequisite mentions without inferring game state.

Only a labelled "Quest Prerequisite(s):" requirement is used. A linked
page is a textual wiki assertion, not proof of a server's active script.
"""
import re

SECTION = re.compile(r"\bQuest\s+Prerequisite(?:\(\s*s\s*\)|s)?\s*:\s*", re.I)
NEXT_FIELD = re.compile(r"\b(?:Base Level|Job Level|Item\(s\)|Rewards|Skill\(s\)|Zeny|Quest Reward\(s\))\s*:",re.I)

SCHEMA = """
CREATE TABLE IF NOT EXISTS wiki_quest_prerequisite_text(
 page_id INTEGER PRIMARY KEY REFERENCES wiki_pages(id),
 raw_text TEXT NOT NULL,
 expression_logic TEXT NOT NULL DEFAULT 'unspecified',
 source TEXT NOT NULL DEFAULT 'wiki_requirements'
);
CREATE TABLE IF NOT EXISTS wiki_quest_prerequisite_links(
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 prerequisite_page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 matched_title TEXT NOT NULL,
 assurance TEXT NOT NULL DEFAULT 'wiki_documented_not_server_verified',
 PRIMARY KEY(page_id,prerequisite_page_id)
) WITHOUT ROWID;
"""

def prerequisite_clause(requirements):
    if not requirements:return None
    match=SECTION.search(requirements)
    if not match:return None
    text=requirements[match.end():]
    stop=NEXT_FIELD.search(text)
    if stop:text=text[:stop.start()]
    return re.sub(r"\s+"," ",text).strip(" :.;")[:1000] or None

def match_known_titles(clause,current_title,titles):
    """Use full bounded names, longest first; overlapping titles need review."""
    result=[];used=[]
    for title,pid in sorted(titles,key=lambda row:(-len(row[0]),row[0])):
        if title.casefold()==current_title.casefold():continue
        pattern=r"(?<![A-Za-z0-9])"+re.escape(title)+r"(?![A-Za-z0-9])"
        hits=list(re.finditer(pattern,clause,re.I))
        for m in hits:
            if any(m.start()<end and m.end()>start for start,end in used):
                continue
            used.append((m.start(),m.end()))
            result.append((pid,title))
            break
    return result

def extract_prerequisites(db):
    db.executescript(SCHEMA)
    with db:
        db.execute("DELETE FROM wiki_quest_prerequisite_links")
        db.execute("DELETE FROM wiki_quest_prerequisite_text")
        titles=db.execute("SELECT title,id FROM wiki_pages ORDER BY title").fetchall()
        pages=db.execute("SELECT id,title,requirements FROM wiki_pages ORDER BY id").fetchall()
        statements=0;resolved=0;unresolved=0;pages_linked=0
        for pid,title,req in pages:
            clause=prerequisite_clause(req)
            if not clause:continue
            logic="contains_or" if re.search(r"\bor\b|/",clause,re.I) else "unspecified"
            db.execute("INSERT INTO wiki_quest_prerequisite_text VALUES(?,?,?,'wiki_requirements')",(pid,clause,logic))
            statements+=1
            matched=match_known_titles(clause,title,titles)
            if matched:pages_linked+=1
            else:unresolved+=1
            for target,name in matched:
                db.execute("""INSERT OR IGNORE INTO wiki_quest_prerequisite_links
                    (page_id,prerequisite_page_id,matched_title)
                    VALUES(?,?,?)""",(pid,target,name))
                resolved+=1
    return {
        "explicit_prerequisite_statements":statements,
        "wiki_page_links":db.execute("SELECT count(*) FROM wiki_quest_prerequisite_links").fetchone()[0],
        "pages_with_resolved_prerequisites":pages_linked,
        "statements_with_no_exact_catalog_match":unresolved,
        "assurance":"wiki statements only; server implementation unknown"
    }
