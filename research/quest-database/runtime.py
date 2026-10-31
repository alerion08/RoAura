"""Build a versioned, source-aware read-only quest assistant SQLite projection.

Designed for later RoAura integration without importing research heuristics
or script parser code. No source is marked gameplay verified.
"""
import json
import sqlite3
from pathlib import Path
from urllib.parse import unquote,urlparse
from quality import ensure_quality_schema
from prerequisites import SCHEMA as PREREQ_SCHEMA
from content import SCHEMA as CONTENT_SCHEMA
from dependencies import SCHEMA as DEPENDENCY_SCHEMA
from adjudicate import SCHEMA as ADJUDICATION_SCHEMA

RUNTIME_VERSION=5
SCHEMA="""
PRAGMA foreign_keys=ON;
PRAGMA user_version=5;
CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE quests(
 quest_key TEXT PRIMARY KEY,title TEXT NOT NULL,source_url TEXT NOT NULL,
 status TEXT NOT NULL,review_state TEXT NOT NULL,base_level INTEGER,job_level INTEGER,
 requirements TEXT,rewards TEXT,content_sha TEXT
) WITHOUT ROWID;
CREATE TABLE quest_quality(
 quest_key TEXT PRIMARY KEY REFERENCES quests(quest_key),
 refresh_state TEXT NOT NULL,
 last_attempt_utc TEXT,
 last_success_utc TEXT,
 last_error TEXT,
 stored_steps INTEGER NOT NULL
) WITHOUT ROWID;
CREATE TABLE quest_prerequisite_text(
 quest_key TEXT PRIMARY KEY REFERENCES quests(quest_key),
 raw_text TEXT NOT NULL,
 expression_logic TEXT NOT NULL,
 source TEXT NOT NULL
) WITHOUT ROWID;
CREATE TABLE quest_prerequisites(
 quest_key TEXT NOT NULL REFERENCES quests(quest_key),
 prerequisite_key TEXT NOT NULL REFERENCES quests(quest_key),
 matched_title TEXT NOT NULL,
 assurance TEXT NOT NULL,
 PRIMARY KEY(quest_key,prerequisite_key)
) WITHOUT ROWID;
CREATE TABLE quest_dependency_paths(
 quest_key TEXT NOT NULL REFERENCES quests(quest_key),
 dependency_key TEXT NOT NULL REFERENCES quests(quest_key),
 min_hops INTEGER NOT NULL CHECK(min_hops>=1),
 provenance TEXT NOT NULL,
 PRIMARY KEY(quest_key,dependency_key)
) WITHOUT ROWID;
CREATE TABLE quest_content_categories(
 quest_key TEXT PRIMARY KEY REFERENCES quests(quest_key),
 category TEXT NOT NULL,method TEXT NOT NULL,
 explanation TEXT NOT NULL,evidence_steps INTEGER NOT NULL,
 assurance TEXT NOT NULL
) WITHOUT ROWID;
CREATE TABLE quest_content_signals(
 quest_key TEXT NOT NULL,step_no INTEGER NOT NULL,
 signal TEXT NOT NULL,matched_text TEXT NOT NULL,source_kind TEXT NOT NULL,
 PRIMARY KEY(quest_key,step_no,signal),
 FOREIGN KEY(quest_key,step_no) REFERENCES quest_steps(quest_key,step_no)
) WITHOUT ROWID;
CREATE TABLE quest_steps(
 quest_key TEXT NOT NULL REFERENCES quests(quest_key),step_no INTEGER NOT NULL,
 source_step INTEGER,kind TEXT,section TEXT,instruction TEXT NOT NULL,
 PRIMARY KEY(quest_key,step_no)
) WITHOUT ROWID;
CREATE TABLE quest_step_locations(
 quest_key TEXT NOT NULL,step_no INTEGER NOT NULL,
 hint_kind TEXT NOT NULL,raw_hint TEXT NOT NULL,
 map TEXT,x INTEGER NOT NULL,y INTEGER NOT NULL,
 assurance TEXT NOT NULL,
 PRIMARY KEY(quest_key,step_no,hint_kind,raw_hint),
 FOREIGN KEY(quest_key,step_no) REFERENCES quest_steps(quest_key,step_no)
) WITHOUT ROWID;
CREATE INDEX quest_step_locations_by_map ON quest_step_locations(map,x,y);
CREATE TABLE step_hints(
 quest_key TEXT NOT NULL,step_no INTEGER NOT NULL,
 kind TEXT NOT NULL,value TEXT NOT NULL,source_verified INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY(quest_key,step_no,kind,value),
 FOREIGN KEY(quest_key,step_no) REFERENCES quest_steps(quest_key,step_no)
) WITHOUT ROWID;
CREATE TABLE scripts(
 script_key TEXT PRIMARY KEY,project TEXT NOT NULL,source_path TEXT NOT NULL,
 variant_scope TEXT NOT NULL CHECK(variant_scope IN ('common','pre-re','renewal')),
 source_url TEXT NOT NULL,git_blob_sha TEXT
) WITHOUT ROWID;
CREATE TABLE quest_scripts(
 quest_key TEXT NOT NULL REFERENCES quests(quest_key),
 script_key TEXT NOT NULL REFERENCES scripts(script_key),
 score INTEGER NOT NULL,method TEXT NOT NULL,status TEXT NOT NULL,
 PRIMARY KEY(quest_key,script_key)
) WITHOUT ROWID;
CREATE TABLE quest_source_decisions(
 quest_key TEXT NOT NULL REFERENCES quests(quest_key),
 script_key TEXT NOT NULL REFERENCES scripts(script_key),
 source_blob_sha TEXT NOT NULL,decision TEXT NOT NULL,
 reason TEXT NOT NULL,reviewer TEXT NOT NULL,
 status_scope TEXT NOT NULL,
 PRIMARY KEY(quest_key,script_key)
) WITHOUT ROWID;
CREATE VIEW quest_source_supported AS
 SELECT d.quest_key,d.script_key,d.reason,s.project,s.variant_scope
 FROM quest_source_decisions d JOIN scripts s ON s.script_key=d.script_key
 WHERE d.decision='source_supported';
CREATE TABLE npcs(
 npc_key TEXT PRIMARY KEY,script_key TEXT NOT NULL REFERENCES scripts(script_key),
 source_ordinal INTEGER NOT NULL,name TEXT NOT NULL,map TEXT,x INTEGER,y INTEGER,
 direction INTEGER,line_start INTEGER,line_end INTEGER,
 UNIQUE(script_key,source_ordinal)
);
CREATE INDEX npcs_by_map ON npcs(map,x,y);
CREATE TABLE quest_step_npcs(
 quest_key TEXT NOT NULL,step_no INTEGER NOT NULL,
 npc_key TEXT NOT NULL REFERENCES npcs(npc_key),
 score INTEGER NOT NULL,evidence TEXT NOT NULL,status TEXT NOT NULL,
 PRIMARY KEY(quest_key,step_no,npc_key),
 FOREIGN KEY(quest_key,step_no) REFERENCES quest_steps(quest_key,step_no)
) WITHOUT ROWID;
CREATE TABLE dialogue_lines(
 npc_key TEXT NOT NULL REFERENCES npcs(npc_key),
 line_no INTEGER NOT NULL,command TEXT NOT NULL,argument TEXT NOT NULL,
 PRIMARY KEY(npc_key,line_no)
) WITHOUT ROWID;
CREATE TABLE dialogue_points(
 npc_key TEXT NOT NULL REFERENCES npcs(npc_key),line_no INTEGER NOT NULL,
 kind TEXT NOT NULL,expression TEXT NOT NULL,
 PRIMARY KEY(npc_key,line_no,kind)
) WITHOUT ROWID;
CREATE TABLE dialogue_options(
 npc_key TEXT NOT NULL,line_no INTEGER NOT NULL,kind TEXT NOT NULL,
 option_no INTEGER NOT NULL,label TEXT,target_line INTEGER,resolution TEXT NOT NULL,
 PRIMARY KEY(npc_key,line_no,kind,option_no),
 FOREIGN KEY(npc_key,line_no,kind) REFERENCES dialogue_points(npc_key,line_no,kind)
) WITHOUT ROWID;
CREATE TABLE dialogue_guards(
 npc_key TEXT NOT NULL REFERENCES npcs(npc_key),line_no INTEGER NOT NULL,
 expression TEXT NOT NULL,end_line INTEGER,
 PRIMARY KEY(npc_key,line_no)
) WITHOUT ROWID;
CREATE TABLE dialogue_jumps(
 npc_key TEXT NOT NULL REFERENCES npcs(npc_key),line_no INTEGER NOT NULL,
 label TEXT NOT NULL,target_line INTEGER,resolution TEXT NOT NULL,
 PRIMARY KEY(npc_key,line_no,label)
) WITHOUT ROWID;
CREATE TABLE quest_state_facts(
 npc_key TEXT NOT NULL REFERENCES npcs(npc_key),line_no INTEGER NOT NULL,
 occurrence INTEGER NOT NULL,role TEXT NOT NULL,subject TEXT,object_id INTEGER,
 amount_expr TEXT,value_expr TEXT,argument TEXT NOT NULL,
 context_guard_line INTEGER,evidence TEXT NOT NULL,
 PRIMARY KEY(npc_key,line_no,occurrence)
) WITHOUT ROWID;
CREATE INDEX fact_object ON quest_state_facts(role,object_id);
CREATE INDEX fact_subject ON quest_state_facts(subject);
CREATE TABLE quest_registry(
 project TEXT NOT NULL,variant TEXT NOT NULL,quest_id INTEGER NOT NULL,
 title TEXT NOT NULL,targets_json TEXT,drops_json TEXT,
 PRIMARY KEY(project,variant,quest_id)
) WITHOUT ROWID;
CREATE TABLE quest_registry_evidence(
 npc_key TEXT NOT NULL,line_no INTEGER NOT NULL,occurrence INTEGER NOT NULL,
 project TEXT NOT NULL,variant TEXT NOT NULL,quest_id INTEGER NOT NULL,
 PRIMARY KEY(npc_key,line_no,occurrence,project,variant,quest_id),
 FOREIGN KEY(npc_key,line_no,occurrence) REFERENCES quest_state_facts(npc_key,line_no,occurrence),
 FOREIGN KEY(project,variant,quest_id) REFERENCES quest_registry(project,variant,quest_id)
) WITHOUT ROWID;
CREATE TABLE candidate_answers(
 quest_key TEXT NOT NULL,step_no INTEGER NOT NULL,
 npc_key TEXT NOT NULL,line_no INTEGER NOT NULL,kind TEXT NOT NULL,
 option_no INTEGER NOT NULL,review_state TEXT NOT NULL,
 PRIMARY KEY(quest_key,step_no,npc_key,line_no,kind,option_no),
 FOREIGN KEY(quest_key,step_no) REFERENCES quest_steps(quest_key,step_no),
 FOREIGN KEY(npc_key,line_no,kind,option_no) REFERENCES dialogue_options(npc_key,line_no,kind,option_no)
) WITHOUT ROWID;
CREATE VIEW quest_navigation_candidates AS
 SELECT s.quest_key,s.step_no,n.npc_key,n.name,n.map,n.x,n.y,
        s.score,s.evidence,s.status
 FROM quest_step_npcs s JOIN npcs n ON n.npc_key=s.npc_key;
CREATE VIEW quest_progress_evidence AS
 SELECT q.quest_key,q.script_key,q.status AS script_link_status,
        f.npc_key,f.line_no,f.occurrence,f.role,f.object_id,
        f.amount_expr,f.value_expr,f.context_guard_line,f.evidence
 FROM quest_scripts q JOIN npcs n ON n.script_key=q.script_key
 JOIN quest_state_facts f ON f.npc_key=n.npc_key;
CREATE VIEW quest_dialogue_candidates AS
 SELECT q.quest_key,q.status AS script_link_status,
        n.npc_key,n.name,o.line_no,o.option_no,o.label,o.target_line,o.resolution
 FROM quest_scripts q JOIN npcs n ON n.script_key=q.script_key
 JOIN dialogue_options o ON o.npc_key=n.npc_key;
CREATE VIEW quest_id_candidates AS
 SELECT q.quest_key, q.script_key, n.npc_key, n.map, n.x, n.y,
        r.quest_id, r.project AS registry_project, r.variant AS registry_variant,
        f.line_no, f.occurrence, f.role, q.method AS source_link_method,
        q.status AS script_match_status,
        'candidate_unverified' AS assurance
 FROM quest_scripts q JOIN npcs n ON n.script_key=q.script_key
 JOIN quest_registry_evidence e ON e.npc_key=n.npc_key
 JOIN quest_state_facts f ON f.npc_key=e.npc_key
      AND f.line_no=e.line_no AND f.occurrence=e.occurrence
 JOIN quest_registry r ON r.project=e.project AND r.variant=e.variant AND r.quest_id=e.quest_id;
"""

