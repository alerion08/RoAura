"""Extract provenance-linked NPC quest state and item facts.

This is a lexical audit, NOT a script interpreter or guaranteed in-game state
machine. Never infer an if-branch outcome or item requirement from a bare
check; store the original expression and scope for later manual verification.
"""
import re
import zlib
from graph import mask_comments_and_strings, split_top_level, line_starts, line_for, matching

SCHEMA = """
CREATE TABLE IF NOT EXISTS npc_state_facts (
 id INTEGER PRIMARY KEY,
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 npc_ordinal INTEGER NOT NULL,
 line_no INTEGER NOT NULL,
 occurrence INTEGER NOT NULL,
 role TEXT NOT NULL,
 subject TEXT,
 object_id INTEGER,
 amount_expr TEXT,
 value_expr TEXT,
 argument TEXT NOT NULL,
 context_guard_line INTEGER,
 evidence TEXT NOT NULL DEFAULT 'source_lexical',
 UNIQUE(source_id,npc_ordinal,line_no,occurrence)
);
CREATE INDEX IF NOT EXISTS npc_state_source_line
 ON npc_state_facts(source_id,npc_ordinal,line_no);
CREATE INDEX IF NOT EXISTS npc_state_object
 ON npc_state_facts(role,object_id);
CREATE INDEX IF NOT EXISTS npc_state_subject
 ON npc_state_facts(subject);
CREATE VIEW IF NOT EXISTS npc_quest_state_refs AS
 SELECT source_id,npc_ordinal,line_no,role,object_id,argument,context_guard_line
 FROM npc_state_facts
 WHERE role IN ('quest_start','quest_complete','quest_erase','quest_check');
CREATE VIEW IF NOT EXISTS npc_item_state_refs AS
 SELECT source_id,npc_ordinal,line_no,role,object_id,amount_expr,argument,context_guard_line
 FROM npc_state_facts
 WHERE role IN ('item_check','item_grant','item_consume');
CREATE TABLE IF NOT EXISTS npc_quest_registry_refs (
 fact_id INTEGER NOT NULL REFERENCES npc_state_facts(id),
 registry_project TEXT NOT NULL,
 registry_variant TEXT NOT NULL,
 quest_id INTEGER NOT NULL,
 PRIMARY KEY(fact_id,registry_project,registry_variant,quest_id)
) WITHOUT ROWID;
"""
# Standalone command syntax, not function calls (quest ID or item ID may be
# expressions; we retain them even when a literal ID cannot be determined).
COMMAND = re.compile(
    r"(?<![\w.@])\b(setquest|completequest|erasequest|getitem|delitem)\s+([^;\n]{1,500});",
    re.I,
)
# Function syntax, including nested use in if/checks. No evaluation.
CHECK = re.compile(r"(?<![\w.@])\b(checkquest|countitem)\s*\(",re.I)
VARIABLE = re.compile(r"(?<![\w.@])\bset[ \t]+([.@#'$A-Za-z_][.@#'$\w\[\]]*)[ \t]*,[ \t]*([^;\n]{1,500});",re.I)
ASSIGNMENT = re.compile(
    r"(?m)^[ \t]*([.@#'$A-Za-z_][.@#'$\w\[\]]*)\s*(\+=|-=|=(?!=))\s*([^;\n]{1,500});"
)
INT = re.compile(r"^\s*(\d{1,9})\s*$")
MAX_ARGUMENT=500

def literal_int(expr):
    m=INT.fullmatch(expr or "")
    return int(m.group(1)) if m else None

def decode(blob):
    raw=zlib.decompress(blob)
    try:return raw.decode("utf-8-sig")
    except UnicodeDecodeError:return raw.decode("cp1252",errors="replace")

def guard_for(guards,line):
    # Most deeply nested explicit-brace guard that contains the source line.
    possible=[(end-start,start) for start,end in guards
              if end is not None and start<=line<=end]
    return min(possible)[1] if possible else None

