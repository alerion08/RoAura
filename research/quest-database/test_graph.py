"""Conservative control flow extraction unit tests. No game execution."""
import sqlite3
import unittest
import zlib

from graph import (create_flow, extract_npc, mask_comments_and_strings,
                   options_for_select, split_top_level)

SAMPLE = """prontera,100,110,4 script Test#example 55,{
 if (countitem(909) > 0) {
   mes "[Alice]";
   mes "Have you completed the task?";
   switch(select("Yes:No:More information")) {
     case 1:
       if (Zeny > 100) {
         mes "Pay 100";
       }
       goto L_Reward;
     case 2:
       close;
     case 3:
       switch(select("A:B")) {
         case 1: mes "A";
         case 2: mes "B";
       }
       break;
   }
 }
 if (select("Keep playing:Leave now") == 1) {
   mes "You stayed.";
 }
 menu "Help",L_Help,"Not now",L_Exit;
L_Help:
 mes "Information here";
 goto L_Exit;
L_Reward:
 getitem 501,1;
L_Exit:
 close;
}
"""


class FlowParserTests(unittest.TestCase):
    def test_masks_comments_and_quotes_but_preserves_offsets(self):
        src='// case 6:\nmes "if (select(\\"bad\\"))"; /* menu "Bad",L; */ goto Next;'
        masked=mask_comments_and_strings(src)
        self.assertEqual(len(masked),len(src))
        self.assertEqual(masked.count('\n'),src.count('\n'))
        self.assertNotIn("case 6:",masked)
        self.assertNotIn("select",masked)
        self.assertIn("goto Next;",masked)

    def test_nested_colons_comma_escaped(self):
        self.assertEqual(split_top_level('"one,two",foo(1,2),"third"'),['"one,two"','foo(1,2)','"third"'])
        self.assertEqual(options_for_select('"Yes:No"'),[(1,"Yes"),(2,"No")])
        self.assertEqual(options_for_select('choice_var'),[(0,None)])
        self.assertEqual(options_for_select('"First","Second"'),[(1,"First"),(2,"Second")])

    def test_switch_cases_and_nested_switch_scopes(self):
        record=extract_npc(SAMPLE,1,len(SAMPLE.splitlines()))
        switches=[p for p in record["points"] if p[1]=='select']
        self.assertEqual(len(switches),3)
        first=switches[0][0]
        options=[x for x in record["choices"] if x[0]==first]
        self.assertEqual([x[3] for x in options],["Yes","No","More information"])
        self.assertEqual([x[5] for x in options],["switch_case"]*3)
        self.assertEqual([x[4] for x in options],[6,11,13])
        nested=[x for x in record["choices"] if x[3] in ("A","B")]
        self.assertEqual([x[5] for x in nested],["switch_case","switch_case"])

    def test_if_select_only_true_arm_is_explicit(self):
        record=extract_npc(SAMPLE,1,len(SAMPLE.splitlines()))
        yes=[x for x in record["choices"] if x[3]=="Keep playing"][0]
        no=[x for x in record["choices"] if x[3]=="Leave now"][0]
        self.assertEqual(yes[5],"if_selected_equals")
        self.assertIsNotNone(yes[4])
        self.assertIsNone(no[4])

    def test_menu_and_goto_resolve_only_inside_npc(self):
        record=extract_npc(SAMPLE,1,len(SAMPLE.splitlines()))
        menu=[x for x in record["choices"] if x[1]=="menu"]
        self.assertEqual([x[3] for x in menu],["Help","Not now"])
        self.assertEqual([x[5] for x in menu],["menu_label","menu_label"])
        jump=[x for x in record["jumps"] if x[1]=="L_Reward"]
        self.assertEqual(len(jump),1)
        self.assertEqual(jump[0][3],"goto_label")

    def test_unresolved_dynamic_or_missing_target(self):
        source="""yuno,10,10,2 script Test 100,{
          switch(select(.@choices$)) {
            case 1: mes "Go";
          }
          menu "Bad",UnknownLabel;
          goto Missing;
          close;
        }"""
        record=extract_npc(source,1,len(source.splitlines()))
        self.assertEqual(record["choices"][0][3],None)
        self.assertIsNone(record["choices"][0][4])
        self.assertEqual(record["choices"][0][5],"dynamic_menu")
        self.assertEqual(record["choices"][1][4],None)
        self.assertEqual(record["jumps"][0][2],None)

    def test_sqlite_graph_generation_and_idempotency(self):
        con=sqlite3.connect(':memory:')
        con.execute("PRAGMA foreign_keys=ON")
        con.executescript("""CREATE TABLE provenance(id INTEGER PRIMARY KEY,content_zlib BLOB);
          CREATE TABLE script_npcs(source_id INTEGER,ordinal INTEGER,line_start INTEGER,line_end INTEGER);""")
        con.execute("INSERT INTO provenance VALUES(?,?)",(1,zlib.compress(SAMPLE.encode())))
        con.execute("INSERT INTO script_npcs VALUES(1,1,1,?)",(len(SAMPLE.splitlines()),))
        summary=create_flow(con)
        self.assertGreaterEqual(summary["resolved_options"],5)
        self.assertGreaterEqual(summary["resolved_jumps"],2)
        first=con.execute("SELECT count(*) FROM npc_flow_edges").fetchone()[0]
        self.assertGreater(first,0)
        create_flow(con)
        self.assertEqual(con.execute("SELECT count(*) FROM npc_flow_edges").fetchone()[0],first)
        self.assertEqual(con.execute("PRAGMA integrity_check").fetchone()[0],"ok")


if __name__=="__main__":
    unittest.main()
