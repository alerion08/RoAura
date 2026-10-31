# Semantic database progress — 2026-10-31

Status: **partial research dataset, not ready for application integration**.
Scope: database, sources, parsers, curations, exports and research CI only.
No WPF/application/overlay/settings/release files or `main` changed.

## Verified data scope

The original archive retains 939 NPC scripts, 286 wiki records and 3,420
extracted rows. Active selection retains **217 catalog candidates and 2,753
original rows**; 59 source-confirmed historical event records are excluded
and 10 ambiguous event records are quarantined. Quarantine is not a claim
that an event is permanently retired. The active catalog still contains
reference pages and unreviewed candidates; 217 is not a count of independently
verified permanent quests.

The normalized `output/quest_semantic.sqlite` uses semantic schema **1** and
generator **semantic-1.1.0**. All four description fields are populated for
**76 steps across 12 curated guides**, up from the previous uncommitted
6-guide/25-step checkpoint. **2,677 steps in 205 remaining catalog entries
still lack complete localization/context analysis.** `complete` remains false.

| Curated guide | Preserved steps | Specific source issue covered |
| --- | ---: | --- |
| Juice Quest | 3 | Showing Meat versus consuming it; conflicting header |
| Factory Quest | 5 | Contextual material delivery; reward level conflict |
| Hugel Memory Quest | 11 | Dialogue sequence and intermediate context |
| Gemstone Exchanging | 1 | Optional exchanges and separate inputs/outputs |
| Dropped Knife Quest | 3 | Quest item and EXP outcomes |
| Prontera Culverts Entrance Quest | 2 | Reward items are not requirements |
| Amatsu Dungeon Entrance Quest | 7 | Nine Tails creature/item homonym; own-character permit |
| Ayothaya Dungeon Entrance Quest | 10 | Two crafting stages; box retained without a second grant |
| Turtle Island Entrance Quest | 2 | Exact answer and fee per journey |
| Abyss Lake Entrance Guide | 4 | Materials omitted by old extractor; timed portal; separate Columns |
| Lost Child Quest | 13 | Two Idle Merchants; target Kid versus pond Kid; conditional prerequisite |
| Rachel Sanctuary Quest | 15 | Server donation target; two EXP installments; exact/wrong answers |

A filled localized row does **not** certify complete quest mechanics or server
availability. Quest review states deliberately remain `source_reviewed_partial`.
The source-derived evidence tier is distinct from human review and gameplay
verification; there are zero server-verified records.

## Implemented in this checkpoint

- Normalized entities, locations, actions, items, monsters, requirements,
  rewards, dialogues/options, evidence, review queue and description spans.
- Evidence-based event selection also filters the older research consumer
  projection and core worklist; archived data remain in the separate archive.
- All curated sources are pinned to both complete extracted guide metadata
  and original HTML SHA-256. Changed requirements, reward headers, steps or
  HTML require explicit re-review. Extra/missing steps and duplicate pinned
  step IDs fail the build instead of silently dropping data.
- Exact game names and dialogue choices are preserved in both full locales;
  numeric and entity-presence parity is checked for short and full pairs.
- Same-spelling NPCs have separate explicit curation references. Per-occurrence
  span overrides distinguish a monster from an item and a target from an NPC
  the player should avoid. High Priest Zhed is shared across the two Rachel
  guides with source-qualified identity; different NPCs are never merged just
  because their display names match.
- Source-section facts retain evidence from requirements, rewards or original
  HTML. Abyss Lake's three materials come from the pinned article introduction,
  which the legacy numbered-step extractor omitted.
- Fixed legacy coordinate evidence that silently lowercased `Hugel` to
  `hugel`, inventing map certainty and invalidating the source adjudication
  worklist. Literal spellings are now preserved in both matching paths, and
  offline rebuilds refresh unverified coordinate hints from unchanged source
  prose. All existing source decision gates remain strict.
- Builds use per-quest cached extraction plans and replace semantic SQLite
  only after validation. Failed review gates close SQLite connections before
  retrying, preventing a stale temporary journal from causing an I/O failure.
- Human-reviewed database rows are protected from silent replacement. Export
  corrections to the source-pinned curation ledger before rebuilding.

## Reproduction and checks

Run from the repository root with Python 3 and `requests`, `beautifulsoup4`,
`pyyaml` installed:

```bash
python research/quest-database/semantic.py
(cd research/quest-database && python -m unittest discover -p 'test_*.py' -q)
python research/quest-database/build.py --out research/quest-database/output --enrich-only
```

`semantic.py` needs no network and opens the source archive read-only. Full
offline enrichment regenerates derived research tables and both independent
research projections; it does not update the application's runtime database.
`output/semantic_report.json` is authoritative for current counts and integrity.
`output/semantic_review_queue.json` records unresolved facts with source links.
`output/semantic_examples.json` contains all curated guides and their relations.

Validated locally: **83 tests passed**; complete offline enrichment passed;
the exact SQLite validation script from `quest-research.yml` passed for the
archive and both projections. All integrity checks are `ok`, with zero
foreign-key violations. Original script bytes, guide metadata and original
step text equal the previous committed archive (939 / 286 / 3,420).

The regression suite includes repeated-build byte identity/cache reuse,
unchanged original rows, event selection, context witnesses, original names,
exact answers, source pinning, failed-build preservation, homonyms, source HTML
requirements, per-trip cost, server-wide donations and split reward totals.

## Next work, in order

1. Review the remaining 205 catalog candidates, separating reference pages,
   duplicate guides and genuine quests without deleting original source rows.
   Enrich full quest chains, not isolated easy sentences.
2. **Biolabs Entrance Quest** is a concrete parser/context problem: the old
   extractor appended the alternate-server barrel puzzle after the exit text
   in step 22. That text must be attached to the puzzle branch around step 16,
   while preserving the original extraction and variant evidence. Original
   HTML has been captured, but this guide is not counted as curated.
3. Continue larger access/story chains (Nameless Island, Veins, Thor Volcano,
   Kiel Hyre), with source-backed item roles, dialogue choices and prerequisites.
4. Resolve source spelling conflicts (Boonthon/Boomthon, Dusit/Dustit,
   Phoebe/Phobe, Nema/Nemma); do not silently invent canonical names/IDs.
5. Normalize optional donation lottery rules separately from guaranteed
   Rachel quest rewards. Review image permissions and attach sourced route
   diagrams for Ayothaya/Rachel; image absence is allowed, fabricated images
   are not.
6. Review the 10 quarantined event statuses and remaining metadata/variant
   conflicts. Add tested migrations when the already-versioned schema evolves.

No in-game check was performed. No quest is promised to work on SpringRO or
any other server solely because it appears in wiki/emulator sources.
