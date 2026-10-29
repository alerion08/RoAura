#!/usr/bin/env python3
"""RoAura independent quest research harvester; does not import/modify app code."""
import argparse
import concurrent.futures
import datetime
import gzip
import hashlib
import json
import re
import sqlite3
import time
import urllib.parse
import urllib.robotparser
import zlib
from collections import deque
from pathlib import Path

import requests
import yaml
from bs4 import BeautifulSoup
from enrich import parse_guide, step_evidence, ensure_enrichment_schema, link_database
from graph import SCHEMA as FLOW_SCHEMA, create_flow
from reconcile import SCHEMA as REVIEW_SCHEMA, create_reviews
from state import SCHEMA as STATE_SCHEMA, create_state_facts
from runtime import make_runtime
from audit import summarize_review_queue
from triage import triage_review
from baseline import assemble_core_worklist
from content import SCHEMA as CONTENT_SCHEMA, extract_content
from name_links import link_npc_name_mentions
from dependencies import SCHEMA as DEPENDENCY_SCHEMA, build_dependency_paths
from prerequisites import SCHEMA as PREREQ_SCHEMA, extract_prerequisites
from augment import augment_evidence
from quality import (ensure_quality_schema, update_state, state_counts,
                     dataset_counts, check_nonregression, too_few_steps)

UA = "RoAura-Quest-Research/1.0 (public source research, limited crawl)"
WIKI = "https://irowiki.org/classic/"
NPC_HEADER = re.compile(r"^\s*([a-zA-Z0-9_]+),(\d+),(\d+),(\d+)\s+script\s+(.+?)\s+([a-zA-Z0-9_-]+),\s*\{\s*$")
COMMAND = re.compile(r"^\s*(?:(?:if\s*\([^;]+\)\s*)|(?:switch\s*\(\s*))?(mes|mesf|npctalk|select|menu|input|next|close|close2|getitem|delitem|setquest|completequest|erasequest|checkquest|questinfo)\b(.*)", re.I)
MARKUP_CLEAN = re.compile(r"\s+")
SCHEMA = """
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS provenance (
 id INTEGER PRIMARY KEY, project TEXT NOT NULL, path TEXT NOT NULL,
 blob_sha TEXT, source_url TEXT NOT NULL, stored_at TEXT NOT NULL,
 raw_size INTEGER NOT NULL, content_zlib BLOB NOT NULL,
 UNIQUE(project,path)
);
CREATE TABLE IF NOT EXISTS script_npcs (
 id INTEGER PRIMARY KEY, source_id INTEGER NOT NULL REFERENCES provenance(id),
 ordinal INTEGER NOT NULL, map TEXT, x INTEGER, y INTEGER, direction INTEGER,
 name TEXT NOT NULL, sprite TEXT, line_start INTEGER, line_end INTEGER,
 UNIQUE(source_id, ordinal)
);
CREATE INDEX IF NOT EXISTS npc_pos ON script_npcs(map,x,y);
CREATE INDEX IF NOT EXISTS npc_name ON script_npcs(name);
CREATE TABLE IF NOT EXISTS script_events (
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 npc_ordinal INTEGER, line_no INTEGER NOT NULL,
 command TEXT NOT NULL, argument TEXT NOT NULL,
 PRIMARY KEY(source_id,line_no)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS event_command ON script_events(command);
CREATE TABLE IF NOT EXISTS quest_registry (
 project TEXT NOT NULL, variant TEXT NOT NULL, quest_id INTEGER NOT NULL,
 title TEXT NOT NULL, targets_json TEXT, drops_json TEXT,
 PRIMARY KEY(project,variant,quest_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_pages (
 id INTEGER PRIMARY KEY, title TEXT NOT NULL UNIQUE,
 url TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'indexed_only',
 base_level INTEGER, job_level INTEGER, requirements TEXT, rewards TEXT,
 content_sha TEXT, error TEXT
);
CREATE TABLE IF NOT EXISTS wiki_steps (
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 step_no INTEGER NOT NULL, section TEXT, instruction TEXT NOT NULL,
 PRIMARY KEY(page_id,step_no)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS wiki_details (
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 step_no INTEGER NOT NULL, kind TEXT NOT NULL,
 value TEXT NOT NULL, verified INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY(page_id,step_no,kind,value)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS research_meta (key TEXT PRIMARY KEY,value TEXT NOT NULL) WITHOUT ROWID;
"""

