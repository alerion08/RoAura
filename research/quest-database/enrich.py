"""Parse iRO Classic quest guides and link them to candidate NPC script files.

All links represent evidence/candidates, never a verified official server branch.
"""
from dataclasses import dataclass
import re
import unicodedata

from bs4 import BeautifulSoup, Tag

SPACE = re.compile(r"\s+")
STEP_START = re.compile(r"^\s*(\d{1,3})\s*[.)]\s+(.*)$", re.S)
MAP_COORD = re.compile(r"\b([a-z][a-z0-9_]{2,24})\s*[, ]?\s*\(?\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)?", re.I)
BARE_COORD = re.compile(r"\b(\d{1,3})\s*,\s*(\d{1,3})\b")
QUOTED = re.compile(r'["“]([^"”\n]{1,110})["”]')
ANSWERS = re.compile(r"\b(select|choose|answer|say|input|enter|type|tell|reply|respond|press)\b", re.I)
STOP = {"the","and","for","from","with","quest","quests","guide","walkthrough","change","class","job","skill","skills","to","of","in","a","an","entrance","item","npc"}
GENERIC_NPC = {"guard","guardsman","woman","man","girl","boy","child","old lady","stranger","merchant","kafra employee","book","door","portal"}

def clean(value):
    return SPACE.sub(" ", value or "").strip()

def tokens(value):
    value = unicodedata.normalize("NFKD", value).lower().replace("'", "")
    return tuple(x for x in re.findall(r"[a-z0-9]+", value.replace("_", " ")) if x not in STOP and len(x)>1)

def info(root):
    values = {"base_level": None, "job_level": None, "requirements": None, "rewards": None}
    # iRO wiki quest template is a plain table, not table.infobox.
    for table in root.find_all("table"):
        text = clean(table.get_text(" ", strip=True))
        if "Requirements" not in text or "Rewards" not in text or len(text) > 5000:
            continue
        for field,label in (("base_level","Base Level"),("job_level","Job Level")):
            m = re.search(r"\b" + re.escape(label) + r"\s*:\s*(\d+)", text, re.I)
            if m: values[field] = int(m.group(1))
        halves = re.split(r"\bRewards\b", text, maxsplit=1)
        before = re.split(r"\bRequirements\b", halves[0], maxsplit=1)
        values["requirements"] = clean(before[-1])[:2000] or None
        values["rewards"] = clean(halves[-1])[:2000] if len(halves)>1 else None
        break
    return values

@dataclass(frozen=True)
class GuideStep:
    section: str
    instruction: str
    source_number: int | None
    kind: str

def parse_guide(raw):
    soup = BeautifulSoup(raw,"html.parser")
    root = soup.select_one(".mw-parser-output") or soup.select_one("#mw-content-text")
    if root is None:
        return [], {"mode":"missing","numbered":0,"narrative":0}, info(soup)
    for el in root.select(".mw-editsection,.toc,.navbox,.printfooter,.mw-empty-elt,script,style"):
        el.decompose()
    metadata = info(root)
    numbered=[]
    narrative=[]
    section=""
    for child in root.children:
        if not isinstance(child,Tag): continue
        if child.name in ("h2","h3","h4"):
            section = clean(child.get_text(" ",strip=True))
            continue
        if child.name=="ol":
            for li in child.find_all("li",recursive=False):
                value=clean(li.get_text(" ",strip=True))
                if value: numbered.append(GuideStep(section,value,None,"ordered_list"))
            continue
        if child.name=="p":
            value=clean(child.get_text(" ",strip=True))
            if not value:continue
            m=STEP_START.match(value)
            if m:
                numbered.append(GuideStep(section,clean(m.group(2)),int(m.group(1)),"numbered"))
            else:
                narrative.append((section,value))
                if numbered and not value.lower().startswith(("retrieved from","see also")):
                    last=numbered[-1]
                    numbered[-1]=GuideStep(last.section,clean(last.instruction+" "+value),last.source_number,last.kind)
        if child.name=="ul" and numbered:
            bullets=[clean(li.get_text(" ",strip=True)) for li in child.find_all("li",recursive=False)]
            bullets=[x for x in bullets if x]
            if bullets:
                last=numbered[-1]
                numbered[-1]=GuideStep(last.section,clean(last.instruction+" | "+" | ".join(bullets)),last.source_number,last.kind)
    if numbered:
        return numbered,{"mode":"numbered","numbered":len(numbered),"narrative":0},metadata
    prose=[GuideStep(sec,txt,None,"narrative") for sec,txt in narrative
           if len(txt)>30 and not txt.lower().startswith(("this page","retrieved from","see also"))]
    return prose,{"mode":"narrative" if prose else "empty","numbered":0,"narrative":len(prose)},metadata

