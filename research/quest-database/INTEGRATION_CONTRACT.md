# ROQuestie — shared Ragnarok Online quest data contract

**Objective:** a maximally complete, source-backed, versioned **ROQuestie** knowledge base for main Ragnarok Online quests across the wider RO ecosystem. The shared quest data are reusable, while Classic/Pre-Renewal/Renewal and optional individual-server differences are handled separately. A quest in the catalog is **not a guarantee that every server offers it**. This contract belongs to the standalone research work; it must not modify RoAura runtime code during collection.

## Shared core and optional server compatibility

ROQuestie is organized around stable quest identities, wiki walkthroughs,
source-backed NPC/dialogue evidence and explicit variants. Main story,
dungeon-access and class-advancement quests are researched first; seasonal
events remain available as historical source records.

`output/roquestie_core_worklist.json` is a complete, heuristic curation
worklist across all indexed guides. Its categories prioritize research rather
than claim universal availability. Every row retains `server_availability:
unknown` and `universal_availability_claim: false` until verified sources
are added for specific server/revision combinations.

Per-server compatibility (when later requested) should be an **optional
overlay** keyed by a canonical quest key, server identifier, revision and
evidence; it must never replace or mutate the shared source catalog.

## Source-cited content, NPC and dependency evidence (schema v4)

ROQuestie builds conservative `wiki_content_signals` and
`wiki_content_categories` from source guide steps and requirements.
Categories are **research candidates**, not mechanics or server availability
claims. The `roquestie_core_worklist.json` export shows the evidence method,
number of supporting steps and remaining unclassified pages. Broad words such
as "quest" or "dungeon" alone are not sufficient evidence.

Step-to-NPC name matching is permitted only when the wiki explicitly says to
talk/speak/meet/visit a **distinctive, unique NPC name** found in a strongly
matched script. These candidates have score 55 and method
`explicit_npc_name_in_candidate_script`; they are weaker than exact map and
NPC-coordinate evidence. Different source variants remain separate.

`quest_step_locations` in the read-only catalog exposes structured waypoint
coordinates **directly documented in the wiki**, even without a matched NPC:
`map` is NULL when the source only gives bare `x,y`. They must not be
presented as verified NPC locations or assigned an inferred map.

`quest_content_categories` and `quest_content_signals` preserve literal
guide evidence and provisional classification. `quest_dependency_paths`
contains direct or transitive **wiki page references**, with minimum hop
count; it does not imply AND/OR prerequisites, a quest journal state or that
all linked quests exist on a chosen server. Cyclic references are diagnosed.

**Migration:** schema v4 adds these tables without changing v3 quest keys.
Open `PRAGMA user_version` and migrate the reader explicitly. User quest
progress belongs in separate per-character storage.

## Individually reviewed source implementations (schema v5)

`quest_source_decisions` is a distinct, source-SHA-pinned review ledger for
newly discovered quest↔script proposals. Each row is
`source_supported`, `rejected`, `variant_conflict` or `needs_review`,
with a specific explanation and reviewer scope. The read-only
`quest_source_supported` view selects **only source-supported mappings**,
including source project and Pre-Renewal/Renewal scope.

Source-supported means that a human source-code review found a plausible
file implementing the named wiki quest, with NPC step order and code-state
evidence. It does **not** claim that every dialogue branch, Quest ID, reward,
prerequisite or server variation is correct. Never treat this view as a
quest-completion detector or silently merge it into automatic route advice.

The old `quest_scripts` table remains the unchanged set of 574 heuristic
source candidates, so a consumer can distinguish the two confidence tiers.
`source_decisions.json` pins exact source blob SHAs and wiki URLs; the
build fails closed on changed snapshots and requires re-review. Research
`output/source_adjudication.json` retains the wiki step-to-NPC sequence,
relevant variable writes and source guards for each of the 25 decisions.
`source_code_execution_order_verified: false` is intentional: a lexical
script search is not an executable branch trace.

**Migration to v5:** check `PRAGMA user_version = 5`, read the separate
source-review tables explicitly and retain all per-character progression data
outside the catalog.

## Two deliverables

- `output/quest_research.sqlite` (research schema **7**): full compressed upstream NPC sources and extracted evidence, public wiki pages, audit rows, parser-derived dialogue/control flow and literal quest/item state facts.
- `output/questie_catalog.sqlite` (consumer schema **5**, `PRAGMA user_version = 5`): ready-to-query local SQLite projection, **without compressed scripts or Python dependency**. The consumer must **not** guess the correctness of unverified responses or use candidates as verified directions.

Both formats are independently checked with `PRAGMA integrity_check` and `PRAGMA foreign_key_check`. Their `.gz` variants compress for download; decompress before SQLite use.

## Data quality and safe refresh rules

- `output/quest_review_queue.json` is a machine-readable manual verification queue: each wiki quest carries freshness, missing-script/missing-NPC issues, mixed gameplay variants, and an explicit unverified assurance state.
- Consumer `quest_quality` records each wiki guide's `refresh_state`, `last_success_utc`, `last_attempt_utc`, `last_error`, and `stored_steps`. An imported older guide is labeled `legacy_cached` with **unknown last success time**, never guessed fresh.
- Source script `variant_scope` is `common`, `pre-re`, or `renewal`; a mix of them must never be silently selected as one active server variant.
- The crawler retries transient failures and rechecks previously discovered pages. Failed requests retain last-known-good steps/content hash and are flagged `cached_http_error`. Unexpectedly empty/shrunken parses retain the earlier walkthrough and are flagged `cached_parse_regression`.
- New incremental snapshots must pass a no-regression gate before publishing: no unexpected loss greater than 5% of source scripts/steps or 2% of wiki pages. This protects storage completeness, **not** factual correctness; report flags and in-game verification remain mandatory.
- Consumer schema v2 introduced `quest_quality` and `scripts.variant_scope`. Never open v2/v3 as if it were v1: integrations must check `PRAGMA user_version`, use a versioned reader/migration, and preserve character progress separately.

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
| Position | `npcs`, `quest_step_npcs` | Sourced map coordinates, not validated for a particular server |
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
### Explicit wiki prerequisites

`wiki_quest_prerequisite_text` retains the exact section following
`Quest Prerequisite(s):` from source wiki requirements. References to **known
wiki page titles** are extracted into `wiki_quest_prerequisite_links` only when
a full bounded title match exists. An unmatched requirement remains available
as raw text for future research. Boolean expression logic is *unspecified*;
a comma or an "or" is not automatically converted to executable conditions.

The consumer SQLite v3 exports `quest_prerequisite_text` and
`quest_prerequisites`. Both are source-documented statements,
**not confirmation that any particular server enables these quests**.

### Candidate Quest IDs (not live quest progress)

The `quest_id_candidates` SQL view connects a guide's *candidate* script with literal
quest ID operations in the original NPC code, matching a Quest ID to source
registry rows, source line, NPC and script. This is **not** proof that an ID
uniquely belongs to a quest or that a quest is enabled on a particular server.

```sql
SELECT quest_key,script_key,quest_id,registry_variant,role,line_no,assurance
FROM quest_id_candidates WHERE quest_key='irowiki-classic:President_Quest';
```

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

Currently source scripts are from selected public emulator directories, not a complete installation of every Ragnarok Online server. iRO Wiki index may contain nonquest pages. Cross-references are largely unverified; dynamic choices and control flow are incomplete. Need broaden source coverage, reduce ambiguous links, model true state-transition paths, and validate against target server recordings/authoritative scripts. See `output/coverage.json` for **actual** counts rather than reading this design contract as a completion claim.
