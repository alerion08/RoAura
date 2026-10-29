# Server-specific quest verification protocol — Tides of Comodo

**Status:** Research only. Nothing in the quest database is currently verified
against live ToC NPC scripts or a controlled in-game test session.

## Source/assurance levels

- `wiki_documented`: explicitly present in a named iRO Wiki Classic page,
  with page URL and a stable content hash (not necessarily freshly retrieved).
- `emulator_source_derived`: specific rAthena/Hercules filename, Git blob SHA,
  NPC code line, map/name/coordinates, Quest ID and condition expression.
- `candidate_link`: correspondence inferred by filename/header/NPC proximity.
  Its numeric score is **not** a probability.
- `toc_server_authoritative`: requires a real ToC source supplied by its
  operators: enabled quest script manifest or implementation, date/revision.
- `toc_gameplay_observed`: a reproducible quest acceptance/step/completion
  observation on ToC with client/server version and exact character state.

Never promote the first three levels to either ToC level automatically.

## Primary review queue

Read `output/critical_quest_triage.json` rather than considering every old
iRO seasonal event a permanent ToC quest. Source entries are retained even
when likely historical. First investigate core storyline and dungeon access
quests, especially President Quest, Biolabs Entrance, Odin's Temple Excavation,
Einbroch Murder, Thanatos Tower, and regional quest prerequisites. Check
whether the quest is actually present before collecting a complete walkthrough.

Each verification needs: (1) canonical quest and content/version marker,
(2) server source or precise in-game evidence, (3) NPC name, map and cell,
(4) starting and step conditions, consumed/nonconsumed items and output,
(5) Quest ID/log entry if one exists, (6) dialogue choices and transition
conditions, (7) quest completion criteria and known branches, (8) evidence
timestamp and a reviewer decision. Not every quest has a Quest Window entry;
do not manufacture a Quest ID for an untracked script quest.

## Dataset rules

- A script with path `npc/re/...` is a Renewal source. A ToC quest is **not**
  assigned the Renewal implementation without affirmative server evidence.
- Common and Pre-Renewal scripts also require verification of the active
  ToC variant. Source filenames alone are insufficient.
- The new consumer SQLite `quest_id_candidates` view is an audit aid: Quest
  ID appearing in a candidate script does not mean that ID is the wiki
  quest's journal ID on ToC.
- Never mutate a user's saved quest progress while updating a catalog.
- Game observations can be recorded manually; do not bypass server anti-cheat,
  inject packets, or rely on speculative packet layouts.
- The ToC public website has not supplied an accessible authoritative
  quest-script/Quest ID list in this research stage. Its item/monster/MVP
  database, if accessible, is a separate source and cannot validate quests.

## Minimum acceptance before implementing automatic quest navigation

For a specific quest and server revision, require a verified NPC position and
start requirements, all branch conditions and allowed choices across the
player's tested paths, exact progression checkpoints, and a reliable way to
distinguish started, active and completed. If any item is missing, RoAura may
display a cited guide with an *unverified* indicator, but must not
automatically assert the next dialogue response or completed quest state.
