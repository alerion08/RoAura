# Semantic database progress — 2026-11-01

Status: **partial research dataset, not ready for application integration**.
Scope: database, sources, parsers, curations, exports and research CI only.
No WPF/application/overlay/settings/release files or `main` changed.

## Verified data scope

The original archive retains 939 NPC scripts, 286 wiki records and 3,420
extracted rows. Active selection retains **216 catalog candidates and 2,750
original rows**; 59 source-confirmed historical event records are excluded
and 10 ambiguous event records are quarantined. One source-confirmed reference
index (`Access Quests`, 3 extracted notes) is excluded from active catalogs. Quarantine is not a claim
that an event is permanently retired. The active catalog still contains
reference pages and unreviewed candidates; 216 is not a count of independently
verified permanent quests.

The normalized `output/quest_semantic.sqlite` uses semantic schema **5** and
generator **semantic-5.1.0**. All four description fields are populated for
**473 steps across 32 curated guides**, up from the previous 28-guide/420-step
checkpoint. **29 conditional branches** also have independent RU/EN short and
full descriptions. Six quest routes represent two exclusive expedition paths
and four conditional/optional activities. **2,277 steps in 184 remaining catalog entries still lack
complete localization/context analysis.** `complete` remains false.

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
| Biolabs Entrance Quest | 22 | Alternate puzzle relocated from exit prose; player-specific codes; uncertain shape mapping |
| Veins Siblings Quest | 20 | Random material consumption; Steel is not consumed; contextual NPCs |
| Nameless Island Entrance Quest | 20 | House-only death penalty exception; prerequisite gate; EXP installments |
| Thor Volcano Base Quest | 22 | Literal oath; five mutually exclusive item rewards; unknown quiver quantities |
| Thor Volcano Level 2 Access Quest | 6 | Optional iRO/premium shortcut; reusable warp versus one-time quest |
| Curse of Gaebolg | 14 | Three final routes; EXP forfeiture order; exact rhyme; lossless source split |
| Kiel Hyre Quest | 75 | Restored sword puzzle; optional advance cards; retry costs; ambiguous keys; approximate rewards |
| Ice Necklace Quest | 7 | Optional summon and sales; weapon slots are not counts; merchant unlock without boss kill |
| Cursed Spirit Quest | 22 | Ordered triggers; separate Representatives; disguise not consumed; four level reward bands |
| Odin's Temple Excavation Quest | 21 | Exclusive Alex/Laura paths; one material set/box; alternative warp points |
| Onward to the New World Quest | 17 | Separate staff/manager identities; ordered gem deliveries; two EXP installments; Classic warning |
| New Surroundings | 22 | Shared two-post material total; repair action reuse; 20 pies across 12 grants; letter identities |
| Attitude to the New World | 16 | Concurrent quest conflict; item versus monster hunt; extra stage required for Report prerequisite |
| Report from the New World | 13 | Minimum two-of-four prerequisite group; alternative scouts; variant timers/failure losses; distinct volumes |
| Pursuing Rayan Moore | 26 | Unknown timed combat count/duration; two EXP installments; optional wrap-up materials and reward |
| Tripatriate Union's Feud | 21 | One invitation with two signatures; unknown bug count; separate repeatable food recipes |
| Finding a Fairy | 4 | Two EXP installments; source prerequisite for the language chain |
| Ring of the Wise King | 14 | Started Report requirement; exact source/non-iRO language inputs; separate Gem actors; one-hour wait |
| Two Tribes | 25 | Equipped ring spelling; three granted plus three gathered berries; all-held removal warning; Guardian start milestone |
| Guardian of Yggdrasil | 10 | Finished versus started prerequisites; independent tribe choices; exclusive rewards; source map typo and iRO warning |

A filled localized row does **not** certify complete quest mechanics or server
availability. Quest review states deliberately remain `source_reviewed_partial`.
The source-derived evidence tier is distinct from human review and gameplay
verification; there are zero server-verified records.

## Implemented in this checkpoint