def utcnow():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def norm(s):
    return MARKUP_CLEAN.sub(" ",s or "").strip()

def source_url(repo, ref, path):
    return f"https://github.com/{repo}/blob/{ref}/{path}"

def save_source(db, project, path, blob_sha, url, raw):
    payload = zlib.compress(raw,9)
    with db:
        db.execute("""INSERT INTO provenance(project,path,blob_sha,source_url,stored_at,raw_size,content_zlib)
             VALUES(?,?,?,?,?,?,?)
             ON CONFLICT(project,path) DO UPDATE SET
              blob_sha=excluded.blob_sha,source_url=excluded.source_url,
              stored_at=excluded.stored_at,raw_size=excluded.raw_size,
              content_zlib=excluded.content_zlib""",
              (project,path,blob_sha,url,utcnow(),len(raw),payload))
        sid=db.execute("SELECT id FROM provenance WHERE project=? AND path=?",(project,path)).fetchone()[0]
        db.execute("DELETE FROM script_events WHERE source_id=?",(sid,))
        db.execute("DELETE FROM script_npcs WHERE source_id=?",(sid,))
        try:
            text=raw.decode("utf-8-sig")
        except UnicodeDecodeError:
            text=raw.decode("cp1252",errors="replace")
        lines=text.splitlines()
        active=None
        npcs=[]
        events=[]
        for line_no,line in enumerate(lines,1):
            m=NPC_HEADER.match(line)
            if m:
                if npcs: npcs[-1][-1]=line_no-1
                active=len(npcs)+1
                npcs.append([sid,active,m.group(1),int(m.group(2)),int(m.group(3)),int(m.group(4)),m.group(5),m.group(6),line_no,len(lines)])
                continue
            cmd=COMMAND.match(line)
            if cmd:
                events.append((sid,active,line_no,cmd.group(1).lower(),norm(cmd.group(2)).rstrip(";")))
        db.executemany("INSERT INTO script_npcs(source_id,ordinal,map,x,y,direction,name,sprite,line_start,line_end) VALUES (?,?,?,?,?,?,?,?,?,?)",npcs)
        db.executemany("INSERT INTO script_events(source_id,npc_ordinal,line_no,command,argument) VALUES (?,?,?,?,?)",events)
    return len(npcs),len(events)

def github_tree(session, repo, ref):
    url=f"https://api.github.com/repos/{repo}/git/trees/{ref}?recursive=1"
    r=session.get(url,timeout=90)
    r.raise_for_status()
    data=r.json()
    if data.get("truncated"):
        raise RuntimeError(f"GitHub tree truncated: {repo}/{ref}")
    return data["tree"]

def scripts_from_tree(tree, provider):
    # Public server sources are not mutually equivalent. Keep pre-re and re
    # folder paths intact so downstream consumers can select their variant.
    prefixes=(
        "npc/quests/","npc/jobs/","npc/other/","npc/cities/","npc/instances/",
        "npc/events/","npc/airports/",
        "npc/pre-re/quests/","npc/pre-re/jobs/","npc/pre-re/guides/",
        "npc/pre-re/cities/","npc/pre-re/other/","npc/pre-re/instances/",
        "npc/re/quests/","npc/re/jobs/","npc/re/guides/",
        "npc/re/cities/","npc/re/other/","npc/re/instances/",
    )
    out=[x for x in tree if x.get("type")=="blob" and x["path"].endswith(".txt")
         and x["path"].startswith(prefixes) and (x.get("size",0) <= 900000)]
    return sorted(out,key=lambda x:x["path"])

def download_script(session, repo, ref, entry):
    path=entry["path"]
    url="https://raw.githubusercontent.com/"+repo+"/"+ref+"/"+urllib.parse.quote(path)
    for attempt in range(3):
        try:
            res=session.get(url,timeout=45)
            res.raise_for_status()
            # Git tree blob SHA pins the exact upstream bytes, even when a
            # branch advances between listing and raw downloads.
            actual=hashlib.sha1(b"blob "+str(len(res.content)).encode()+b"\0"+res.content).hexdigest()
            if entry.get("sha") and actual!=entry["sha"]:
                raise ValueError("Git blob SHA mismatch for "+path)
            return entry,res.content,None
        except Exception as e:
            if attempt==2:return entry,None,str(e)
            time.sleep(2*(attempt+1))

