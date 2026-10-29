"""Rank per-quest data gaps for source verification and optional server-specific validation.

This queue is a quality control artifact, not a recommendation to the player.
Only explicit SQL evidence is shown; no quest IDs or NPCs are fabricated.
"""
from collections import Counter
from datetime import datetime,timezone
from runtime import key_page,script_variant_scope

def summarize_review_queue(db):
    result=[]
    query="""SELECT p.id,p.title,p.url,p.status,
             COALESCE(q.refresh_state,'unknown'),
             COALESCE(r.review_state,'not_reviewed'),
             (SELECT count(*) FROM wiki_steps s WHERE s.page_id=p.id) AS steps,
             (SELECT count(*) FROM wiki_script_links l WHERE l.page_id=p.id) AS linked_scripts,
             (SELECT count(DISTINCT l.step_no) FROM wiki_step_npc_links l WHERE l.page_id=p.id) AS npc_steps,
             (SELECT count(*) FROM wiki_answer_menu_matches m WHERE m.page_id=p.id) AS answer_matches
             FROM wiki_pages p LEFT JOIN wiki_refresh_state q ON q.page_id=p.id
             LEFT JOIN wiki_script_reviews r ON r.page_id=p.id
             ORDER BY p.id"""
    for pid,title,url,status,freshness,review,steps,links,npc_steps,answers in db.execute(query):
        scopes=sorted({script_variant_scope(path) for (path,) in db.execute(
            """SELECT s.path FROM wiki_script_links l
               JOIN provenance s ON s.id=l.source_id
               WHERE l.page_id=?""",(pid,))})
        issues=[]
        if steps==0:issues.append("no_extracted_walkthrough")
        if not links:issues.append("no_script_candidate")
        if links and npc_steps==0 and steps:issues.append("no_step_npc_correspondence")
        if review in ("ambiguous_top","cross_source_variant"):issues.append(review)
        if freshness not in ("fresh","fresh_empty"):issues.append("not_freshly_verified")
        if "renewal" in scopes and ("pre-re" in scopes or "common" in scopes):
            issues.append("mixed_gameplay_variants")
        if steps>0 and npc_steps<steps:issues.append("incomplete_step_npc_mapping")
        # Priority is a review queue triage label, not a ranking of player quests.
        critical=("no_script_candidate" in issues and steps>=5)
        high=(steps>0 and ("no_script_candidate" in issues or
             "no_step_npc_correspondence" in issues or "mixed_gameplay_variants" in issues))
        priority="critical" if critical else "high" if high else "medium" if issues else "low"
        result.append({
            "quest_key":key_page(url),"title":title,"guide_status":status,
            "freshness":freshness,"script_review":review,
            "steps":steps,"linked_scripts":links,"steps_with_npc":npc_steps,
            "matched_guide_answers":answers,"source_scopes":scopes,
            "issues":issues,"review_priority":priority,
            "assurance":"source-derived candidates only; all server implementations unverified"
        })
    rank={"critical":0,"high":1,"medium":2,"low":3}
    result.sort(key=lambda x:(rank[x["review_priority"]],-x["steps"],x["quest_key"]))
    total=len(result)
    metrics={
        "indexed_pages":total,
        "pages_with_steps":sum(x["steps"]>0 for x in result),
        "pages_with_scripts":sum(x["linked_scripts"]>0 for x in result),
        "pages_with_step_npc_links":sum(x["steps_with_npc"]>0 for x in result),
        "pages_without_script":sum("no_script_candidate" in x["issues"] for x in result),
        "pages_with_mixed_scopes":sum("mixed_gameplay_variants" in x["issues"] for x in result),
        "pages_not_freshly_verified":sum("not_freshly_verified" in x["issues"] for x in result),
        "priority_counts":dict(Counter(x["review_priority"] for x in result)),
        "unverified_by_server":total,
    }
    return {"schema_version":1,
            "purpose":"research/manual review queue; not game instructions",
            "metrics":metrics,"quests":result}
