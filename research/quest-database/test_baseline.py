import unittest
from baseline import classify_quest_title, assemble_core_worklist

class BaselineTests(unittest.TestCase):
    def test_classification_is_server_neutral_and_conservative(self):
        self.assertEqual(classify_quest_title("2014 Fourth of July Event")[0], "historical_or_seasonal")
        self.assertEqual(classify_quest_title("Biolabs Entrance Quest")[0],"dungeon_or_access_candidate")
        self.assertEqual(classify_quest_title("Swordman Job Change Guide")[0],"profession_quest_candidate")
        self.assertEqual(classify_quest_title("President Quest")[0],"storyline_candidate")
        self.assertEqual(classify_quest_title("TaeKwon Kid")[0],"profession_reference_candidate")
        self.assertEqual(classify_quest_title("Unknown Quest")[0],"unsorted_quest_candidate")

    def test_no_server_availability_is_claimed(self):
        def item(title):
            return {"quest_key":"irowiki-classic:"+title.replace(" ","_"),"title":title,
                    "steps":12,"linked_scripts":0,"steps_with_npc":0,
                    "source_scopes":[],"issues":["no_script_candidate"],
                    "review_priority":"critical"}
        out=assemble_core_worklist({"quests":[item("President Quest"),
                                            item("Winter Event"),item("Unknown Quest")]})
        self.assertEqual(out["product"],"ROQuestie")
        self.assertEqual(out["metrics"]["server_verified"],0)
        self.assertEqual(out["metrics"]["seasonal_archive_candidates"],1)
        self.assertTrue(all(x["server_availability"]=="unknown" for x in out["quests"]))
        self.assertTrue(all(x["universal_availability_claim"] is False for x in out["quests"]))
        self.assertEqual({x["title"] for x in out["quests"]},
                         {"President Quest","Winter Event","Unknown Quest"})

if __name__=="__main__":
    unittest.main()