def ingest_repo(db, repo, ref, provider, threads=5):
    session=requests.Session()
    session.headers.update({"User-Agent":UA,"Accept":"application/vnd.github+json"})
    tree=github_tree(session,repo,ref)
    entries=scripts_from_tree(tree,provider)
    counts={}
    for entry in entries:
        parts=entry["path"].split("/")
        scope=parts[1] if len(parts)>2 and parts[1] in ("pre-re","re") else "common"
        counts[scope]=counts.get(scope,0)+1
    print(f"Discovered {len(entries)} {provider} NPC script files by scope: {counts}",flush=True)
    successes=0
    errors=[]
    # Each worker uses an isolated requests Session (requests.Session is not thread-safe).
    def fetch(entry):
        local=requests.Session()
        local.headers.update({"User-Agent":UA})
        try:return download_script(local,repo,ref,entry)
        finally:local.close()
    with concurrent.futures.ThreadPoolExecutor(max_workers=threads) as pool:
        for idx,(entry,body,err) in enumerate(pool.map(fetch,entries),1):
            if body is None:
                errors.append({"source":entry["path"],"error":err})
                continue
            save_source(db,provider,entry["path"],entry.get("sha"),source_url(repo,ref,entry["path"]),body)
            successes+=1
            if idx%50==0:print(f"{provider}: {idx}/{len(entries)} fetched",flush=True)
    session.close()
    return {"listed":len(entries),"saved":successes,"errors":errors,
            "scope_counts":counts,
            "download_integrity":"GitHub blob SHA verified for every stored script"}

def import_registry(db, session):
    url="https://raw.githubusercontent.com/rathena/rathena/master/db/pre-re/quest_db.yml"
    res=session.get(url,timeout=70)
    res.raise_for_status()
    body=yaml.safe_load(res.text)
    records=body.get("Body",[])
    with db:
        for x in records:
            if not isinstance(x,dict) or x.get("Id") is None: continue
            db.execute("INSERT OR REPLACE INTO quest_registry VALUES (?,?,?,?,?,?)",
              ("rathena","pre-re",int(x["Id"]),str(x.get("Title","")),
               json.dumps(x.get("Targets"),ensure_ascii=False,separators=(",",":")) if x.get("Targets") else None,
               json.dumps(x.get("Drops"),ensure_ascii=False,separators=(",",":")) if x.get("Drops") else None))
    return len(records)

class WikiFetcher:
    def __init__(self, delay=1.25):
        self.session=requests.Session()
        self.session.headers.update({"User-Agent":UA})
        self.delay=delay
        self.last=0.0
        self.robots=urllib.robotparser.RobotFileParser()
        self.robots.set_url("https://irowiki.org/robots.txt")
        self.robot_error=None
        try:self.robots.read()
        except Exception as e:self.robot_error=str(e)
    def get(self,url):
        if self.robot_error:raise RuntimeError("robots.txt could not be checked: "+self.robot_error)
        if not self.robots.can_fetch(UA,url):
            raise PermissionError("Disallowed by robots.txt: "+url)
        for attempt in range(3):
            delta=self.delay-(time.monotonic()-self.last)
            if delta>0:time.sleep(delta)
            self.last=time.monotonic()
            try:
                r=self.session.get(url,timeout=35)
                # Retry transient overloads; a missing or forbidden page is
                # not a transient failure and must remain an explicit error.
                if r.status_code in (429,500,502,503,504):
                    r.raise_for_status()
                r.raise_for_status()
                if "html" not in r.headers.get("Content-Type","html").lower():
                    raise ValueError("not html")
                return r.text,r.url
            except (requests.ConnectionError,requests.Timeout,requests.HTTPError) as exc:
                transient=not isinstance(exc,requests.HTTPError) or (
                    exc.response is not None and exc.response.status_code in (429,500,502,503,504))
                if not transient or attempt==2:raise
                time.sleep(min(4,2**attempt))
        raise RuntimeError("Wiki retries exhausted")


