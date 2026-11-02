# ROQuestie — shared Ragnarok Online quest data contract

> **2026-10-31 superseding database-scope decision:** See [SEMANTIC_DATA_SPEC.md](./SEMANTIC_DATA_SPEC.md). Current work is the normalized **bilingual RU/EN semantic quest database** with immutable original game names and no archived event-only quests in active working/catalog outputs. RoAura integration is postponed. Historical statements below about event retention or integration describe earlier research, not current work authorization.

**Objective:** a maximally complete, source-backed, versioned **ROQuestie** knowledge base for main Ragnarok Online quests across the wider RO ecosystem. The shared quest data are reusable, while Classic/Pre-Renewal/Renewal and optional individual-server differences are handled separately. A quest in the catalog is **not a guarantee that every server offers it**. This contract belongs to the standalone research work; it must not modify RoAura runtime code during collection.

## Normalized semantic research export — schema 5 (current data phase)

`output/quest_semantic.sqlite` is separate from the older research consumer
catalog (`questie_catalog.sqlite`, schema 5). It is not integrated into RoAura.
Check its own `PRAGMA user_version = 5`; apply migrations `001_semantic.sql`,
`002_step_branches.sql`, `003_source_additions.sql`, `004_quest_routes.sql`, then `005_prerequisite_groups.sql`
under `migrations/`. See
[SEMANTIC_PROGRESS.md](./SEMANTIC_PROGRESS.md). Both active catalogs exclude
archived/quarantined quest records and source-reviewed reference indexes; `quest_research.sqlite` is the raw audit archive.

- Stable quest keys match the wiki URL. Uncurated step keys include original
  instruction hash and duplicate occurrence. Curated `step_id` values are
  explicitly pinned and must be preserved during approved source revisions.
- `entities` plus `npcs`/`maps`/`items`/`monsters` provide typed IDs. Unknown game
  IDs are NULL. Same spelling is not sufficient to merge NPC identities.
- Curation `non_entity_mentions` suppresses a reviewed surface throughout one
  entry when it names an organization/quest rather than a catalog entity. A
  reason and actual matching text are required; it cannot conflict with
  `mention_entities`. This controls stored spans, not UI keyword matching.
- Literal instance map codes can include a numeric floor and `@`. Recognition
  does not imply the map exists in the sampled emulator or selected server.
- Supplemental typed requirements such as `monster_population`,
  `monster_spawn` and `item_disabled` describe context, not compulsory player
  costs. Companion item/monster relations use schema-supported operations;
  unsupported curated operations stop the build rather than disappearing.
- Dungeon population, conditional extra spawns and per-visit combat counts
  are distinct from total kill objectives. Timers retain their typed scope;
  crafting-route costs are not entrance requirements. Read source-variant
  warnings and uncertainty before offering server-specific directions.
- `step_descriptions` holds four independent fields. `description_mentions`
  uses half-open Unicode code-point offsets `[start_offset, end_offset)` in
  the indicated field, not UTF-16 offsets or byte indices. A future C# reader
  must convert code points to UTF-16 indices before slicing. `surface` must
  equal that exact substring. Never rediscover links with a UI keyword search.
- Curation `ref` separates homonymous entities, `mention_entities` selects a
  step's default identity, and `mention_overrides` selects one occurrence
  by field/surface/1-based occurrence. Unresolved ambiguity fails closed.
- `requirements.kind` distinguishes personal costs, total material summaries,
  time limits and server-wide donation conditions. Do not add a quest total
  to the per-step material rows: totals summarize the same obligations.
- Intermediate quest items are recorded as rewards with conditions. Possess,
  bring, deliver, consume and keep are not interchangeable. Quest-level and
  stage rewards must not be blindly summed without reading their conditions.
- `base_exp_total` is a source header summary, not a payable installment.
  Peace for Arunafeltz has two `base_exp` grants with NULL amounts and a known
  total; do not infer their split. A slot suffix such as Knife [3] is part of
  the item label, not quantity. Same-label File documents may have distinct
  IDs for the original and forged versions, with explicit mention bindings.
- Level-reward choice groups belong to their own installment. Lovers stages
  4 and 9 each select one band; do not reuse an earlier level selection or add
  their conditional header totals as another grant. Dynamic own-character-name
  prompts are `enter_character_name` actions without fixed correct options.
  A missing-NPC wait suggestion is distinct from a retry cooldown; a Bingo
  player total is distinct from additional players and does not imply winning.
- `evidence` and `evidence_context` distinguish literal text, contextual
  witnesses and confidence. `source_section` in curation may cite exact header
  text or normalized text extracted from pinned HTML; the HTML bytes and hash
  remain separately available. No server-verified evidence is created.