- Four connected New World guides preserve all 53 source stages and add eight
  localized branches. `prerequisite_clauses` adds reviewed header components
  with typed `required_quest`, `required_started_quest` and
  `required_completed_quest` links. Unqualified clauses do not assert a
  specific flag phase. Exact source clauses and complete header coverage are
  validated; a Started requirement cannot be promoted to Finished.
- `stage_required_to_start` links Two Tribes stage 7 to Guardian. This is
  distinct from completing Two Tribes; Report must explicitly be finished
  for Guardian, but only started for Ring of the Wise King.
- Ring language input is losslessly partitioned into source-default and
  explicit non-iRO branches, preserving all four exact strings. The two Gem
  objects have separate identities. Ring 2782 is locally source-linked; the
  differently spelled Two Tribes ring remains unlinked rather than silently
  aliased. Exact final EXP comes from the reward header, with its evidence,
  while the stage prose itself only says some EXP.
- Two Tribes grants three berries and separately collects three. Snorren
  explicitly takes all held berries if more than six are carried; removal
  quantity remains unknown, not capped at six. Bare coordinates inherit a
  map only through explicit preceding-step witnesses.
- Guardian has independent investigation, assistance and final reward choices.
  One completion gives one EXP/coin reward branch, not both. No literal
  password, token ID or instance party size is invented. Its undated iRO
  completion warning and `spl_fld01`/`spl_fild01` discrepancy stay visible;
  server availability remains unknown. NPC spelling conflicts remain separate.

- Additive schema migration 4 → 5 introduces `quest_prerequisite_groups`
  and `quest_prerequisite_members`. Report has one reviewed minimum-two-of-four
  group, not four mandatory prerequisites. Source header order, membership,
  threshold and evidence are validated together; unsupported group changes
  fail without replacing a valid export. Generic prerequisite mentions remain
  distinct from this reviewed threshold. Attitude's separate stage-16 relation
  still governs its eligibility toward Report. No runtime quest flags are inferred.
- Report's four scouts are alternative contacts, with a separate collective
  textual label rather than an arbitrary individual identity. Default/iRO
  search intervals (30/15 minutes) and failure losses (10/5 pages) are four
  localized branches. Ten pages per attempt does not prove success or a
  forty-page total. Four distinct volumes are needed; completed volumes are
  stored to avoid duplicate-in-inventory failures. Page/volume IDs, delivery
  copy counts and probabilities remain unknown.
- Pursuing Rayan Moore has 200,000+1,200,000 base and 10,000+200,000 job EXP
  installments. Its optional wrap-up (20 Potatoes, 20 logs → 5 Sweet Potatoes)
  is a separate route. Dandelion Members' IDs/count and battle duration remain
  unknown. Generic journals and tracking information are not silently assigned
  inventory IDs or exact dialogue answers.
- Tripatriate's final archived row is losslessly partitioned into main
  completion and three optional repeatable food recipes. Recipe choice is per
  exchange, not a permanent exclusion of other recipes. Main food/kit rewards
  are not duplicated from headers. One invitation is presented for two
  signatures; no second invitation or fixed Thief Bug count is invented.
- Schema-1 through schema-4 migration paths preserve existing human data;
  failed threshold migration rolls back to schema 4. Human-reviewed groups
  cannot be silently regenerated.

- Three additional source-pinned New World guides preserve all 55 original
  stages and four localized fields. Promotional Staff and Alliance Manager
  homonyms remain distinct; Diego is shared across two guides with a
  source-qualified identity. Marian/Marien and Rumis/Lumis spellings remain
  unresolved source conflicts rather than invented aliases.
- New Surroundings uses one shared 20-Branch/20-Vine material total for two
  posts. `repeat_actions_from` links step 10 to the repair sequence in step 9;
  per-click consumption stays unknown. Twelve Chocolate Pie grants sum to 20,
  and the reward header does not create an extra grant.
