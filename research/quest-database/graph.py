"""Conservative lexical NPC dialogue flow extraction.

Never executes scripts or claims an official server's actual dialogue path.
Only explicit switch(select), if(select==N), menu labels, and goto create
resolved edges. Script events remain the source of truth for complete dialogue.
"""
import bisect
import re
import sqlite3
import zlib

SCHEMA = """
CREATE TABLE IF NOT EXISTS npc_flow_points(
 id INTEGER PRIMARY KEY,
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 npc_ordinal INTEGER NOT NULL,
 line_no INTEGER NOT NULL,
 kind TEXT NOT NULL,
 expression TEXT NOT NULL,
 UNIQUE(source_id,npc_ordinal,line_no,kind)
);
CREATE INDEX IF NOT EXISTS npc_flow_point_lookup ON npc_flow_points(source_id,npc_ordinal,line_no);
CREATE TABLE IF NOT EXISTS npc_flow_options(
 point_id INTEGER NOT NULL REFERENCES npc_flow_points(id),
 option_no INTEGER NOT NULL,
 label TEXT,
 target_line INTEGER,
 resolution TEXT NOT NULL,
 PRIMARY KEY(point_id,option_no)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS npc_flow_conditions(
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 npc_ordinal INTEGER NOT NULL,
 line_no INTEGER NOT NULL,
 expression TEXT NOT NULL,
 end_line INTEGER,
 PRIMARY KEY(source_id,npc_ordinal,line_no)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS npc_flow_jumps(
 source_id INTEGER NOT NULL REFERENCES provenance(id),
 npc_ordinal INTEGER NOT NULL,
 line_no INTEGER NOT NULL,
 label TEXT NOT NULL,
 target_line INTEGER,
 resolution TEXT NOT NULL,
 PRIMARY KEY(source_id,npc_ordinal,line_no,label)
) WITHOUT ROWID;
CREATE VIEW IF NOT EXISTS npc_flow_edges AS
 SELECT p.source_id,p.npc_ordinal,p.line_no AS from_line,
        o.target_line AS to_line,'choice' AS kind,o.option_no,o.label,o.resolution
 FROM npc_flow_options o JOIN npc_flow_points p ON p.id=o.point_id
 WHERE o.target_line IS NOT NULL
 UNION ALL
 SELECT j.source_id,j.npc_ordinal,j.line_no,j.target_line,
        'goto',NULL,j.label,j.resolution
 FROM npc_flow_jumps j WHERE j.target_line IS NOT NULL;
"""

SELECT = re.compile(r"(?<![\w.@])select\s*\(", re.I)
MENU = re.compile(r"(?<![\w.@])menu\b(?!\s*\()", re.I)
IF = re.compile(r"\bif\s*\(", re.I)
GOTO = re.compile(r"\bgoto\s+([A-Za-z_]\w*)\s*;", re.I)
LABEL = re.compile(r"(?m)^[ \t]*([A-Za-z_]\w*):(?!=)")
CASE = re.compile(r"\bcase\s+(\d+)\s*:",re.I)
SIMPLE_STR = re.compile(r'^\s*"((?:\\.|[^"\\])*)"\s*$',re.S)
IF_SELECTED = re.compile(r"^\s*==\s*(\d+)\s*$")
MAX_FIELD = 1500

def mask_comments_and_strings(raw):
    """Maintain offsets and newline count; omit comments and string contents."""
    out=list(raw);i=0;mode=None
    while i<len(raw):
        c=raw[i];n=raw[i+1] if i+1<len(raw) else ''
        if mode=="line":
            if c=='\n':mode=None
            else:out[i]=' '
            i+=1;continue
        if mode=="block":
            if c=='*' and n=='/':out[i]=out[i+1]=' ';i+=2;mode=None;continue
            if c!='\n':out[i]=' '
            i+=1;continue
        if mode=="string":
            if c=='\\' and n:out[i]=out[i+1]=' ';i+=2;continue
            if c=='"':mode=None
            out[i]=' ' if c!='\n' else '\n'
            i+=1;continue
        if c=='/' and n=='/':out[i]=out[i+1]=' ';mode="line";i+=2;continue
        if c=='/' and n=='*':out[i]=out[i+1]=' ';mode="block";i+=2;continue
        if c=='"':out[i]=' ';mode="string";i+=1;continue
        i+=1
    return "".join(out)

def matching(src,start,left='(',right=')'):
    if start<0 or start>=len(src) or src[start]!=left:return -1
    depth=0
    for i in range(start,len(src)):
        if src[i]==left:depth+=1
        if src[i]==right:
            depth-=1
            if depth==0:return i
    return -1