def key_page(url):
    path=urlparse(url).path
    if not path.startswith("/classic/"):raise ValueError("Unexpected wiki URL path: "+url)
    return "irowiki-classic:"+unquote(path[len("/classic/"):])

def key_script(project,path):
    return f"{project}:{path}"

def key_npc(script_key,ordinal):
    return f"{script_key}#{ordinal}"

def script_variant_scope(path):
    if path.startswith("npc/pre-re/"):return "pre-re"
    if path.startswith("npc/re/"):return "renewal"
    return "common"

def make_runtime(source,destination):
    ensure_quality_schema(source)
    source.executescript(PREREQ_SCHEMA)
    source.executescript(CONTENT_SCHEMA)
    source.executescript(DEPENDENCY_SCHEMA)
    source.executescript(ADJUDICATION_SCHEMA)
    path=Path(destination);path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists():path.unlink()
    out=sqlite3.connect(path)
    out.execute("PRAGMA foreign_keys=ON")
    out.executescript(SCHEMA)
    out.execute("INSERT INTO metadata VALUES('schema_version',?)",(str(RUNTIME_VERSION),))
    out.execute("INSERT INTO metadata VALUES('source_scope','iRO Wiki Classic + rAthena + Hercules; not verified for individual game servers')")
    out.execute("INSERT INTO metadata VALUES('assurance','All NPC script links, dialogue recommendations and status interpretation are research candidates')")
    out.execute("INSERT INTO metadata VALUES('format','SQLite; stable source-derived keys; no WPF or Python runtime dependency')")
    page_keys={pid:key_page(url) for pid,url in source.execute("SELECT id,url FROM wiki_pages")}
    source_keys={sid:key_script(project,path) for sid,project,path in source.execute("SELECT id,project,path FROM provenance")}
    npc_keys={(sid,ordinal):key_npc(source_keys[sid],ordinal) for sid,ordinal in
              source.execute("SELECT source_id,ordinal FROM script_npcs")}
    with out:
        out.executemany("INSERT INTO quests VALUES(?,?,?,?,?,?,?,?,?,?)",
          ((page_keys[pid],title,url,status,review or "unreviewed",bl,jl,req,rew,sha)
          for pid,title,url,status,bl,jl,req,rew,sha,review in source.execute(
              """SELECT p.id,p.title,p.url,p.status,p.base_level,p.job_level,p.requirements,
              p.rewards,p.content_sha,r.review_state FROM wiki_pages p
              LEFT JOIN wiki_script_reviews r ON r.page_id=p.id""")))
        out.executemany("INSERT INTO quest_quality VALUES(?,?,?,?,?,?)",
          ((page_keys[pid],state,attempt,success,error,num) for pid,state,attempt,success,error,num
           in source.execute("""SELECT page_id,refresh_state,last_attempt_utc,
                 last_success_utc,last_error,stored_steps FROM wiki_refresh_state""")))
        out.executemany("INSERT INTO quest_prerequisite_text VALUES(?,?,?,?)",
          ((page_keys[pid],raw,logic,source_name)
          for pid,raw,logic,source_name in source.execute(
              "SELECT page_id,raw_text,expression_logic,source FROM wiki_quest_prerequisite_text")))
        out.executemany("INSERT INTO quest_prerequisites VALUES(?,?,?,?)",
          ((page_keys[pid],page_keys[requires],title,assurance)
          for pid,requires,title,assurance in source.execute(
              "SELECT page_id,prerequisite_page_id,matched_title,assurance FROM wiki_quest_prerequisite_links")))
        out.executemany("INSERT INTO quest_dependency_paths VALUES(?,?,?,?)",
          ((page_keys[pid],page_keys[dep],hops,provenance) for pid,dep,hops,provenance
           in source.execute("""SELECT page_id,dependency_page_id,min_hops,provenance
              FROM wiki_prerequisite_paths""")))
        out.executemany("INSERT INTO quest_content_categories VALUES(?,?,?,?,?,?)",
          ((page_keys[pid],cat,method,explanation,steps,assurance)
           for pid,cat,method,explanation,steps,assurance in source.execute(
               """SELECT page_id,category,method,explanation,evidence_steps,assurance
               FROM wiki_content_categories""")))
        out.executemany("INSERT INTO quest_steps VALUES(?,?,?,?,?,?)",
          ((page_keys[pid],step,source_step,kind,section,instruction)
           for pid,step,section,instruction,source_step,kind in source.execute(
               """SELECT s.page_id,s.step_no,s.section,s.instruction,t.source_step,t.kind
               FROM wiki_steps s LEFT JOIN wiki_step_sources t ON
               t.page_id=s.page_id AND t.step_no=s.step_no""")))
        out.executemany("INSERT INTO quest_content_signals VALUES(?,?,?,?,?)",
          ((page_keys[pid],step,kind,matched,source_kind)
           for pid,step,kind,matched,source_kind in source.execute(
              """SELECT page_id,step_no,signal,matched_text,source_kind
                 FROM wiki_content_signals""")))
        # Locations from the guide itself are valuable even without any
        # rAthena/Hercules NPC match. Mapless coordinates are NOT assigned a
        # guessed map. These are waypoint hints, not verified NPC positions.
        for pid,step,kind,value in source.execute(
                """SELECT page_id,step_no,kind,value FROM wiki_details
                WHERE kind IN ('map_coord','bare_coord')"""):
            bits=value.split(":")
            if kind=="map_coord" and len(bits)==3:
                mapname,x,y=bits
            elif kind=="bare_coord" and len(bits)==2:
                x,y=bits;mapname=None
            else:
                continue
            if not x.isdigit() or not y.isdigit():continue
            out.execute("INSERT OR IGNORE INTO quest_step_locations VALUES(?,?,?,?,?,?,?,?)",
                        (page_keys[pid],step,kind,value,mapname,int(x),int(y),
                         "wiki_literal_not_npc_verified"))
        out.executemany("INSERT INTO step_hints VALUES(?,?,?,?,?)",
          ((page_keys[pid],step,kind,value,verified) for pid,step,kind,value,verified
            in source.execute("SELECT page_id,step_no,kind,value,verified FROM wiki_details")))
        out.executemany("INSERT INTO scripts VALUES(?,?,?,?,?,?)",
          ((source_keys[sid],project,path,script_variant_scope(path),url,sha)
           for sid,project,path,url,sha in source.execute(
               "SELECT id,project,path,source_url,blob_sha FROM provenance")))
        out.executemany("INSERT INTO quest_scripts VALUES(?,?,?,?,?)",
          ((page_keys[pid],source_keys[sid],score,method,status)
           for pid,sid,score,method,status in source.execute(
               "SELECT page_id,source_id,score,method,status FROM wiki_script_links")))
        out.executemany("INSERT INTO quest_source_decisions VALUES(?,?,?,?,?,?,?)",
          ((page_keys[pid],source_keys[sid],sha,state,reason,reviewer,scope)
           for pid,sid,sha,state,reason,reviewer,scope in source.execute(
             """SELECT page_id,source_id,source_blob_sha,decision,reason,reviewer,status_scope
                  FROM wiki_source_decisions""")))
        out.executemany("INSERT INTO npcs VALUES(?,?,?,?,?,?,?,?,?,?)",
          ((npc_keys[(sid,ord_)],source_keys[sid],ord_,name,map_,x,y,direction,first,last)
          for sid,ord_,name,map_,x,y,direction,first,last in source.execute(
              "SELECT source_id,ordinal,name,map,x,y,direction,line_start,line_end FROM script_npcs")))
        out.executemany("INSERT INTO quest_step_npcs VALUES(?,?,?,?,?,?)",
          ((page_keys[pid],step,npc_keys[npc_src,npc_ord],score,evidence,status)
           for pid,step,npc_src,npc_ord,score,evidence,status in source.execute(
              """SELECT l.page_id,l.step_no,n.source_id,n.ordinal,l.score,l.evidence,l.status
              FROM wiki_step_npc_links l JOIN script_npcs n ON n.id=l.npc_id""")))
        # Only commands already recognized as dialogue text, not arbitrary source
        # execution statements. Text is source-derived and kept exactly as parsed.
        out.executemany("INSERT OR IGNORE INTO dialogue_lines VALUES(?,?,?,?)",
          ((npc_keys[sid,npc],line,cmd,arg)
          for sid,npc,line,cmd,arg in source.execute(
              """SELECT source_id,npc_ordinal,line_no,command,argument
              FROM script_events WHERE command IN ('mes','mesf','npctalk')
                AND npc_ordinal IS NOT NULL""") if (sid,npc) in npc_keys))
        out.executemany("INSERT INTO dialogue_points VALUES(?,?,?,?)",
          ((npc_keys[sid,ord_],ln,kind,expr)
           for sid,ord_,ln,kind,expr in source.execute(
               "SELECT source_id,npc_ordinal,line_no,kind,expression FROM npc_flow_points")))
        out.executemany("INSERT INTO dialogue_options VALUES(?,?,?,?,?,?,?)",
          ((npc_keys[sid,ord_],ln,kind,opt,label,target,resolution)
           for sid,ord_,ln,kind,opt,label,target,resolution in source.execute(
               """SELECT p.source_id,p.npc_ordinal,p.line_no,p.kind,
               o.option_no,o.label,o.target_line,o.resolution
               FROM npc_flow_options o JOIN npc_flow_points p ON p.id=o.point_id""")))
        out.executemany("INSERT INTO dialogue_guards VALUES(?,?,?,?)",
          ((npc_keys[sid,ord_],ln,expr,end)
           for sid,ord_,ln,expr,end in source.execute(
              "SELECT source_id,npc_ordinal,line_no,expression,end_line FROM npc_flow_conditions")))
        out.executemany("INSERT INTO dialogue_jumps VALUES(?,?,?,?,?)",
          ((npc_keys[sid,ord_],ln,label,target,resolution)
           for sid,ord_,ln,label,target,resolution in source.execute(
              "SELECT source_id,npc_ordinal,line_no,label,target_line,resolution FROM npc_flow_jumps")))
        out.executemany("INSERT INTO quest_state_facts VALUES(?,?,?,?,?,?,?,?,?,?,?)",
          ((npc_keys[sid,ord_],ln,occ,role,subject,obj,amount,val,arg,guard,evidence)
           for sid,ord_,ln,occ,role,subject,obj,amount,val,arg,guard,evidence in source.execute(
              """SELECT source_id,npc_ordinal,line_no,occurrence,role,subject,object_id,
               amount_expr,value_expr,argument,context_guard_line,evidence FROM npc_state_facts""")))
        out.executemany("INSERT INTO quest_registry VALUES(?,?,?,?,?,?)",
          (r for r in source.execute(
              "SELECT project,variant,quest_id,title,targets_json,drops_json FROM quest_registry")))
        out.executemany("INSERT INTO quest_registry_evidence VALUES(?,?,?,?,?,?)",
          ((npc_keys[sid,ord_],ln,occ,proj,variant,qid)
          for sid,ord_,ln,occ,proj,variant,qid in source.execute(
             """SELECT f.source_id,f.npc_ordinal,f.line_no,f.occurrence,
             l.registry_project,l.registry_variant,l.quest_id
             FROM npc_quest_registry_refs l JOIN npc_state_facts f ON f.id=l.fact_id""")))
        out.executemany("INSERT OR IGNORE INTO candidate_answers VALUES(?,?,?,?,?,?,?)",
          ((page_keys[pid],step,npc_keys[sid,ord_],ln,kind,opt,state)
           for pid,step,sid,ord_,ln,kind,opt,state in source.execute(
               """SELECT m.page_id,m.step_no,p.source_id,p.npc_ordinal,p.line_no,p.kind,
               m.option_no,m.review_state
               FROM wiki_answer_menu_matches m JOIN npc_flow_points p ON p.id=m.point_id""")))
    from selection import filter_catalog
    filter_catalog(out,source)
    integrity=out.execute("PRAGMA integrity_check").fetchone()[0]
    fk=out.execute("PRAGMA foreign_key_check").fetchall()
    if integrity!="ok" or fk:
        raise RuntimeError(f"Invalid runtime quest database: {integrity}, FK={len(fk)}")
    summary={
        "schema_version":RUNTIME_VERSION,
        "sqlite_integrity":integrity,"foreign_key_violations":len(fk),
        "tables":{name:out.execute(f"SELECT count(*) FROM {name}").fetchone()[0] for name in
          ("quests","quest_quality","quest_prerequisite_text","quest_prerequisites",
           "quest_dependency_paths","quest_content_categories","quest_content_signals",
           "quest_steps","quest_step_locations","step_hints","scripts","quest_scripts","quest_source_decisions","npcs",
           "quest_step_npcs","dialogue_lines","dialogue_points","dialogue_options",
           "dialogue_guards","dialogue_jumps","quest_state_facts","quest_registry",
           "quest_registry_evidence","candidate_answers")}
    }
    out.commit();out.execute("VACUUM");out.close()
    summary["bytes"]=path.stat().st_size
    return summary