def article_links(html,base_url):
    soup=BeautifulSoup(html,"html.parser")
    root=soup.select_one("#mw-content-text") or soup.select_one(".mw-parser-output")
    if root is None:return [],[],[]
    links={}; categories=set(); pages=set()
    for a in root.select("a[href]"):
        par=a.find_parent(["li","td","th"])
        if par is None:continue
        url=urllib.parse.urljoin(base_url,a["href"])
        parsed=urllib.parse.urlparse(url)
        if parsed.netloc.lower() not in ("irowiki.org","www.irowiki.org"):continue
        if not parsed.path.startswith("/classic/"):continue
        name=urllib.parse.unquote(parsed.path[len("/classic/"):]).replace("_"," ")
        if not name or ":" in name and not name.startswith("Category:"):continue
        links.setdefault(name,url.split("#")[0])
        if name.startswith("Category:"):categories.add(url.split("#")[0])
        elif not name.startswith(("Special:","File:")):pages.add(name)
    return links,categories,pages

def wiki_discover(fetcher,max_categories=150):
    urls={}
    errors=[]
    try:
        raw,actual=fetcher.get(WIKI+"Quests")
        index,_,_=article_links(raw,actual)
        urls.update({name:url for name,url in index.items() if not name.startswith("Category:")})
    except Exception as e:errors.append({"url":WIKI+"Quests","error":str(e)})
    queue=deque([WIKI+"Category:Quests"]);seen=set()
    while queue and len(seen)<max_categories:
        uri=queue.popleft()
        if uri in seen:continue
        seen.add(uri)
        try:
            raw,actual=fetcher.get(uri)
            soup=BeautifulSoup(raw,"html.parser")
            for section in soup.select("#mw-pages,#mw-subcategories"):
                for a in section.select("a[href]"):
                    link=urllib.parse.urljoin(actual,a["href"])
                    u=urllib.parse.urlparse(link)
                    if u.netloc.lower() not in ("irowiki.org","www.irowiki.org"):continue
                    part=urllib.parse.unquote(u.path)
                    if not part.startswith("/classic/"):continue
                    title=part[len("/classic/"):].replace("_"," ")
                    if title.startswith("Category:"):
                        queue.append(link.split("#")[0])
                    elif title and ":" not in title:
                        urls.setdefault(title,link.split("#")[0])
                for a in section.select("a[href]"):
                    if a.get_text(" ",strip=True).lower().startswith("next page"):
                        queue.append(urllib.parse.urljoin(actual,a["href"]))
        except Exception as e:errors.append({"url":uri,"error":str(e)})
    # Drop obvious navigation and non-quest pages; wiki catalog is a superset and
    # some overview articles may need later manual splitting.
    skip={"Quests","Quest Table","Quest Window","Item Mixing","Maps","Monsters","NPCs","Classes"}
    return {name:url for name,url in urls.items() if name not in skip},errors