- Attitude records the Camp Guard conflict with New Surroundings separately
  from ordinary prerequisites. Its 1,500,000 EXP stage is distinct from the
  additional Horn of Tendrilion stage (1,000,000 EXP), which is required when
  counting Attitude toward Report from the New World. The extra stage has a
  conditional route and a quest relation with exact stage-16 evidence.
- Two reviewed single-quest prerequisite headers establish mandatory Onward
  relations. Unreviewed/compound headers retain unspecified Boolean semantics;
  Report's reviewed threshold is stored separately in the schema-5 groups above.
- Onward's undated pinned Classic warning remains visible in review metadata.
  Server-neutral inclusion does not turn it into a verified active Classic quest.
  No game/server availability was promoted.

- Additive schema migration 3 → 4 introduces `quest_routes` and `step_routes`
  for alternatives that span several archived steps and independent optional
  activities. Odin source steps 4–7 (Alex) and 8–12 (Laura) belong to one
  exclusive choice group; step 13 onward is shared. Their rune deliveries
  and boxes must not be added together for one completion.
- Ice Necklace's optional Ktullanux summon and postquest Glacial Hearts sales
  are independent activities, not compulsory steps before the main reward.
  Hammer [2] is slot notation for one linked weapon. Merchant unlock depends
  on steps 1–5, not a boss kill; 375 Zeny is a per-item sale rate.
- Cursed Spirit's EXP uses four exclusive level-band branches. Disguise gear
  is worn and retained; Handcuffs remain held until the evidence stage. The
  exact removal stage is unknown despite the consumed-item header.
- `step_locations.location_order` and `condition_text` distinguish a reviewed
  visit sequence from alternative triggers. Cursed Spirit's building entrance
  precedes its interior trigger; Odin's five possible warp points have no
  compulsory order. Older/unreviewed order values remain NULL.
- `selection_decisions.json` records a source-pinned review of Access Quests:
  its own introduction says it is a reference table. This removes one index
  and three notes from both usable catalogs and the worklist, while retaining
  archive rows and HTML. Changed source hashes or missing proof fail closed;
  similarly titled quests are not automatically excluded.
- Tested migrations preserve human descriptions, source additions and location
  rows. Human-reviewed routes are protected from silent regeneration.

- Additive schema migration 2 → 3 introduces `step_source_additions`. These
  records attach literal text from pinned HTML and its Russian translation
  to an existing step. The archive and stable step IDs remain unchanged.
  English full descriptions are validated as original/partition text plus
  ordered additions; fabricated or missing additions fail the build.
- Kiel Hyre step 30's four sword/snake pairings were omitted by the old
  extractor because the solution uses `<pre>` blocks. The full descriptions
  now include the source-backed solution; four item-use/action records retain
  its order and targets. Future HTML extraction also retains these blocks.
- Curse of Gaebolg step 14 is losslessly partitioned into its introduction
  and three route branches. Historian disclosure can forfeit all EXP if Bonnie
  is visited before Biscuss pays; that route's numeric EXP amount is unknown.
  The two routes that explicitly state 1,600,000 keep separate conditional grants.
- Kiel Hyre records one mandatory initial Black Keycard, three optional
  advance cards, door uses and three retry cards after a party wipe. Per-attempt
  monsters and optional material substitutions are not flattened into quest totals.
  The source's approximate 700,000 installment, unknown final EXP and 1,700,000
  header total remain distinct; no exact remainder is invented.
- Kiehl NPC/combat mentions use separate typed spans. Uncertain Kiel identities,
  Old Bronze/Copper Key, Rosimmer/Rosimer names and Ellisia/Allysia ring names
  retain source spellings; key/ring conflicts have explicit review notes. Literal answer whitespace
  is preserved and marked unverified where the source may contain formatting spaces.
- Schema-1 and schema-2 upgrades preserve human descriptions and branch rows;
  failed migrations roll back. Human-reviewed additions are also protected from
  silent replacement during regeneration.

- Additive schema migration 1 → 2 preserves existing IDs, facts and human
  descriptions; failed migration rolls back schema and version changes.
