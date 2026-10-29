"""Review ambiguous guide-to-script mappings and match quoted guide answers to menus.

Evidence is heuristic, never verified gameplay behavior. No automatic
promotion of candidate links to authoritative quest instructions.
"""
import re

SCHEMA = """
CREATE TABLE IF NOT EXISTS wiki_script_reviews(
 page_id INTEGER PRIMARY KEY REFERENCES wiki_pages(id),
 candidates INTEGER NOT NULL,
 best_score INTEGER,
 best_score_ties INTEGER NOT NULL,
 npc_evidence INTEGER NOT NULL,
 review_state TEXT NOT NULL,
 note TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS wiki_answer_menu_matches(
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 step_no INTEGER NOT NULL,
 point_id INTEGER NOT NULL REFERENCES npc_flow_points(id),
 option_no INTEGER NOT NULL,
 review_state TEXT NOT NULL,
 PRIMARY KEY(page_id,step_no,point_id,option_no)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS wiki_answer_menu_point ON wiki_answer_menu_matches(point_id);
"""
GENERIC_ANSWERS = {"yes","no","cancel","talk","back","exit","quit","ok","okay",
                   "continue","next","help","return","i agree","nothing","leave"}

def norm_answer(s):
    s = re.sub(r"\^[0-9a-fA-F]{6}", "",s or "")
    s = re.sub(r"\s+", " ",s).strip().strip(".!? \t\"'").casefold()
    return s

def create_reviews(db):
    db.executescript(SCHEMA)
    with db:
        db.execute("DELETE FROM wiki_answer_menu_matches")
        db.execute("DELETE FROM wiki_script_reviews")
        pages=db.execute("SELECT id FROM wiki_pages ORDER BY id").fetchall()
        for (pid,) in pages:
            links=db.execute("""SELECT l.source_id,l.score,p.path FROM wiki_script_links l JOIN provenance p ON p.id=l.source_id WHERE l.page_id=? ORDER BY l.score DESC""",(pid,)).fetchall()
            score=links[0][1] if links else None
            top_ties=sum(x[1]==score for x in links) if links else 0
            npc_support=db.execute("""SELECT count(*) FROM wiki_step_npc_links
                WHERE page_id=? AND score>=85""",(pid,)).fetchone()[0]
            if not links:
                state="no_candidate"
                note="No script filename candidate. Not proof the quest is absent."
            elif top_ties>1:
                best=[path for _sid,value,path in links if value==score]
                stems={path.rsplit("/",1)[-1].lower() for path in best}
                if len(stems)==1:
                    state="cross_source_variant"
                    note="Several emulator/source variants share the strongest script filename; compare implementations, not yet verified."
                else:
                    state="ambiguous_top"
                    note="Different script filenames tie for strongest match; manual quest/state comparison required."
            elif npc_support:
                state="candidate_with_npc_evidence"
                note="Filename match and at least one NPC exact-coordinate hint; still unverified."
            else:
                state="filename_candidate"
                note="Filename-based match only; no confirmed NPC/quest-state alignment."
            db.execute("INSERT INTO wiki_script_reviews VALUES(?,?,?,?,?,?,?)",
                      (pid,len(links),score,top_ties,npc_support,state,note))
        # Match only distinct, literal guide choices to recorded literal menu options.
        # Short/generic labels are unhelpful and must not become automatic links.
        entries=db.execute("""SELECT d.page_id,d.step_no,d.value
            FROM wiki_details d
            WHERE d.kind='guide_answer' ORDER BY d.page_id,d.step_no""").fetchall()
        cache={}
        for pid,step,answer in entries:
            expected=norm_answer(answer)
            if len(expected)<6 or expected in GENERIC_ANSWERS:continue
            if pid not in cache:
                cache[pid]=db.execute("""SELECT p.id,o.option_no,o.label
                    FROM wiki_script_links l
                    JOIN npc_flow_points p ON p.source_id=l.source_id
                    JOIN npc_flow_options o ON o.point_id=p.id
                    WHERE l.page_id=? AND l.score>=75
                      AND o.label IS NOT NULL""",(pid,)).fetchall()
            matches=[(point,opt) for point,opt,label in cache[pid]
                     if norm_answer(label)==expected]
            # A repeated label in a multi-NPC quest is not a unique dialogue destination.
            state="candidate_unique_label" if len(matches)==1 else "ambiguous_repeated_label"
            for point,opt in matches:
                db.execute("INSERT OR IGNORE INTO wiki_answer_menu_matches VALUES(?,?,?,?,?)",
                           (pid,step,point,opt,state))
    return {
        "pages_no_script": db.execute("SELECT count(*) FROM wiki_script_reviews WHERE review_state='no_candidate'").fetchone()[0],
        "pages_ambiguous": db.execute("SELECT count(*) FROM wiki_script_reviews WHERE review_state='ambiguous_top'").fetchone()[0],
        "pages_cross_source_variants": db.execute("SELECT count(*) FROM wiki_script_reviews WHERE review_state='cross_source_variant'").fetchone()[0],
        "pages_candidate_with_npc_evidence": db.execute("SELECT count(*) FROM wiki_script_reviews WHERE review_state='candidate_with_npc_evidence'").fetchone()[0],
        "guide_answer_menu_matches": db.execute("SELECT count(*) FROM wiki_answer_menu_matches").fetchone()[0],
    }
