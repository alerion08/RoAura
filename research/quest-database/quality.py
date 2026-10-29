"""Wiki refresh provenance, safe cache fallback, and content regression guards.

The guide text remains an original source extraction. A stale success is never
reported as a fresh HTTP success, and a failed update must not destroy it.
"""
import datetime
import sqlite3

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_refresh_state (
 page_id INTEGER PRIMARY KEY REFERENCES wiki_pages(id),
 refresh_state TEXT NOT NULL,
 last_attempt_utc TEXT,
 last_success_utc TEXT,
 last_error TEXT,
 stored_steps INTEGER NOT NULL DEFAULT 0 CHECK(stored_steps>=0)
);
CREATE INDEX IF NOT EXISTS wiki_refresh_by_state
 ON wiki_refresh_state(refresh_state);
"""
VALID_STATES={"legacy_cached","fresh","fresh_empty","cached_http_error",
              "cached_parse_regression","never_fetched","cached_not_discovered"}

def utcnow():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def ensure_quality_schema(db):
    db.executescript(SCHEMA)
    # Upgrading an existing SQLite cannot recover last successful fetch time.
    # Mark this clearly as legacy, rather than inventing freshness.
    db.execute("""INSERT OR IGNORE INTO wiki_refresh_state
        (page_id,refresh_state,stored_steps)
        SELECT p.id,
            CASE WHEN count(s.step_no)>0 THEN 'legacy_cached' ELSE 'never_fetched' END,
            count(s.step_no)
        FROM wiki_pages p LEFT JOIN wiki_steps s ON s.page_id=p.id
        GROUP BY p.id""")
    # Earlier collector set wiki_pages.status=fetch_error despite preserving
    # parsed content. Repair the indexing status, but keep unknown fetch age.
    db.execute("""UPDATE wiki_pages
      SET status=CASE
        WHEN (SELECT mode FROM wiki_parse_quality q WHERE q.page_id=wiki_pages.id)='numbered' THEN 'walkthrough_parsed'
        WHEN (SELECT mode FROM wiki_parse_quality q WHERE q.page_id=wiki_pages.id)='narrative' THEN 'narrative_parsed'
        ELSE status END
      WHERE status='fetch_error' AND EXISTS
        (SELECT 1 FROM wiki_steps s WHERE s.page_id=wiki_pages.id)""")
    db.commit()

def too_few_steps(previous,extracted):
    """Guard catastrophic parsing regressions, allow genuine small changes."""
    if previous<=0:return False
    if extracted==0:return True
    if previous>=5 and extracted<max(2,int(previous*0.5)):return True
    return False

def update_state(db,pid,state,steps=None,error=None,success=False):
    if state not in VALID_STATES:raise ValueError(state)
    t=utcnow()
    sql="""UPDATE wiki_refresh_state
        SET refresh_state=?,last_attempt_utc=?,
            last_success_utc=CASE WHEN ? THEN ? ELSE last_success_utc END,
            last_error=?,
            stored_steps=COALESCE(?,stored_steps)
        WHERE page_id=?"""
    db.execute(sql,(state,t,int(success),t,error[:500] if error else None,steps,pid))

def state_counts(db):
    return dict(db.execute("SELECT refresh_state,count(*) FROM wiki_refresh_state GROUP BY refresh_state"))

def dataset_counts(db):
    return {
        "script_sources":db.execute("SELECT count(*) FROM provenance").fetchone()[0],
        "wiki_pages":db.execute("SELECT count(*) FROM wiki_pages").fetchone()[0],
        "wiki_steps":db.execute("SELECT count(*) FROM wiki_steps").fetchone()[0],
        "wiki_pages_with_steps":db.execute("SELECT count(DISTINCT page_id) FROM wiki_steps").fetchone()[0],
        "wiki_pages_with_script":db.execute("SELECT count(DISTINCT page_id) FROM wiki_script_links").fetchone()[0],
    }

def check_nonregression(previous,current):
    """Fail closed for major unexpected data losses in incremental snapshots."""
    losses=[]
    thresholds={"script_sources":0.95,"wiki_pages":0.98,"wiki_steps":0.95}
    for field,ratio in thresholds.items():
        old=previous.get(field,0)
        if old>=100 and current[field]<(old*ratio):
            losses.append({"field":field,"before":old,"after":current[field]})
    if losses:
        raise RuntimeError("Quest data regression would publish a smaller dataset: "+repr(losses))
    return {"passed":True,"before":previous,"after":current,
            "limits":"script_sources>=95%, wiki_pages>=98%, wiki_steps>=95% of previous snapshot"}