- `step_branches`, `branch_descriptions` and `branch_mentions` retain branch
  conditions, source scope, four localized fields and typed spans. Actions,
  items, monsters, requirements, rewards and dialogues have optional branch IDs.
  A choice group denotes alternatives, not sequential instructions or a
  default selection. Unqualified NPC/location links remain step context.
- `source_fragments` maps immutable source ranges to their semantic target.
  Biolabs original step 22 is partitioned without gaps or overlaps: the exit
  remains step 22, the non-iRO two-digit puzzle belongs to a branch at step 16.
  Example numbers never become universal correct answers. Source order remains
  article order, not executable gameplay flow.
- Future HTML extraction preserves notes after new headings as `section_note`
  records in document order instead of appending them to the preceding step.
  This does not rewrite the original 3,420 archived rows.
- Five additional pinned HTML pages are now covered by curation (Biolabs was
  already captured; four new snapshots were added). iRO/premium category
  evidence for the Thor shortcut does not establish availability elsewhere.

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
  the player should avoid. High Priest Zhed is shared across the Rachel, Nameless Island and Thor
  guides with source-qualified identity; Karyn is shared across the two Veins
  story appearances; different NPCs are never merged just
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

Validated locally: **135 tests passed**; complete offline enrichment passed;
the exact SQLite validation script from `quest-research.yml` passed for the
archive and both projections. All integrity checks are `ok`, with zero
foreign-key violations. Original script bytes, guide metadata and original
step text equal the previous committed archive (939 / 286 / 3,420).

The regression suite includes repeated-build byte identity/cache reuse,
unchanged original rows, event selection, context witnesses, original names,
exact answers, source pinning, failed-build preservation, homonyms, source HTML
requirements, per-trip cost, server-wide donations, split reward totals, lossless source
partitions, branch corruption, additive/failed migrations, human-reviewed
branch protection, optional rewards and repeatability/death-penalty scope,
source additions, sword order, Gaebolg route rewards, keycard retry quantities
and NPC/combat homonyms. Schema-1, schema-2 and schema-3 migration paths are tested, including rollback
and preserved human additions/locations. Additional checks cover reference-index
selection pins, route membership, optional activities, level rewards and location
order versus alternative points, shared repair totals, repeated action references,
New World reward accounting, item-versus-kill goals, and evidence-backed
prerequisite/concurrency relations. Corrupted links and unsupported header
prerequisites fail without replacing a valid export. Further cases cover
threshold group corruption and migration rollback, source-default/iRO scouting
variants, distinct volumes, optional wrap-up and postquest food recipes. New
checks cover phase-qualified prerequisites, exact language inputs, distinct
gem actors, all-held berry loss, independent tribe choices and source map
spelling conflicts.

## Next work, in order

1. Review the remaining 184 catalog candidates, separating reference pages,
   duplicate guides and genuine quests without deleting original source rows.
   Enrich full quest chains, not isolated easy sentences.
2. Continue remaining permanent access/story chains and related New World
   requirements in whole-quest context. Keep early start milestones separate
   from full quest completion.
   Audit structured entity coverage
   of curated guides. Nameless Island now has a curated Curse of Gaebolg
   prerequisite, but no executable or universally applicable prerequisite graph
   is claimed.
3. Resolve source spelling conflicts (Boonthon/Boomthon, Dusit/Dustit,
   Phoebe/Phobe, Nema/Nemma, Jackknif e/Jackknife, Laboratory Pass/Permit).
   Biolabs shape-menu/input mapping and the single-digit Yellow Key receipt
   remain review items, not silently invented facts.
4. Normalize optional donation lottery rules separately from guaranteed
   Rachel rewards. Review image permissions and attach sourced route diagrams
   for Ayothaya/Rachel; no images are fabricated or automatically licensed.
5. Review the 10 quarantined event statuses and remaining metadata/variant
   conflicts. Keep source applicability distinct from server verification.

No in-game check was performed. No quest is promised to work on SpringRO or
any other server solely because it appears in wiki/emulator sources.
