# ROQuestie — stage 11 source-code review findings

Research branch: `research/quest-database`. Published evidence:
[`output/npc_source_dossiers.json`](output/npc_source_dossiers.json),
[`output/missing_script_discovery.json`](output/missing_script_discovery.json),
[`output/coverage.json`](output/coverage.json).

**Status:** 25 previously unlinked source proposals have now been cross-checked
lexically against their named NPC blocks. **None are promoted into
`wiki_script_links` or the RoAura consumer catalog.** NPC names, script
execution and guide steps are not interchangeable sources of truth.

## Verified scope and evidence tiers

- Exact code provenance: project, script path, retained Git blob SHA,
  NPC/source line, textual `mes` arguments, static menu and guard references,
  lexical script state facts and explicit Quest IDs when present.
- The dossier `dialogue_and_quest_state_present_review_required` means only
  that named NPC code contains both categories; it **does not** establish
  that the literal Quest ID belongs uniquely to that wiki guide.
- `dialogue_and_other_state_present_review_required` covers legacy scripts
  that often use local/state variables rather than quest journal IDs. Absence
  of `setquest` is **not** evidence that a quest is missing.
- Shared source files can contain many independent quest chains, so matching
  two names in one file is **not sufficient** for verifying a single chain.
  New `source_token_overlap` and literal-coordinate checks are only review
  evidence, not a source of automatic gameplay instructions.
- Every `can_auto_promote_to_quest_link` remains `false`.

## Specific source checks performed

### Einbroch Murder Quest — verify shared state track, not just names

Source: rAthena
[`npc/quests/quests_ein.txt`](https://github.com/rathena/rathena/blob/3a83a1fcac9708bed1ba55d61486b1c4ae119f18/npc/quests/quests_ein.txt),
snapshot Git blob SHA `3a83a1fcac9708bed1ba55d61486b1c4ae119f18`.

- Around source line **4223**, the comment `Murder Quest :: ein_main_tre`
  immediately introduces `Buender Hikeman#ein`. His initial dialogue
  checks `Shinokas_Quest`.
- Around line **5294**, `Scientist#ein` checks `Shinokas_Quest == 7`
  and moves it to `8` after a particular conversation choice.
- Around line **3453**, `Zelmeto` uses a **different**
  progression variable, `EinFactory`, in the inspected code branch.

**Audit finding:** Buender + Scientist have meaningful source-level
corroboration for the *same variable family*, whereas Zelmeto can represent
an additional or unrelated factory quest step. The wiki guide must be checked
against all relevant branches before associating any `quest_id_candidates`
with a single chain. **Not approved for automatic guidance.**

### Rebirth Walkthrough / Prayer To Heaven

Source: rAthena
[`npc/jobs/valkyrie.txt`](https://github.com/rathena/rathena/blob/9a4dcfe4903f85bd3e76a980befcf2298fae93c8/npc/jobs/valkyrie.txt),
snapshot Git blob SHA `9a4dcfe4903f85bd3e76a980befcf2298fae93c8`.

- `Valkyrie#` is defined at line **43**, with conditional access checks
  including `BaseLevel > 98`, `JobLevel > 49` and class restrictions.
- `Metheus Sylphe#Library` is defined at line **215**, uses
  `valkyrie_Q` and has a documented donation choice of **1,285,000 zeny**.
- Two differently titled wiki walkthroughs propose the **same NPC pair** and
  source file. Determine whether they are overlapping views of the same
  process or distinct guide variants; preserve distinct wiki source records.

**Audit finding:** code-level start conditions and dialogue exist, but these
pages must not automatically become duplicate quests or independently tracked
Quest IDs. Not validated for any specific server.

### Seasonal name collision

`2014 Fourth of July Event` was matched on two `quests_amatsu.txt`
variants by the names Grandma and Grandpa. The script filenames are not
specific to that historical event. Both dossier rows are classified as
`seasonal_source_identity_check`, not confirmed event implementation.
This demonstrates why even multiple-name evidence can misidentify a guide.

## Build results

Full GitHub Actions run
[#40943245833](https://github.com/alerion08/RoAura/actions/runs/40943245833):
**success**, integrity OK, foreign-key violations 0.
Focused regression run
[#40943245769](https://github.com/alerion08/RoAura/actions/runs/40943245769):
**48 tests passed**.

- 25 source proposals assessed; 688 source facts copied with NPC and line
  provenance into the new research tables.
- 12 proposals: matched NPCs have dialogue and lexical quest-state commands.
- 11 proposals: dialogue and other state facts (item/variable), without a
  lexical quest-state command within those matched NPC blocks.
- 2 proposals: flagged seasonal source-identity risk.
- 11 proposals share a script with another proposed guide; no source link was
  promoted automatically.
- None of the 25 had an exact wiki-to-source NPC coordinate match in the
  named guide steps. This is **not** a disproof; it limits corroboration.
- Research SQLite schema v10, consumer ROQuestie SQLite **remains v4**;
  prior 939 source scripts, 286 wiki pages, 3,420 guide steps and 574
  candidate script links are preserved.

## Next review gates

Before accepting a source link as source-corroborated, verify the exact wiki
step order against source dialogue/guards, shared quest-variable progression,
map coordinates and prerequisite conditions; distinguish independent quest
segments in shared script files. A *source-corroborated* link is still not
an assertion that every RO server enables the same code or Quest ID.

Only source-corroborated, non-conflicting links should be eligible for a
separate human-reviewed alias/variant catalog. Automatic next-step game
instructions need stricter per-server evidence; keep character progress
separate from ROQuestie updates.