def split_top_level(s):
    """Split a comma list ignoring strings, escaped quotes and nested brackets."""
    parts=[];start=0;quote=False;escaped=False;depth=0
    for i,c in enumerate(s):
        if quote:
            if escaped:escaped=False
            elif c=='\\':escaped=True
            elif c=='"':quote=False
            continue
        if c=='"':quote=True;continue
        if c in '([{':depth+=1
        elif c in ')]}':depth=max(0,depth-1)
        elif c==',' and depth==0:
            parts.append(s[start:i].strip());start=i+1
    parts.append(s[start:].strip())
    return parts

def literal(token):
    m=SIMPLE_STR.fullmatch(token)
    if not m:return None
    return m.group(1).replace(r'\"','"').replace(r'\n','\n').replace(r'\\','\\')

def options_for_select(expr):
    parts=split_top_level(expr)
    if not parts:return [(0,None)]
    values=[literal(x) for x in parts]
    if any(v is None for v in values):
        return [(0,None)]  # unknown dynamic options: sentinel; never invent a first choice
    labels=[]
    for v in values:labels.extend(v.split(':'))
    return [(i,label) for i,label in enumerate(labels,1)]

def line_starts(text):
    return [0]+[m.end() for m in re.finditer('\n',text)]

def line_for(starts,offset):
    return bisect.bisect_right(starts,offset)

def statement_end(mask,offset):
    # For menu syntax, stop at the next semicolon outside any nested parentheses.
    depth=0
    for i in range(offset,len(mask)):
        c=mask[i]
        if c=='(':depth+=1
        elif c==')':depth=max(0,depth-1)
        elif c==';' and depth==0:return i
    return -1

def switch_cases(mask,start_select,end_select,starts):
    """Case N labels directly inside the switch block, excluding nested switches."""
    line_start=mask.rfind('\n',0,start_select)+1
    prefix=mask[line_start:start_select]
    found=re.search(r"\bswitch\s*\(\s*$",prefix,re.I)
    if not found:return {}
    switch_paren=line_start+found.end()-1
    switch_end=matching(mask,switch_paren)
    if switch_end<0:return {}
    tail=re.match(r"\s*\{",mask[switch_end+1:])
    if not tail:return {}
    open_brace=switch_end+1+tail.end()-1
    close_brace=matching(mask,open_brace,'{','}')
    if close_brace<0:return {}
    candidates={}
    depth=0
    for pos in range(open_brace+1,close_brace):
        ch=mask[pos]
        if ch=='{':depth+=1
        elif ch=='}':depth-=1
        if ch=='c' and depth==0:
            cm=CASE.match(mask,pos)
            if cm:
                n=int(cm.group(1))
                candidates.setdefault(n,[]).append(line_for(starts,pos))
    return {n:xs[0] for n,xs in candidates.items() if len(xs)==1}

def selected_if_target(mask,select_start,select_close,starts):
    """Resolve only an explicit 'if (select(...) == N) {' true arm."""
    line_start=mask.rfind('\n',0,select_start)+1
    before=mask[line_start:select_start]
    found=re.search(r"\bif\s*\(\s*$",before,re.I)
    if not found:return {}
    if_start=line_start+found.end()-1
    if_end=matching(mask,if_start)
    if if_end<0:return {}
    tail=IF_SELECTED.fullmatch(mask[select_close+1:if_end])
    if not tail:return {}
    brace=re.match(r"\s*\{",mask[if_end+1:])
    if not brace:return {}
    first=if_end+1+brace.end()
    # The first statement of a block might begin on the next line.
    while first<len(mask) and mask[first].isspace():first+=1
    if first>=len(mask):return {}
    return {int(tail.group(1)):line_for(starts,first)}

def explicit_labels(mask,starts):
    names={}
    for match in LABEL.finditer(mask):
        name=match.group(1)
        names.setdefault(name,[]).append(line_for(starts,match.start()))
    return {name:rows[0] for name,rows in names.items() if len(rows)==1}

