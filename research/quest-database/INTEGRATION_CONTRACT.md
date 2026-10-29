# Questie-ready data contract for RoAura

**Objective:** one maximally complete, compact, documented, source-backed Ragnarok Online quest dataset, easy to integrate later as a full quest assistant. This contract belongs to the standalone research work; it must not modify RoAura runtime code during collection.

## Two deliverables

- `output/quest_research.sqlite` (research schema **5**): full compressed upstream NPC sources and extracted evidence, public wiki pages, audit rows, parser-derived dialogue/control flow and literal quest/item state facts.
- `output/questie_catalog.sqlite` (consumer schema **1**, `PRAGMA user_version = 1`): ready-to-query local SQLite projection, **without compressed scripts or Python dependency**. The consumer must **not** guess the correctness of unverified responses or use candidates as verified directions.

Both formats are independently checked with `PRAGMA integrity_check` and `PRAGMA foreign_key_check`. Their `.gz` variants compress for download; decompress before SQLite use.

## Stable identities and source variants

- `quests.quest_key = irowiki-classic:<wiki URL pathname suffix>`; it does not depend on a SQL autoincrement ID or row order.
- `scripts.script_key = <provider>:<repository path>`, e.g. `rathena:npc/quests/juice_maker.txt`. Each rAthena/Hercules variant remains separate.
- `npcs.npc_key = <script_key>#<source ordinal>`; **the ordinal is stable only within a particular source revision**. Watch `scripts.git_blob_sha` when refreshing.
- All step numbers remain tied to their quest key. `source_step` contains the wiki's original number when known. Unnumbered paragraphs remain labeled narrative, not proven steps.
- The export and all text fields retain source language, not assumed game localization.

## Key consumer tables

| Area | Tables | Behavior |
| --- | --- | --- |
| Quest index | `quests`, `quest_steps`, `step_hints` | Ordered walkthrough and literal guide hints |
| Variant link | `scripts`, `quest_scripts` | Candidate script source and confidence method |
| Position | `npcs`, `quest_step_npcs` | Sourced map coordinates, unverified against ToC |
| Dialogue | `dialogue_lines`, `dialogue_points`, `dialogue_options`, `dialogue_guards`, `dialogue_jumps` | Literal dialogue, choices, explicit code-line targets, stored guards |
| Quest state | `quest_state_facts`, `quest_registry`, `quest_registry_evidence` | Lexical setquest/completequest/erasequest/checkquest, item operations, variables, matched Quest IDs |
| Answer matching | `candidate_answers` | Heuristic guide/menu text equality; never verified correct answer |

Every script-derived position, line and condition can be traced to `scripts.source_url` and `scripts.git_blob_sha` in the full research database. Keep associated source/license notices when distributing.

## Read-only consumer views

For simple integration without query-builder joins, the runtime SQLite also provides these **views** (no duplicated rows in storage):

- `quest_navigation_candidates`: quest key + step number + NPC map/coordinates + evidence/status.
- `quest_progress_evidence`: quest key + NPC + lexical quest/item/variable actions and guard references. These are **not** proven quest requirements or results.
- `quest_dialogue_candidates`: quest key + linked script NPC + menu options and explicit code-line targets. A candidate must not automatically be treated as the correct dialogue choice.

Read the underlying fields/statuses; never present a `candidate` edge as game-verified. New views can be added in a future consumer schema version, but existing columns require migration discipline.
## Example queries

List stages:

```sql
SELECT step_no,source_step,kind,section,instruction
FROM quest_steps WHERE quest_key='irowiki-classic:Juice_Quest'
ORDER BY step_no;
```

List script variants for one guide:

```sql
SELECT qs.script_key,s.project,qs.score,qs.method,qs.status,s.source_url
FROM quest_scripts qs JOIN scripts s USING(script_key)
WHERE qs.quest_key='irowiki-classic:Kiel_Hyre_Quest';
```

List NPC dialogue choices with possible code branch targets:

```sql
SELECT n.name,n.map,n.x,n.y,o.line_no,o.option_no,o.label,
       o.target_line,o.resolution
FROM npcs n JOIN dialogue_options o ON o.npc_key=n.npc_key
WHERE n.script_key='rathena:npc/quests/juice_maker.txt'
ORDER BY n.source_ordinal,o.line_no,o.option_no;
```

List quest and item state facts (source evidence, *not a gameplay verdict*):

```sql
SELECT n.name,f.line_no,f.role,f.object_id,f.amount_expr,
       f.value_expr,f.argument,f.context_guard_line
FROM quest_state_facts f JOIN npcs n ON n.npc_key=f.npc_key
WHERE n.script_key='rathena:npc/quests/juice_maker.txt'
ORDER BY n.source_ordinal,f.line_no,f.occurrence;
```

## RoAura integration rules

1. Open the consumer database read-only in a separate module. No parser or network requests are required. For .NET/Windows, use the app's existing supported SQLite provider; do not introduce a new dependency without review.
2. Store **per-character** quest progress, UI placement/preferences and manual confirmations in a separate user-owned SQLite/settings store, **never** in the distributed catalog. DB updates must not reset user progress.
3. Treat `quest_scripts`, `quest_step_npcs` and `candidate_answers` as **candidate** links. A high filename score is not a probability, and server mechanics may differ.
4. Treat `quest_state_facts` and `dialogue_guards` as unevaluated code evidence. `countitem(...)` can be a branch check rather than a mandatory quest requirement; `getitem` can be optional loot rather than a guaranteed reward.
5. Do not label a quest step as automatically confirmed until there is reliable, server-specific observable state with explicit validation. Network packet identifiers and quest states can differ by server.
6. Verify `PRAGMA user_version` before importing. For unsupported future schema versions, fall back to existing functionality rather than guessing columns. Use migration tests.
7. Protect compatibility with existing RoAura Database, MVP timers, Statistics and Gold Goblin. No integration is done in this research branch.

## Coverage / unresolved work

Currently source scripts are from selected public emulator directories, not a complete official iRO/ToC NPC installation. iRO Wiki index may contain nonquest pages. Cross-references are largely unverified; dynamic choices and control flow are incomplete. Need broaden source coverage, reduce ambiguous links, model true state-transition paths, and validate against target server recordings/authoritative scripts. See `output/coverage.json` for **actual** counts rather than reading this design contract as a completion claim.
