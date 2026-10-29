"""Quest research parser and conservative cross-link regression tests."""
import sqlite3
import unittest

from enrich import parse_guide, step_evidence, script_score, link_database, ensure_enrichment_schema


class GuideParserTests(unittest.TestCase):
    def test_numbered_paragraphs_with_continuation_and_bullets(self):
        raw = """<div id="mw-content-text"><div class="mw-parser-output">
          <table><tr><th>Requirements</th></tr><tr><td>Base Level: 70</td></tr>
          <tr><th>Rewards</th></tr><tr><td>Experience</td></tr></table>
          <p>1. Visit Juno (yuno 51, 105) and select "Sure".</p>
          <p>2. Enter the academy.</p><p>Talk to the guard.</p>
          <ul><li>Lecollane</li><li>Culinary Wine</li></ul><p>3. Return to Juno.</p>
          </div></div>"""
        steps,q,meta=parse_guide(raw)
        self.assertEqual(q["numbered"],3)
        self.assertEqual(steps[0].source_number,1)
        self.assertIn("Talk to the guard.",steps[1].instruction)
        self.assertIn("Culinary Wine",steps[1].instruction)
        self.assertEqual(meta["base_level"],70)
        details=step_evidence(steps[0].instruction,["yuno"])
        self.assertIn(("map_coord","yuno:51:105"),details)
        self.assertIn(("guide_answer","Sure"),details)
        self.assertNotIn(("bare_coord","51:105"),details)

    def test_narrative_fallback_is_labeled(self):
        html="""<div class="mw-parser-output">
        <p>Talk to the priest in Prontera to begin your adventure.</p>
        <p>Return when the mission is complete.</p></div>"""
        steps,q,_=parse_guide(html)
        self.assertEqual(q["mode"],"narrative")
        self.assertEqual(len(steps),2)

    def test_unrelated_table_and_footer_not_steps(self):
        html="""<div class="mw-parser-output">
        <table><tr><td>1. Not a quest step</td></tr></table>
        <p>1. Actual quest step.</p><div class="printfooter">
        <p>2. Not a step.</p></div></div>"""
        steps,_,_=parse_guide(html)
        self.assertEqual([x.instruction for x in steps],["Actual quest step."])

    def test_filename_candidates_are_conservative(self):
        self.assertEqual(script_score("Kiel Hyre Quest","npc/quests/kiel_hyre_quest.txt")[0],95)
        self.assertEqual(script_score("Juice Quest","npc/quests/juice_maker.txt")[0],60)
        self.assertEqual(script_score("Goblin Quest","npc/quests/archer.txt")[0],0)

    def test_link_evidence_keeps_candidate_status(self):
        db=sqlite3.connect(":memory:")
        db.execute("PRAGMA foreign_keys=ON")
        db.executescript("""CREATE TABLE wiki_pages(id INTEGER PRIMARY KEY,title TEXT,status TEXT);
        CREATE TABLE provenance(id INTEGER PRIMARY KEY,path TEXT);
        CREATE TABLE script_npcs(id INTEGER PRIMARY KEY,source_id INTEGER REFERENCES provenance(id),
          map TEXT,x INTEGER,y INTEGER,name TEXT);
        CREATE TABLE wiki_steps(page_id INTEGER,step_no INTEGER,instruction TEXT);""")
        ensure_enrichment_schema(db)
        db.execute("INSERT INTO wiki_pages VALUES(1,'Kiel Hyre Quest','walkthrough_parsed')")
        db.execute("INSERT INTO provenance VALUES(1,'npc/quests/kiel_hyre_quest.txt')")
        db.execute("INSERT INTO script_npcs VALUES(1,1,'yuno',51,105,'Bar Keeper#kiel')")
        db.execute("INSERT INTO wiki_steps VALUES(1,1,'Talk to Bar Keeper at yuno 51,105')")
        summary=link_database(db)
        self.assertEqual(summary["linked_pages"],1)
        self.assertEqual(summary["wiki_step_npc_links"],1)
        self.assertEqual(db.execute("SELECT status FROM wiki_script_links").fetchone()[0],"candidate")
        self.assertEqual(db.execute("SELECT score FROM wiki_step_npc_links").fetchone()[0],95)


if __name__=="__main__":
    unittest.main()
