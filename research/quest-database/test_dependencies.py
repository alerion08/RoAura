import sqlite3
import unittest
from dependencies import build_dependency_paths

class DependencyTests(unittest.TestCase):
    def test_multiple_hops_branch_and_cycles(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript("""CREATE TABLE wiki_pages(id INTEGER PRIMARY KEY);
          CREATE TABLE wiki_quest_prerequisite_links(page_id INTEGER,prerequisite_page_id INTEGER);""")
        db.executemany("INSERT INTO wiki_pages VALUES(?)",[(i,) for i in (1,2,3,4)])
        db.executemany("INSERT INTO wiki_quest_prerequisite_links VALUES(?,?)",
                       [(1,2),(2,3),(3,1),(1,4)])
        summary=build_dependency_paths(db)
        self.assertEqual(summary["direct_edges"],4)
        self.assertGreater(summary["transitive_references"],0)
        self.assertGreater(summary["nodes_in_detected_cycles"],0)
        self.assertEqual(db.execute(
            "SELECT min_hops FROM wiki_prerequisite_paths WHERE page_id=1 AND dependency_page_id=3"
            ).fetchone()[0],2)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_prerequisite_paths WHERE page_id=dependency_page_id").fetchone()[0],0)
        self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(),[])
        before=db.execute("SELECT count(*) FROM wiki_prerequisite_paths").fetchone()[0]
        build_dependency_paths(db)
        self.assertEqual(db.execute("SELECT count(*) FROM wiki_prerequisite_paths").fetchone()[0],before)

if __name__=="__main__":
    unittest.main()