def extract_state(raw,start_line,end_line,guards=()):
    lines=raw.splitlines(keepends=True)
    if start_line>len(lines):return []
    snippet="".join(lines[start_line-1:end_line])
    masked=mask_comments_and_strings(snippet)
    starts=line_starts(snippet)
    absline=lambda i:start_line-1+line_for(starts,i)
    found=[]
    def add(pos,role,subject,object_id,amount,value,arg):
        line=absline(pos)
        found.append((pos,line,role,subject,object_id,amount,value,
                      (arg or "").strip()[:MAX_ARGUMENT],
                      guard_for(guards,line)))
    for m in COMMAND.finditer(masked):
        command=m.group(1).lower()
        # Use unmasked code argument: original strings and variable names are
        # retained, while the match itself was verified outside comments.
        arg=snippet[m.start(2):m.end(2)]
        args=split_top_level(arg)
        first=args[0] if args else ""
        roles={"setquest":"quest_start","completequest":"quest_complete","erasequest":"quest_erase",
               "getitem":"item_grant","delitem":"item_consume"}
        add(m.start(),roles[command],None,literal_int(first),
            args[1] if command in ("getitem","delitem") and len(args)>1 else None,
            args[1] if command=="setquest" and len(args)>1 else None,arg)
    for m in CHECK.finditer(masked):
        op=m.end()-1
        close=matching(masked,op)
        if close<0:continue
        args=split_top_level(snippet[op+1:close])
        kind=m.group(1).lower()
        add(m.start(),"quest_check" if kind=="checkquest" else "item_check",
            None,literal_int(args[0]) if args else None,
            None,args[1] if len(args)>1 else None,snippet[op+1:close])
    for m in VARIABLE.finditer(masked):
        var=snippet[m.start(1):m.end(1)].strip()
        expr=snippet[m.start(2):m.end(2)]
        add(m.start(),"variable_write",var,None,None,expr,expr)
    for m in ASSIGNMENT.finditer(masked):
        var=snippet[m.start(1):m.end(1)].strip()
        op=m.group(2)
        expr=snippet[m.start(3):m.end(3)]
        add(m.start(),"variable_write",var,None,None,f"{op} {expr}",f"{var} {op} {expr}")
    found.sort(key=lambda row:row[0])
    occurrence={}
    output=[]
    for _pos,ln,role,subject,obj,amount,value,arg,guard in found:
        ordinal=occurrence.get(ln,0)+1
        occurrence[ln]=ordinal
        output.append((ln,ordinal,role,subject,obj,amount,value,arg,guard))
    return output

def create_state_facts(db):
    db.executescript(SCHEMA)
    with db:
        db.execute("DELETE FROM npc_quest_registry_refs")
        db.execute("DELETE FROM npc_state_facts")
    summary={"facts":0,"quest_actions":0,"quest_checks":0,"item_actions":0,
             "item_checks":0,"variable_writes":0,"literal_quest_ids":0,
             "literal_item_ids":0,"registry_matches":0,"guarded_facts":0}
    registry={int(qid):[] for (qid,) in db.execute("SELECT DISTINCT quest_id FROM quest_registry")}
    for project,variant,qid in db.execute("SELECT project,variant,quest_id FROM quest_registry"):
        registry[int(qid)].append((project,variant,int(qid)))
    source_records=db.execute("SELECT id,content_zlib FROM provenance ORDER BY id").fetchall()
    for sid,blob in source_records:
        raw=decode(blob)
        npc_rows=db.execute("SELECT ordinal,line_start,line_end FROM script_npcs WHERE source_id=? ORDER BY ordinal",(sid,)).fetchall()
        guards_by_npc={}
        for ordinal,ln,end in db.execute("SELECT npc_ordinal,line_no,end_line FROM npc_flow_conditions WHERE source_id=?",(sid,)):
            guards_by_npc.setdefault(ordinal,[]).append((ln,end))
        with db:
            for ordinal,start,end in npc_rows:
                for ln,idx,role,subject,obj,amount,value,arg,guard in extract_state(raw,start,end,guards_by_npc.get(ordinal,())):
                    cur=db.execute("""INSERT INTO npc_state_facts
                      (source_id,npc_ordinal,line_no,occurrence,role,subject,object_id,amount_expr,value_expr,argument,context_guard_line)
                      VALUES(?,?,?,?,?,?,?,?,?,?,?)""",
                      (sid,ordinal,ln,idx,role,subject,obj,amount,value,arg,guard))
                    summary["facts"]+=1
                    if guard is not None:summary["guarded_facts"]+=1
                    if role in ("quest_start","quest_complete","quest_erase"):
                        summary["quest_actions"]+=1
                    elif role=="quest_check":summary["quest_checks"]+=1
                    elif role in ("item_grant","item_consume"):summary["item_actions"]+=1
                    elif role=="item_check":summary["item_checks"]+=1
                    else:summary["variable_writes"]+=1
                    if role.startswith("quest_") and obj is not None:
                        summary["literal_quest_ids"]+=1
                        for project,variant,qid in registry.get(obj,[]):
                            db.execute("INSERT OR IGNORE INTO npc_quest_registry_refs VALUES(?,?,?,?)",
                                       (cur.lastrowid,project,variant,qid))
                            summary["registry_matches"]+=1
                    elif role.startswith("item_") and obj is not None:
                        summary["literal_item_ids"]+=1
    return summary