- `quest_routes` and `step_routes` describe conditional groups spanning
  multiple preserved source steps. `kind=alternative` requires a choice group
  scoped to that quest; select one route in the group. `kind=optional` has no
  choice group and describes an independent activity. Unassigned steps are
  shared. Route membership conditions apply to all step facts, including
  materials and rewards; do not sum mutually exclusive deliveries/grants.
  `route_order` orders members within a route. These relations supplement
  source order but do not claim a complete executable gameplay graph.
- `step_locations.location_order` is a reviewed visit sequence when present,
  not an ordering inferred automatically from every textual occurrence.
  NULL means no established sequence. `condition_text` identifies alternative
  trigger spots, one-time activations and similar constraints. A building
  entrance may need to precede an interior trigger even if the article prints
  the interior coordinates first. Map/point verification remains separate.
- `selection_decisions.json` contains pinned source reviews for nonquest pages.
  A reference exclusion needs both the archived content hash and pinned HTML
  plus a literal witness; changed or missing evidence fails the build. The
  original source remains in the raw archive and selection audit, with no
  dangling associations in active projections.
- `step_branches.choice_group` groups mutually exclusive alternatives at a
  step. `variant_scope` and `condition_text` explain when an alternative
  applies; no branch is selected by default and no mechanism (random versus
  player choice) is invented. `branch_descriptions` and `branch_mentions`
  have the same locale and Unicode span rules as step descriptions.
- Facts in `step_actions`, `step_items`, `step_monsters`, `requirements`,
  `rewards` and `dialogues` carry nullable `branch_id`. NULL denotes common
  step/quest context; a non-NULL value applies only to that branch. Filter by
  branch before reading `action_order`; never execute or sum every alternative.
  NPC/location relations remain a union of contextual step references.
- `source_fragments` uses half-open Unicode offsets into the immutable
  `quest_steps.original_instruction` of `origin_step_id`, independently of
  `target_step_id`. A `partition_key` identifies a complete nonoverlapping
  partition of that original step. Biolabs step 22's exit and alternate puzzle
  are separated here; its original bytes, stable ID and audit text remain.
  Branch fragments cite the actual source step, not their semantic target.
- `step_source_additions` holds ordered literal English fragments omitted by
  older extraction, their Russian translations, source evidence and review state.
  The original instruction/hash remains immutable. The full EN description is
  original text (or its approved primary partition) plus ordered additions;
  full RU contains their translations. Existing entity spans use final displayed
  descriptions. Additions are not new steps and are not alternative branches.
  The source fragment must occur in pinned HTML; never invent missing instructions.
- Apply `migrate_semantic(connection)` to upgrade a committed schema-1, schema-2, schema-3 or schema-4 export
  without replacing human descriptions or IDs. Migration refuses an active
  transaction and rolls back on error. Regeneration is a separate operation
  and refuses human-reviewed step, branch, source-addition, quest-route or prerequisite-group rows until exported
  to curation.
- `step_edges.kind = source_order_only` is article order, not an executable
  route/branch graph. Raw prerequisites do not establish AND/OR semantics.
- `step_edges.kind = repeat_actions_from` references an earlier step in the
  same quest for a repeated action sequence. It is an explanatory reference,
  not a progression loop. Evidence cites the current instruction and links
  the earlier instruction through `evidence_context`. New Surroundings step
  10 reuses step 9 for the other post; neither the link nor NULL per-click
  quantities create a second independent material total.
- Reviewed `quest_relations.kind = mandatory_prerequisite` currently accepts
  only a single exact quest name in the owner's prerequisite header, with
  quest-level `curated_single_prerequisite` evidence. Generic
  `prerequisite_boolean_unspecified` links remain separate source mentions;
  do not treat them as additional mandatory obligations. Other multiquest expressions
  require further review.
- `source_concurrency_constraint` links a source-described conflict with
  another quest. `extra_stage_required_for_prerequisite` links the owning
  quest to the later quest that requires its additional stage. Both use
  `curated_quest_relation` evidence whose `step_id` points to the literal
  source condition. For Attitude, Report eligibility requires the horn stage
  16 despite the displayed reward at step 15. These links do not establish
  executable quest flags or a complete prerequisite expression.
- `quest_prerequisite_groups.minimum_completed` is a reviewed threshold
  over distinct `quest_prerequisite_members.related_id` values. Member order
  preserves the source header list, not a compulsory progression order. Report
  requires at least two of its four named quests; completing more than two
  does not disqualify a player. The exact threshold/list/header evidence are
  validated together. No UI prose parsing is needed. Check member-specific
  `extra_stage_required_for_prerequisite` relations too: Attitude must include
  stage 16 to count toward Report. Generic unspecified links do not override
  the reviewed group or turn every member into a mandatory quest. This is
  source logic, not an in-game completion/flag evaluator.
