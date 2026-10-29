import unittest
from triage import classify_critical,triage_review

class TriageTests(unittest.TestCase):
    def test_event_and_core_are_never_automatically_declared_unsupported(self):
        self.assertEqual(classify_critical("2014 Fourth of July Event")[0],"historical_event_or_spotlight")
        self.assertEqual(classify_critical("Nifflheim Spotlight")[0],"historical_event_or_spotlight")
        self.assertEqual(classify_critical("TaeKwon Kid")[0],"class_or_reference_page")
        self.assertEqual(classify_critical("President Quest")[0],"core_or_other_quest_candidate")
        self.assertEqual(classify_critical("Biolabs Entrance Quest")[0],"core_or_other_quest_candidate")

    def test_all_critical_maintained_with_explicit_unknown_server(self):
        def row(title,priority):
            return dict(quest_key="irowiki-classic:"+title.replace(" ","_"),title=title,
              steps=12,source_scopes=[],issues=["no_script_candidate"],
              guide_status="walkthrough_parsed",review_priority=priority)
        data={"quests":[row("President Quest","critical"),row("2014 Fourth of July Event","critical"),
                        row("Other Quest","high")]}
        output=triage_review(data)
        self.assertEqual(output["counts"]["critical_total"],2)
        self.assertEqual(output["queue"][0]["title"],"President Quest")
        self.assertTrue(all(x["target_server_status"]=="unknown" for x in output["queue"]))
        self.assertEqual(output["counts"]["categories"]["historical_event_or_spotlight"],1)

if __name__=="__main__":
    unittest.main()
