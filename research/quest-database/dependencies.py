"""Transitive *documentation* references between quest guides.

Not a gameplay prerequisite solver: boolean AND/OR in wiki requirements is
not fully parsed. A transitive path only means that wiki pages cite other
pages in a chain. Cycles are retained in diagnostics, never as self-edges.
"""
from collections import defaultdict,deque

SCHEMA="""
CREATE TABLE IF NOT EXISTS wiki_prerequisite_paths(
 page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 dependency_page_id INTEGER NOT NULL REFERENCES wiki_pages(id),
 min_hops INTEGER NOT NULL CHECK(min_hops>=1),
 provenance TEXT NOT NULL,
 PRIMARY KEY(page_id,dependency_page_id)
) WITHOUT ROWID;
"""
def build_dependency_paths(db):
    db.executescript(SCHEMA)
    adjacency=defaultdict(set)
    for pid,other in db.execute("SELECT page_id,prerequisite_page_id FROM wiki_quest_prerequisite_links"):
        adjacency[pid].add(other)
    cyclic=set()
    with db:
        db.execute("DELETE FROM wiki_prerequisite_paths")
        for pid in sorted(adjacency):
            visited={pid:0}
            queue=deque([(pid,0)])
            while queue:
                node,dist=queue.popleft()
                for other in sorted(adjacency.get(node,())):
                    if other==pid:
                        cyclic.add(pid)
                        continue
                    if other not in visited:
                        visited[other]=dist+1
                        queue.append((other,dist+1))
            for other,distance in visited.items():
                if other==pid:continue
                db.execute("INSERT INTO wiki_prerequisite_paths VALUES(?,?,?,?)",
                           (pid,other,distance,
                            "direct_wiki_reference" if distance==1 else
                            "transitive_wiki_reference_unverified"))
    return {
        "direct_edges":sum(len(x) for x in adjacency.values()),
        "transitive_references":db.execute("SELECT count(*) FROM wiki_prerequisite_paths WHERE min_hops>1").fetchone()[0],
        "all_reference_pairs":db.execute("SELECT count(*) FROM wiki_prerequisite_paths").fetchone()[0],
        "nodes_in_detected_cycles":len(cyclic),
        "assurance":"page citation graph only; no AND/OR semantics or server verification"
    }