def step_evidence(text,known_maps=()):
    """Only literal guide data, bare coordinates are deliberately map-unknown."""
    result=set()
    recognized={x.lower() for x in known_maps}
    named_matches=list(MAP_COORD.finditer(text))
    for m in named_matches:
        mapname=m.group(1).lower()
        x=int(m.group(2));y=int(m.group(3))
        if x<=400 and y<=400 and (mapname in recognized or "_" in mapname):
            result.add(("map_coord",f"{mapname}:{x}:{y}"))
    for m in BARE_COORD.finditer(text):
        if any(n.start()<=m.start() and m.end()<=n.end() for n in named_matches):continue
        if int(m.group(1))<=400 and int(m.group(2))<=400:
            result.add(("bare_coord",f"{int(m.group(1))}:{int(m.group(2))}"))
    for m in QUOTED.finditer(text):
        before=text[max(0,m.start()-80):m.start()]
        if ANSWERS.search(before):
            result.add(("guide_answer",clean(m.group(1))))
    return sorted(result)

def script_score(title,path):
    """Filename matching only; never proves equivalence with actual game script."""
    stem=path.rsplit("/",1)[-1].rsplit(".",1)[0]
    wt=tokens(title); st=tokens(stem)
    if not wt or not st:return 0,""
    w=set(wt);s=set(st);common=w&s
    if not common:return 0,""
    if wt==st or w==s:return 95,"exact_title_filename"
    if len(w)>=2 and w<=s:return 86,"full_title_in_filename"
    if len(common)>=2 and len(common)/len(w)>=.8 and len(common)/len(s)>=.5:
        return 75,"substantial_filename_overlap"
    if len(w)==1 and wt[0] in s and len(wt[0])>=5:
        return 60,"single_distinctive_filename_token"
    return 0,""

SCHEMA = """
CREATE TABLE IF NOT EXISTS wiki_parse_quality (
 page_id INTEGER PRIMARY KEY REFERENCES wiki_pages(id), mode TEXT NOT NULL,
 numbered_steps INTEGER NOT NULL, narrative_steps INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS wiki_step_sources (
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),step_no INTEGER NOT NULL,
 source_step INTEGER,kind TEXT NOT NULL,PRIMARY KEY(page_id,step_no)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_script_links (
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),source_id INTEGER NOT NULL REFERENCES provenance(id),
 score INTEGER NOT NULL,method TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'candidate',
 PRIMARY KEY(page_id,source_id)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_step_npc_links (
 page_id INTEGER NOT NULL,step_no INTEGER NOT NULL,npc_id INTEGER NOT NULL REFERENCES script_npcs(id),
 score INTEGER NOT NULL,evidence TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'candidate',
 PRIMARY KEY(page_id,step_no,npc_id)) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS page_script_link ON wiki_script_links(source_id);
"""
def ensure_enrichment_schema(db):
    db.executescript(SCHEMA)

def link_database(db):
    ensure_enrichment_schema(db)
    with db:
        db.execute("DELETE FROM wiki_step_npc_links")
        db.execute("DELETE FROM wiki_script_links")
        pages=db.execute("SELECT id,title FROM wiki_pages WHERE status IN ('walkthrough_parsed','narrative_parsed')").fetchall()
        sources=db.execute("SELECT id,path FROM provenance").fetchall()
        npcs={}
        for nid,sid,mapname,x,y,name in db.execute("SELECT id,source_id,map,x,y,name FROM script_npcs"):
            npcs.setdefault(sid,[]).append((nid,mapname,x,y,name))
        for pid,title in pages:
            matches=[]
            for sid,path in sources:
                score,method=script_score(title,path)
                if score>=60:matches.append((score,sid,method))
            matches=sorted(matches,reverse=True)[:6]
            for score,sid,method in matches:
                db.execute("INSERT INTO wiki_script_links(page_id,source_id,score,method) VALUES(?,?,?,?)",
                           (pid,sid,score,method))
            if not matches:continue
            steps=db.execute("SELECT step_no,instruction FROM wiki_steps WHERE page_id=?",(pid,)).fetchall()
            for step_no,instruction in steps:
                positions={(m.group(1).lower(),int(m.group(2)),int(m.group(3)))
                           for m in MAP_COORD.finditer(instruction)}
                text=clean(instruction).casefold()
                hits=[]
                for linkscore,sid,_ in matches:
                    for nid,mapname,x,y,name in npcs.get(sid,[]):
                        short=clean(name.split("#")[0]).casefold()
                        exact=(mapname.casefold(),x,y) in positions
                        named=(len(short)>=5 and short not in GENERIC_NPC
                               and re.search(r"(?<![a-z])"+re.escape(short)+r"(?![a-z])",text))
                        if exact and named:hits.append((95,nid,"map_coord_and_npc_name"))
                        elif exact:hits.append((85,nid,"exact_map_coord"))
                        elif named and linkscore>=75:hits.append((70,nid,"npc_name_in_linked_script"))
                for score,nid,why in sorted(hits,reverse=True)[:5]:
                    db.execute("INSERT OR IGNORE INTO wiki_step_npc_links VALUES(?,?,?,?,?,'candidate')",
                               (pid,step_no,nid,score,why))
    return {
        "wiki_script_links":db.execute("SELECT count(*) FROM wiki_script_links").fetchone()[0],
        "linked_pages":db.execute("SELECT count(DISTINCT page_id) FROM wiki_script_links").fetchone()[0],
        "wiki_step_npc_links":db.execute("SELECT count(*) FROM wiki_step_npc_links").fetchone()[0]
    }