- Phase-qualified `quest_relations` use `required_started_quest` and
  `required_completed_quest` for literal Started/Finished header clauses.
  `required_quest` retains an unqualified clause without inventing a flag
  phase. These reviewed component lists are conjunctions; exact full-header
  coverage is checked, and threshold headers cannot be imported through this
  path. Evidence method is `curated_prerequisite_clause`, with a literal
  clause and no step ID.
- `stage_required_to_start` points from a prerequisite quest to the quest
  whose start needs a milestone; its evidence step is that milestone. Two
  Tribes stage 7 permits starting Guardian, while the rest of Two Tribes is
  separate. Do not replace this condition with full quest completion. This
  remains source logic rather than a verified runtime flag evaluator.
- `completion_required_at_step` links the owning quest to a prerequisite
  whose completion is explicitly required at the evidence step in the owner.
  President → Einbroch Murder cites President stage 22. This is distinct from
  requiring completion before accepting the owning quest. It uses the same
  literal-step evidence checks as other curated contextual relations.
- `source_zeny_summary` is a preserved header summary, not an additional
  payment. Class-conditional `zeny_per_drink` values and optional purchase
  facts must be interpreted with their branch conditions. Unknown bottle
  prices and repeat-attempt totals must not be filled by subtracting from
  summary amounts. Repeatable exchange branches do not add main quest costs.
- One stage may have multiple `repeat_actions_from` references. Curation
  `action_reuse` accepts a single object or a list with distinct earlier
  source steps. Read all corresponding edges and their evidence contexts;
  do not turn them into executable progression or multiply inventory costs.
  Medicine 8 reuses both 6 and 7, with further retries of unknown quantity.
- Source primary fragments can relocate quoted instructions between stages
  in one quest while retaining the original rows. Friendship source 4 holds
  the message actually entered at 5; its six dialogue options cite pinned
  HTML and the source fragments map the same literals. An unknown return
  message is not filled by copying outgoing text.
- NULL `dialogue_options.correct` denotes an unverified candidate, not a
  guaranteed answer or a sequence to enter in full. Heart Fragment's partial
  list and random code require live prompts. Overlapping source reward bands
  (Friendship at 75) remain review issues rather than automatically resolved.
- A generic item label with no verified game ID does not establish shared
  identity across quests. Use a source-qualified curation `identity` for such
  documents; Airship Ticket's Journal is distinct from Pursuing Rayan Moore's
  same-label Journal. Verified item IDs may still provide shared identity.
- `ready_steps` currently means **all four localized text fields exist**. It
  is not a readiness claim for the quest, route, server or application.
  Unfinished fields are NULL with blocking review rows; `complete=false` is
  explicit in exports. All curated quests still carry partial review state.
- A cache hit reuses the pinned extraction plan, not the whole SQLite file.
  Rebuilding reconstructs and validates relational outputs deterministically.
  A changed guide/HTML pin requires curation review; human-reviewed SQLite
  rows cannot be silently overwritten by generation.

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

## ROQuestie stage 13 — static transition evidence is not automatic navigation

`output/source_transition_audit.json` contains research-only,
non-executed correlations of wiki NPC order to source variable writes and
explicit NPC guard comparisons for the 19 previously reviewed source files.
A positive `literal_write_guard_correspondence_nonexecuted` is a code
relationship; it does not establish that the guarded path is reached,
that the menu choice is correct, or that all quest prerequisites and terminal
conditions are satisfied. A missing `completequest` in the sampled NPCs
does not mean a classic quest lacks completion.

**The consumer SQLite remains schema v5.** The research audit is not
projected into a runnable navigation state machine. Never auto-select
dialogue choices or set per-character quest completion from these reports;
only display literal, cited source facts with the relevant confidence level.

## ROQuestie stage 14 — unresolved-source evidence only

\`output/source_transition_gap_review.json\` and research SQLite v13
\`wiki_gap_reviews\`/\`wiki_terminal_reviews\` store **non-executed**
NPC source evidence: alternative menu options and unknown targets,
source variable write/guard comparisons, literal item checks/grants/
consumptions and \`setquest\`/\`completequest\`/\`erasequest\` evidence with
guard lines. These fields must **not** become automatically selected
dialogue responses, proven required inventories, or completed character
quest flags. A common Quest ID or common item ID in code is not proof that
the same branch executes or that the user's server contains that version.

The end-user catalog remains **schema v5** and no runtime migration is
required for this research iteration. Keep all per-character progress
separate from source research and use a per-server verified ruleset
before enabling next-step navigation.