def ingest_wiki(db,max_pages=1000):
    ensure_quality_schema(db)
    fetcher=WikiFetcher()
    discovered,errors=wiki_discover(fetcher)
    historical={title:url for title,url in db.execute("SELECT title,url FROM wiki_pages")}
    retained={title:url for title,url in historical.items() if title not in discovered}
    # Preserve *and recheck* historical source URLs when category discovery
    # temporarily fails, instead of silently dropping them from the catalog.
    urls={**discovered,**retained}
    print(f"Discovered {len(discovered)} wiki candidates; retained {len(retained)} historical URLs",flush=True)
    fetched=0;updated=0;failed=0;regressions=0;new_numbered=0;new_narrative=0
    known_maps={row[0].lower() for row in db.execute("SELECT DISTINCT map FROM script_npcs WHERE map IS NOT NULL")}
    with db:
        for title,url in urls.items():
            db.execute("INSERT INTO wiki_pages(title,url) VALUES(?,?) ON CONFLICT(title) DO NOTHING",(title,url))
    ensure_quality_schema(db)
    with db:
        for pid,title in db.execute("SELECT id,title FROM wiki_pages"):
            if title not in urls:
                update_state(db,pid,"cached_not_discovered")
    for idx,(title,url) in enumerate(urls.items(),1):
        if idx>max_pages:break
        pid=db.execute("SELECT id FROM wiki_pages WHERE title=?",(title,)).fetchone()[0]
        old_steps=db.execute("SELECT count(*) FROM wiki_steps WHERE page_id=?",(pid,)).fetchone()[0]
        try:
            html,actual=fetcher.get(url)
            fetched+=1
            steps,quality,meta=parse_guide(html)
            if too_few_steps(old_steps,len(steps)):
                regressions+=1
                with db:
                    update_state(db,pid,"cached_parse_regression",error=(
                        f"Extracted {len(steps)} steps; retained {old_steps} previously parsed steps"))
                errors.append({"url":url,"error":"parse_regression","previous_steps":old_steps,
                               "new_steps":len(steps)})
                continue
            with db:
                db.execute("DELETE FROM wiki_step_sources WHERE page_id=?",(pid,))
                db.execute("DELETE FROM wiki_details WHERE page_id=?",(pid,))
                db.execute("DELETE FROM wiki_steps WHERE page_id=?",(pid,))
                for sn,step in enumerate(steps,1):
                    db.execute("INSERT INTO wiki_steps VALUES(?,?,?,?)",(pid,sn,step.section,step.instruction))
                    db.execute("INSERT INTO wiki_step_sources VALUES(?,?,?,?)",(pid,sn,step.source_number,step.kind))
                    for kind,value in step_evidence(step.instruction,known_maps):
                        db.execute("INSERT OR IGNORE INTO wiki_details VALUES(?,?,?,?,0)",(pid,sn,kind,value))
                status=("walkthrough_parsed" if quality["mode"]=="numbered" else
                        "narrative_parsed" if quality["mode"]=="narrative" else "article_no_steps")
                db.execute("""UPDATE wiki_pages SET url=?,status=?,base_level=?,job_level=?,requirements=?,
                  rewards=?,content_sha=?,error=NULL WHERE id=?""",
                  (actual,status,meta["base_level"],meta["job_level"],meta["requirements"],meta["rewards"],
                   hashlib.sha256(norm(BeautifulSoup(html,"html.parser").get_text(" ",strip=True)).encode()).hexdigest(),pid))
                db.execute("INSERT OR REPLACE INTO wiki_parse_quality VALUES(?,?,?,?)",
                           (pid,quality["mode"],quality["numbered"],quality["narrative"]))
                update_state(db,pid,"fresh" if steps else "fresh_empty",steps=len(steps),success=True)
            updated+=1
            new_numbered+=quality["numbered"]
            new_narrative+=quality["narrative"]
        except Exception as exc:
            failed+=1
            with db:
                # Never overwrite last successful guide state/hash on transient
                # error, even when historical scripts and wiki links still exist.
                update_state(db,pid,"cached_http_error" if old_steps else "never_fetched",
                             error=str(exc))
                if not old_steps:
                    db.execute("UPDATE wiki_pages SET error=? WHERE id=?",(str(exc)[:300],pid))
            errors.append({"url":url,"error":str(exc)})
        if idx%50==0:print(f"Wiki: {idx}/{min(len(urls),max_pages)} pages processed",flush=True)
    fetcher.session.close()
    return {"discovered":len(discovered),"retained_from_previous":len(retained),
            "indexed":len(urls),"attempted":min(len(urls),max_pages),
            "fetched":fetched,"updated":updated,"cached_errors":failed,
            "parse_regressions_prevented":regressions,
            "numbered_steps":new_numbered,"narrative_steps":new_narrative,
            "health":state_counts(db),"errors":errors}