def extract_npc(raw,start_line,end_line):
    """Yield structured lexical evidence with source lines relative to whole file."""
    lines=raw.splitlines(keepends=True)
    if start_line>len(lines):return {"points":[],"choices":[],"guards":[],"jumps":[]}
    snippet="".join(lines[start_line-1:end_line])
    offset=start_line-1
    starts=line_starts(snippet)
    mask=mask_comments_and_strings(snippet)
    absline=lambda pos: offset+line_for(starts,pos)
    label_map=explicit_labels(mask,starts)
    points=[];choices=[];guards=[];jumps=[]
    seen=set()
    for m in SELECT.finditer(mask):
        p=m.end()-1;end=matching(mask,p)
        if end<0:continue
        ln=absline(m.start())
        if (ln,'select') in seen:continue
        seen.add((ln,'select'))
        arg=snippet[p+1:end]
        points.append((ln,'select',arg[:MAX_FIELD]))
        targets=switch_cases(mask,m.start(),end,starts)
        method='switch_case' if targets else 'unresolved'
        if not targets:
            targets=selected_if_target(mask,m.start(),end,starts)
            if targets:method='if_selected_equals'
        for idx,label in options_for_select(arg):
            tgt=targets.get(idx) if idx>0 else None
            resolution=method if tgt else ('dynamic_menu' if idx==0 else 'unresolved')
            choices.append((ln,'select',idx,label[:MAX_FIELD] if label is not None else None,offset+tgt if tgt else None,resolution))
    for m in MENU.finditer(mask):
        line0=mask.rfind('\n',0,m.start())+1
        # A menu command can appear after a conditional, but must have a terminal ';'.
        term=statement_end(mask,m.end())
        if term<0 or term-m.start()>4000:continue
        arg=snippet[m.end():term].strip()
        parts=split_top_level(arg)
        if len(parts)<2:continue
        ln=absline(m.start())
        if (ln,'menu') in seen:continue
        seen.add((ln,'menu'))
        points.append((ln,'menu',arg[:MAX_FIELD]))
        for idx in range(0,len(parts)-1,2):
            label=literal(parts[idx]);target_name=parts[idx+1].strip()
            target=label_map.get(target_name)
            choices.append((ln,'menu',idx//2+1,label[:MAX_FIELD] if label is not None else None,
                            offset+target if target else None,"menu_label" if target else "unresolved"))
    for m in IF.finditer(mask):
        op=m.end()-1
        close=matching(mask,op)
        if close<0:continue
        ln=absline(m.start())
        if any(x[0]==ln for x in guards):continue
        tail=re.match(r"\s*\{",mask[close+1:])
        end_line=None
        if tail:
            b=close+1+tail.end()-1
            closing=matching(mask,b,'{','}')
            if closing>=0:end_line=absline(closing)
        guards.append((ln,snippet[op+1:close][:MAX_FIELD],end_line))
    for m in GOTO.finditer(mask):
        ln=absline(m.start())
        label=m.group(1)
        target=label_map.get(label)
        jumps.append((ln,label,offset+target if target else None,"goto_label" if target else "unresolved"))
    return {"points":points,"choices":choices,"guards":guards,"jumps":jumps}

def create_flow(db):
    db.executescript(SCHEMA)
    # Recreated from source on every build; preserve parent script data.
    with db:
        db.execute("DELETE FROM npc_flow_options")
        db.execute("DELETE FROM npc_flow_points")
        db.execute("DELETE FROM npc_flow_conditions")
        db.execute("DELETE FROM npc_flow_jumps")
    summary={"points":0,"options":0,"resolved_options":0,"guards":0,"jumps":0,"resolved_jumps":0}
    for sid,blob in db.execute("SELECT id,content_zlib FROM provenance ORDER BY id"):
        try:raw=zlib.decompress(blob).decode("utf-8-sig")
        except UnicodeDecodeError:raw=zlib.decompress(blob).decode("cp1252",errors="replace")
        npc_rows=db.execute("SELECT ordinal,line_start,line_end FROM script_npcs WHERE source_id=? ORDER BY ordinal",(sid,)).fetchall()
        with db:
            for ordinal,start,end in npc_rows:
                extracted=extract_npc(raw,start,end)
                for ln,kind,expr in extracted["points"]:
                    db.execute("INSERT OR IGNORE INTO npc_flow_points(source_id,npc_ordinal,line_no,kind,expression) VALUES(?,?,?,?,?)",(sid,ordinal,ln,kind,expr))
                    summary["points"]+=1
                key_to_id={(ln,kind):pid for pid,ln,kind in db.execute("SELECT id,line_no,kind FROM npc_flow_points WHERE source_id=? AND npc_ordinal=?",(sid,ordinal))}
                for ln,kind,opt,label,target,resolution in extracted["choices"]:
                    pid=key_to_id.get((ln,kind))
                    if pid is None:continue
                    db.execute("INSERT OR REPLACE INTO npc_flow_options VALUES(?,?,?,?,?)",(pid,opt,label,target,resolution))
                    summary["options"]+=1
                    if target is not None:summary["resolved_options"]+=1
                for ln,expr,end_ln in extracted["guards"]:
                    db.execute("INSERT OR REPLACE INTO npc_flow_conditions VALUES(?,?,?,?,?)",(sid,ordinal,ln,expr,end_ln))
                    summary["guards"]+=1
                for ln,label,target,resolution in extracted["jumps"]:
                    db.execute("INSERT OR REPLACE INTO npc_flow_jumps VALUES(?,?,?,?,?,?)",(sid,ordinal,ln,label,target,resolution))
                    summary["jumps"]+=1
                    if target is not None:summary["resolved_jumps"]+=1
    return summary