def counts(db):
    return {name:db.execute("SELECT count(*) FROM "+table).fetchone()[0] for name,table in
       [("scripts","provenance"),("npcs","script_npcs"),("script_events","script_events"),("quest_registry_entries","quest_registry"),("wiki_pages","wiki_pages"),("wiki_steps","wiki_steps"),("wiki_details","wiki_details"),("wiki_script_links","wiki_script_links"),("wiki_step_npc_links","wiki_step_npc_links"),
       ("npc_flow_points","npc_flow_points"),("npc_flow_options","npc_flow_options"),
       ("npc_flow_conditions","npc_flow_conditions"),("npc_flow_jumps","npc_flow_jumps"),
       ("wiki_script_reviews","wiki_script_reviews"),("wiki_answer_menu_matches","wiki_answer_menu_matches"),
        ("npc_state_facts","npc_state_facts"),("npc_quest_registry_refs","npc_quest_registry_refs"),
         ("wiki_content_signals","wiki_content_signals"),("wiki_content_categories","wiki_content_categories"),
         ("wiki_prerequisite_paths","wiki_prerequisite_paths")]}

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--out",default="research/quest-database/output")
    p.add_argument("--wiki-limit",type=int,default=1000)
    p.add_argument("--skip-wiki",action="store_true")
    p.add_argument("--skip-hercules",action="store_true")
    p.add_argument("--enrich-only",action="store_true",help="Reparse cached source SQLite without network access")
    p.add_argument("--refresh-wiki-only",action="store_true",help="Refresh wiki HTML only; preserve cached upstream NPC scripts")
    args=p.parse_args()
    target=Path(args.out);target.mkdir(parents=True,exist_ok=True)
    dst=target/"quest_research.sqlite"
    if args.enrich_only and args.refresh_wiki_only:
        raise SystemExit("--enrich-only and --refresh-wiki-only are mutually exclusive")
    if (args.enrich_only or args.refresh_wiki_only) and (not dst.is_file() or dst.stat().st_size<1000):
        raise SystemExit("Incremental mode requires existing validated quest_research.sqlite")
    previous=None
    coverage_file=target/"coverage.json"
    if (args.enrich_only or args.refresh_wiki_only) and coverage_file.is_file():
        previous=json.loads(coverage_file.read_text(encoding="utf-8"))
    db=sqlite3.connect(dst)
    db.executescript(SCHEMA)
    ensure_enrichment_schema(db)
    db.executescript(FLOW_SCHEMA)
    db.executescript(REVIEW_SCHEMA)
    db.executescript(STATE_SCHEMA)
    db.executescript(PREREQ_SCHEMA)
    db.executescript(CONTENT_SCHEMA)
    db.executescript(DEPENDENCY_SCHEMA)
    ensure_quality_schema(db)
    original_counts=dataset_counts(db)
    # A previous generated review links to menu point IDs. Clear those first.
    with db:
        db.execute("DELETE FROM wiki_answer_menu_matches")
    # Rebuild candidate links after refreshing source NPC rows. Otherwise
    # existing link FKs would block replacing source NPCs on a second run.
    with db:
        db.execute("DELETE FROM wiki_step_npc_links")
        db.execute("DELETE FROM wiki_script_links")
    report={"created_utc":utcnow(),"scope":"wiki Classic + rAthena scripts + Hercules quest scripts",
            "limitations":["Wiki quest list is not necessarily exhaustive","Only explicit static branches are linked; dynamic conditions and menus remain unresolved",
                           "Server scripts are not evidence of exact official iRO/ToC behavior"],
            "sources":{}}
    if args.enrich_only:
        for table in ("provenance","wiki_pages","script_npcs","wiki_steps"):
            if db.execute("SELECT count(*) FROM "+table).fetchone()[0]==0:
                raise SystemExit("Existing research database is missing required records: "+table)
        report["sources"]=previous.get("sources",{}) if previous else {"mode":"reused_unverified_snapshot"}
        report["source_snapshot_utc"]=previous.get("source_snapshot_utc",previous.get("created_utc")) if previous else None
        report["fetch_mode"]="reused_existing_snapshot"
        print("Reusing existing captured wiki and NPC scripts without network requests",flush=True)
    elif args.refresh_wiki_only:
        for table in ("provenance","wiki_pages","script_npcs","wiki_steps"):
            if db.execute("SELECT count(*) FROM "+table).fetchone()[0]==0:
                raise SystemExit("Existing research database is missing required records: "+table)
        report["sources"]=dict(previous.get("sources",{})) if previous else {}
        report["sources"]["irowiki_classic"]=ingest_wiki(db,args.wiki_limit)
        report["source_snapshot_utc"]=previous.get("source_snapshot_utc") if previous else None
        report["wiki_snapshot_utc"]=report["created_utc"]
        report["fetch_mode"]="wiki_refreshed_cached_npc_sources"
        print("Wiki refreshed while original NPC script revisions remain unchanged",flush=True)
    else:
        session=requests.Session()
        session.headers.update({"User-Agent":UA})
        for label,job in [
            ("rathena",lambda:ingest_repo(db,"rathena/rathena","master","rathena")),
            ("hercules",lambda:ingest_repo(db,"HerculesWS/Hercules","stable","hercules") if not args.skip_hercules else {"skipped":True}),
            ("rathena_pre_re_registry",lambda:import_registry(db,session)),
            ("irowiki_classic",lambda:ingest_wiki(db,args.wiki_limit) if not args.skip_wiki else {"skipped":True})]:
            try:report["sources"][label]=job()
            except Exception as exc:
                report["sources"][label]={"error":str(exc)}
                print(f"ERROR {label}: {exc}",flush=True)
        session.close()
        report["source_snapshot_utc"]=report["created_utc"]
        report["fetch_mode"]="fresh_upstream_requests"
    report["linking"]=link_database(db)
    report["evidence_augmentation"]=augment_evidence(db)
    report["npc_name_links"]=link_npc_name_mentions(db)
    report["content_curation"]=extract_content(db)
    report["linking"].update({
        "wiki_script_links":report["evidence_augmentation"]["script_candidates"],
        "linked_pages":report["evidence_augmentation"]["linked_pages"],
        "wiki_step_npc_links":db.execute("SELECT count(*) FROM wiki_step_npc_links").fetchone()[0],
    })
    report["dialogue_flow"]=create_flow(db)
    report["quest_state"]=create_state_facts(db)
    report["script_link_review"]=create_reviews(db)
    report["wiki_prerequisites"]=extract_prerequisites(db)
    report["prerequisite_paths"]=build_dependency_paths(db)
    db.execute("INSERT OR REPLACE INTO research_meta VALUES('schema_version','8')")
    db.execute("INSERT OR REPLACE INTO research_meta VALUES('matching_revision','header_and_correlated_npc_coordinate_v1')")
    db.execute("INSERT OR REPLACE INTO research_meta VALUES('research_status','candidate matches only; see coverage.json')")
    db.commit()
    report["counts"]=counts(db)
    report["quality"]={"wiki_refresh":state_counts(db),
                        "nonregression":check_nonregression(original_counts,dataset_counts(db))}
    report["sqlite_integrity"]=db.execute("PRAGMA integrity_check").fetchone()[0]
    report["foreign_key_violations"]=len(db.execute("PRAGMA foreign_key_check").fetchall())
    db.execute("VACUUM")
    report["runtime_export"]=make_runtime(db,target/"questie_catalog.sqlite")
    review=summarize_review_queue(db)
    report["quality"]["review_queue"]=review["metrics"]
    (target/"quest_review_queue.json").write_text(
        json.dumps(review,ensure_ascii=False,indent=2),encoding="utf-8")
    critical=triage_review(review)
    report["quality"]["critical_triage"]=critical["counts"]
    (target/"critical_quest_triage.json").write_text(
        json.dumps(critical,ensure_ascii=False,indent=2),encoding="utf-8")
    core=assemble_core_worklist(review,db)
    report["quality"]["core_quest_worklist"]=core["metrics"]
    (target/"roquestie_core_worklist.json").write_text(
        json.dumps(core,ensure_ascii=False,indent=2),encoding="utf-8")
    db.close()
    report["questie_schema_version"]=report["runtime_export"]["schema_version"]
    (target/"coverage.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    for payload in (dst,target/"questie_catalog.sqlite"):
        with payload.open("rb") as src,gzip.open(str(payload)+".gz","wb",compresslevel=9) as sink:
            while chunk:=src.read(1024*1024):sink.write(chunk)
    print(json.dumps({"counts":report["counts"],"integrity":report["sqlite_integrity"],"source_status":{k:(v.get("error") or "OK") if isinstance(v,dict) else "OK" for k,v in report["sources"].items()}},ensure_ascii=False),flush=True)
    if report["sqlite_integrity"]!="ok" or report["foreign_key_violations"]>0:raise SystemExit(1)

if __name__=="__main__":
    main()
